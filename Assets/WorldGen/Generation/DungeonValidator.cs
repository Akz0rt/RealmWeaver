using System.Collections.Generic;

namespace WorldGen.Generation
{
    public enum IssueSeverity { Error, Warning }

    public class DungeonIssue
    {
        public IssueSeverity Severity;
        public int LevelIndex;         // 0-based
        public string Message;         // Russian, human-readable
    }

    /// <summary>Read-only rule checks over a dungeon graph. No Unity types — headless + self-testable.</summary>
    public static class DungeonValidator
    {
        public static List<DungeonIssue> Validate(InteriorData dungeon,
            int minBossDistance = DungeonGraphGenerator.DefaultMinBossDistance)
        {
            var issues = new List<DungeonIssue>();
            if (dungeon == null) return issues;

            // Boss rooms are a DUNGEON concept (a building has no boss). And a building's outside entrance is
            // ONLY on floor 0 — upper floors have a stairwell (Лестница), not a Вход — so the "exactly one
            // entrance" rule applies per-floor for dungeons but only to floor 0 for buildings.
            //
            // THE one source of truth for "does this interior have a boss rule". InteriorProfile used to carry
            // a HasBossRule flag saying the same thing, consulted by nobody — it was deleted rather than wired
            // in here, because this validator is HEADLESS and InteriorProfile lives in WorldGen.Rendering
            // (ThemeRole → UnityEngine), so the profile can never be the source for this file.
            bool isBuilding = dungeon.Kind == InteriorKind.Building;

            // A SETTLEMENT reuses this graph but none of these dungeon rules apply: its gates are not "the one
            // entrance", it has no boss, no stairwell, and its rooms are not meant to be corridor-reachable
            // from anything. Every dungeon check below stays gated OFF for it — those early-outs are correct
            // and are NOT what this flag replaced. What a settlement has INSTEAD is its own rule block
            // (SettlementIssues, called at the end of each floor pass below): the FOOTPRINT rules.
            bool isSettlement = dungeon.Kind == InteriorKind.Settlement;
            bool bossRule = !isBuilding && !isSettlement;

            for (int li = 0; li < dungeon.Floors.Count; li++)
            {
                var lvl = dungeon.Floors[li];
                int human = li + 1;

                // TypeId 2 is the Boss for a DUNGEON but the Лестница (stairwell column) for a BUILDING — the two
                // never coexist (Kind decides which rules below apply), so one counter serves both. The id is
                // named for the TYPE, not for either meaning: calling it `bossId` read as a leaked dungeon
                // concept at the BUILDING sites below (the orphan root upstairs is the Лестница, not a boss).
                int entrances = 0, entranceId = 0, typeTwo = 0, typeTwoId = 0;
                foreach (var r in lvl.Rooms)
                {
                    if (r.TypeId == 0) { entrances++; entranceId = r.Id; }
                    if (r.TypeId == BuildingGenerator.StairTypeId) { typeTwo++; typeTwoId = r.Id; }
                }

                if ((!isBuilding || li == 0) && !isSettlement && entrances != 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: должен быть ровно один вход (сейчас {entrances}).");
                if (bossRule && typeTwo > 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: не более одной комнаты босса (сейчас {typeTwo}).");
                if (bossRule && typeTwo == 0)
                    Add(issues, IssueSeverity.Warning, li, $"Этаж {human}: нет комнаты босса — глубже только через секретный ход.");
                // Building stairwell: a multi-floor building carries exactly one Лестница per floor (the shared
                // column). A missing or duplicate one means the vertical shaft is broken.
                if (isBuilding && dungeon.Floors.Count > 1 && typeTwo != 1)
                    Add(issues, IssueSeverity.Error, li, $"Этаж {human}: должна быть ровно одна лестница (сейчас {typeTwo}).");

                var adj = BuildAdj(lvl);
                // Boss distance (dungeon only, entrance present).
                if (bossRule && typeTwo == 1 && entrances == 1)
                {
                    int d = Distance(entranceId, typeTwoId, adj);
                    if (d >= 0 && d < minBossDistance)
                        Add(issues, IssueSeverity.Warning, li,
                            $"Этаж {human}: комната босса слишком близко ко входу ({d} шаг(ов), нужно ≥ {minBossDistance}).");
                }
                // Orphans: rooms unreachable from the floor's ROOT via corridors. Dungeons and a building's floor 0
                // root at the entrance; a building UPPER floor has no Вход, so it roots at the Лестница (the stair
                // arrival) — otherwise deleting a corridor upstairs could strand a room with no warning.
                int rootId = (isBuilding && li > 0) ? typeTwoId : entranceId;
                if (rootId != 0 && !isSettlement)
                {
                    var reached = Reachable(rootId, adj);
                    int orphans = 0;
                    foreach (var r in lvl.Rooms) if (!reached.Contains(r.Id)) orphans++;
                    if (orphans > 0)
                        Add(issues, IssueSeverity.Warning, li,
                            $"Этаж {human}: {orphans} комнат(ы) недостижимы от {(isBuilding && li > 0 ? "лестницы" : "входа")} по коридорам.");
                }

                // Dangling inter-floor targets. A secret door or a stairwell must point at a room that still
                // exists on an existing floor — "no stairs to nowhere" (a level removal / hand-edit could strand it).
                foreach (var r in lvl.Rooms)
                    foreach (var s in r.Portals)
                    {
                        if (s.Kind == PortalKind.SecretDoor)
                        {
                            if (!TargetExists(dungeon, s))
                                Add(issues, IssueSeverity.Error, li,
                                    $"Этаж {human}: секретный ход из комнаты {r.Id} ведёт в несуществующую комнату.");
                        }
                        else if (s.Kind == PortalKind.Stairs)
                        {
                            if (!TargetExists(dungeon, s))
                                Add(issues, IssueSeverity.Error, li,
                                    $"Этаж {human}: лестница из комнаты {r.Id} ведёт на несуществующий этаж.");
                        }
                    }

                // The settlement's OWN rules, in place of every dungeon rule this floor just skipped.
                if (isSettlement) SettlementIssues(issues, lvl, li);
            }

            // Building SHAFT integrity: every Лестница must share the floor-0 column's (x,y) AND footprint — one
            // vertical shaft. Floor 0 is free-edit, so moving/resizing the column there desyncs the upper floors
            // (they are pinned to the column only at generation time); flag it so the DM re-generates the floor.
            if (isBuilding && dungeon.Floors.Count > 1)
            {
                var col0 = FindStair(dungeon.Floors[0]);
                if (col0 != null)
                    for (int li = 1; li < dungeon.Floors.Count; li++)
                    {
                        var colF = FindStair(dungeon.Floors[li]);
                        if (colF == null) continue;   // a missing column is already flagged per-floor above
                        bool aligned = System.Math.Abs(colF.X - col0.X) < ShaftTol
                                    && System.Math.Abs(colF.Y - col0.Y) < ShaftTol
                                    && colF.SizeW == col0.SizeW && colF.SizeH == col0.SizeH;
                        if (!aligned)
                            Add(issues, IssueSeverity.Error, li,
                                $"Этаж {li + 1}: лестница не совпадает со столбом 1-го этажа — перегенерируйте этаж.");
                    }
            }
            return issues;
        }

        /// <summary>THE SETTLEMENT RULES — what a town has INSTEAD of the dungeon rules its Kind gates off.
        /// A settlement's rooms are FOOTPRINTS on <see cref="SettlementFootprint"/>'s fixed absolute lattice,
        /// and these four rules are the whole statement of "this floor's footprints are well formed".
        ///
        /// BUILDINGS ONLY (TypeId == 1), every rule, and that scope is load-bearing rather than tidy. A GATE
        /// room (TypeId 0) carries a one-cell footprint too since v11 (SettlementGenerator.BuildFloor stores
        /// its ring cell so the recentring migration, which moves a town by moving CELLS, cannot leave the
        /// gates behind) — but that cell IS a street cell BY CONSTRUCTION ON A FRESHLY GENERATED TOWN: a gate
        /// is a ring-street cell picked by SettlementBlocks.PlaceGateCells, which is exactly what makes it a
        /// gap in the wall the streets run out through. Rule 4 applied to gates would therefore fire on EVERY
        /// gate of EVERY freshly generated town — measured: 360 gates over 120 towns, all three size classes,
        /// 360 of them on a street cell. QUALIFIED since the block-forms/gates arc's Task 2 (gate drag): after
        /// a drag, the gate room's stored cell is instead a Wall/Gate cell (the drag normalizes it there — see
        /// SettlementTileGrid's GateRoomAt doc), which is not a street cell either, so a dragged gate would
        /// not trip Rule 4 even if Rule 4 were widened to cover it — the exclusion's CONCLUSION stands, only
        /// the "always a street cell" premise behind it has narrowed to "freshly generated". A rule that fires
        /// on every correct freshly-generated town is not a rule, it is noise that teaches the DM to ignore
        /// the panel.
        ///
        /// WHAT THIS SCOPE GIVES UP, stated rather than hidden: a GATE dropped onto a building's footprint is
        /// not reported here. It is still refused at the edit — SettlementVolumeRenderer.RebuildCellRooms maps
        /// cells to rooms with NO TypeId filter, so AreCellsFree rejects gate-onto-building, building-onto-gate
        /// and building-onto-building through one term — and the ONLY way to reach the state is a hand-edited
        /// save. Rule 3 below is the data-side twin of that same predicate for the building case.
        ///
        /// TWO DIFFERENT READS OF THE FOOTPRINT, DELIBERATELY:
        ///   • Rules 1-2 (shape) read the STORED array through SettlementFootprint.Decode. They are statements
        ///     about what is ON THE WIRE. Reading them through SettlementTileGrid.FootprintOf instead would
        ///     make rule 1 STRUCTURALLY VACUOUS — FootprintOf's rule (a) substitutes the room's point cell for
        ///     a missing footprint and so never returns an empty list, so "the footprint is non-empty" could
        ///     not fail however broken the data was.
        ///   • Rules 3-4 (overlap, street) read SettlementTileGrid.FootprintOf. They are statements about what
        ///     is DRAWN and CLICKABLE, and FootprintOf is the single canonical read every renderer and the
        ///     drag verdict already share, so a cell this rule calls contended is the same cell the tile grid
        ///     paints and HitRoomId resolves. Nothing here may second-guess it.
        ///
        /// SEVERITIES. Empty/disconnected/overlapping are ERRORS: each one means the stored town cannot be
        /// drawn as what it claims to be (a building with no cells, a building in two pieces, two buildings
        /// contending for one cell — the last leaves one of them permanently hidden behind the other and
        /// unclickable, SettlementVolumeRenderer.Precedes decides which). Standing on a street is a WARNING:
        /// the town still renders correctly (the tile grid gives Building precedence over Road), the house is
        /// merely blocking its own street — a planning complaint, not a broken map, and the one of the four a
        /// DM reaches by ordinary dragging.</summary>
        static void SettlementIssues(List<DungeonIssue> issues, InteriorFloor lvl, int li)
        {
            int human = li + 1;
            var streets = new HashSet<(int i, int j)>(
                SettlementFootprint.Decode(lvl.SettlementParams?.StreetCells));
            // cell -> the FIRST building (in floor order) that claimed it, so a contended cell names a stable
            // pair of ids rather than whichever two the enumeration happened to visit last.
            var claimed = new Dictionary<(int i, int j), int>();

            foreach (var r in lvl.Rooms)
            {
                if (r == null || r.TypeId != 1) continue;   // BUILDINGS ONLY — see the scope note above

                // ---- rules 1-2: the STORED shape ----------------------------------------------------
                var stored = SettlementFootprint.Decode(r.Cells);
                if (stored.Count == 0)
                    Add(issues, IssueSeverity.Error, li,
                        $"Этаж {human}: у здания {r.Id} нет ни одной клетки — отпечаток пуст.");
                else if (!SettlementFootprint.IsConnected4(stored))
                    Add(issues, IssueSeverity.Error, li,
                        $"Этаж {human}: отпечаток здания {r.Id} распадается на части — клетки {Describe(stored)} не связаны по стороне.");

                // ---- rules 3-4: what is DRAWN --------------------------------------------------------
                // Each rule's DECISION is its own single statement (`contended`, and the streets test) rather
                // than being folded into the branch that reports it. That shape is what lets the non-vacuity
                // harness remove exactly one rule per mutant with a one-line rewrite —
                // MutValidatorNoOverlapRule and MutValidatorNoStreetRule each neuter one of these two lines.
                foreach (var c in SettlementTileGrid.FootprintOf(r))
                {
                    bool contended = claimed.ContainsKey(c);
                    if (contended)
                        Add(issues, IssueSeverity.Error, li,
                            $"Этаж {human}: здания {claimed[c]} и {r.Id} занимают одну клетку ({c.i}, {c.j}).");
                    else claimed[c] = r.Id;

                    if (streets.Contains(c))
                        Add(issues, IssueSeverity.Warning, li,
                            $"Этаж {human}: здание {r.Id} стоит на улице — клетка ({c.i}, {c.j}).");
                }
            }
        }

        /// <summary>A footprint's cells as "(i, j) (i, j) …", for an issue message that has to name the EXACT
        /// offending shape rather than its size. Uncapped on purpose: the only footprints that reach it are
        /// ones rule 2 has already found broken, and a hand-edited save's few stray cells are precisely what
        /// the DM needs spelled out.</summary>
        static string Describe(List<(int i, int j)> cells)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < cells.Count; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append('(').Append(cells[k].i).Append(", ").Append(cells[k].j).Append(')');
            }
            return sb.ToString();
        }

        // Normalized-coordinate tolerance for "same column position" — matches the ToNorm/ToTile round-trip.
        const float ShaftTol = 1e-3f;

        static Room FindStair(InteriorFloor lvl)
        {
            foreach (var r in lvl.Rooms) if (r.TypeId == BuildingGenerator.StairTypeId) return r;
            return null;
        }

        static void Add(List<DungeonIssue> list, IssueSeverity sev, int li, string msg)
            => list.Add(new DungeonIssue { Severity = sev, LevelIndex = li, Message = msg });

        // An inter-floor portal's target resolves iff its floor index is in range and the room still exists there.
        static bool TargetExists(InteriorData dungeon, Portal s)
            => s.TargetFloorIndex >= 0 && s.TargetFloorIndex < dungeon.Floors.Count
               && dungeon.Floors[s.TargetFloorIndex].GetRoom(s.TargetRoomId) != null;

        static Dictionary<int, HashSet<int>> BuildAdj(InteriorFloor lvl)
        {
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var r in lvl.Rooms) adj[r.Id] = new HashSet<int>();
            foreach (var c in lvl.Links)
                if (adj.ContainsKey(c.RoomA) && adj.ContainsKey(c.RoomB)) { adj[c.RoomA].Add(c.RoomB); adj[c.RoomB].Add(c.RoomA); }
            return adj;
        }

        static HashSet<int> Reachable(int startId, Dictionary<int, HashSet<int>> adj)
        {
            var seen = new HashSet<int>();
            if (!adj.ContainsKey(startId)) return seen;
            seen.Add(startId);
            var q = new Queue<int>(); q.Enqueue(startId);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (seen.Add(nb)) q.Enqueue(nb); }
            return seen;
        }

        static int Distance(int a, int b, Dictionary<int, HashSet<int>> adj)
        {
            if (!adj.ContainsKey(a)) return -1;
            var dist = new Dictionary<int, int> { [a] = 0 };
            var q = new Queue<int>(); q.Enqueue(a);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int nb in adj[c]) if (!dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; q.Enqueue(nb); } }
            return dist.TryGetValue(b, out var d) ? d : -1;
        }
    }
}
