using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-test for BuildingGenerator -- add to any GameObject, run from the
    /// Inspector. Every assertion targets the geometry/rule a specific generation step produces, so deleting
    /// that step flips the assertion to FAIL (non-vacuous -- the project's #1 past failure mode was tests
    /// that pass whether or not the rule holds; see CompactLayoutSelfTests for the same discipline).</summary>
    public class BuildingGeneratorSelfTests : MonoBehaviour
    {
        // Tile-space tolerance for the containment comparison: generous vs the ToNorm/ToTile round-trip
        // (~1e-3 tiles) yet far below the multi-tile overshoot an UN-nested floor would produce.
        const float Eps = 0.5f;

        [ContextMenu("Self-Test: Building Generator")]
        public void SelfTestBuilding()
        {
            bool ok = true;
            var b = BuildingGenerator.Generate(seed: 42, ownerPoiId: "p", roomCount: 6, floorCount: 3);

            // ---- 1. Shape ------------------------------------------------------------------------------
            if (!(b.Kind == InteriorKind.Building && b.OwnerPoiId == "p"))
            { Debug.LogError("FAIL shape: Kind/OwnerPoiId not set from Generate's own arguments"); ok = false; }
            if (b.Floors.Count != 3)
            { Debug.LogError($"FAIL shape: expected 3 floors, got {b.Floors.Count}"); ok = false; }

            // ---- 2. Floor 0: exactly one entrance (TypeId 0) AND exactly one stairwell column (TypeId 2) --
            // Remove the entrance rule → e0 drifts; remove the column designation → s0 == 0.
            int e0 = 0, s0 = 0;
            foreach (var r in b.Floors[0].Rooms) { if (r.TypeId == 0) e0++; if (r.TypeId == 2) s0++; }
            if (e0 != 1)
            { Debug.LogError($"FAIL entrance: floor 0 has {e0} TypeId==0 rooms, want exactly 1"); ok = false; }
            if (s0 != 1)
            { Debug.LogError($"FAIL column: floor 0 has {s0} TypeId==2 (Лестница) rooms, want exactly 1 (the column)"); ok = false; }

            // ---- 3. Each UPPER floor: exactly one Лестница (TypeId 2), NO entrance, and the Лестница IS the
            //         stair-ARRIVAL room the floor below targets ------------------------------------------------
            // Remove the per-floor column → stairs count drops to 0; mis-target the stair → the id mismatch fires;
            // leak an entrance onto an upper floor → ent != 0.
            for (int f = 1; f < b.Floors.Count; f++)
            {
                int ent = 0, stairs = 0; Room stairRoom = null;
                foreach (var r in b.Floors[f].Rooms) { if (r.TypeId == 0) ent++; if (r.TypeId == 2) { stairs++; stairRoom = r; } }
                if (ent != 0)
                { Debug.LogError($"FAIL upper: floor {f} has {ent} entrance rooms, want 0 (upper floors have no Вход)"); ok = false; }
                if (stairs != 1)
                { Debug.LogError($"FAIL upper: floor {f} has {stairs} Лестница rooms, want exactly 1"); ok = false; }
                int arrival = StairTarget(b, f - 1);   // the room the f-1 -> f stair targets
                if (stairRoom == null || stairRoom.Id != arrival)
                { Debug.LogError($"FAIL upper: floor {f} Лестница is room {(stairRoom == null ? -1 : stairRoom.Id)}, but the stair from below targets {arrival}"); ok = false; }
            }

            // ---- 4. Exactly one non-hidden Stairs portal per consecutive floor pair, stored on the LOWER
            //         floor, pointing up, target room exists on the floor above --------------------------
            // Remove the stair-linking step entirely -> stairs == 0 -> fails. Point TargetFloorIndex at the
            // wrong floor, or leave Hidden true, -> also fails (both are matched in the condition below).
            for (int f = 0; f < b.Floors.Count - 1; f++)
            {
                int stairs = 0; int target = -1;
                foreach (var r in b.Floors[f].Rooms)
                    foreach (var p in r.Portals)
                        if (p.Kind == PortalKind.Stairs && !p.Hidden && p.TargetFloorIndex == f + 1)
                        { stairs++; target = p.TargetRoomId; }

                if (stairs != 1)
                { Debug.LogError($"FAIL stairs: floor {f}->{f + 1} has {stairs} non-hidden Stairs portals, want exactly 1"); ok = false; }
                else if (b.Floors[f + 1].GetRoom(target) == null)
                { Debug.LogError($"FAIL stairs: floor {f}->{f + 1} stair targets room {target}, which does not exist on floor {f + 1}"); ok = false; }
            }

            // ---- 5. Rooms sized in the generator's OWN modest range 4..6 (on EVERY floor) --------------
            // Bound = the generator's exact roll range (rng.Next(4,7)). Sizes must NOT shrink on upper floors
            // (coherence shrinks by FEWER rooms, not smaller ones), so 4..6 holds on every floor. Tighter than
            // "1..8" ON PURPOSE: routing sizes through RoomSizing.Roll (dungeon ranges 4..8 / 5..8, Boss 8..14)
            // would roll a 7 or 8.
            foreach (var fl in b.Floors)
                foreach (var r in fl.Rooms)
                    if (!(r.SizeW >= 4 && r.SizeW <= 6 && r.SizeH >= 4 && r.SizeH <= 6))
                    {
                        Debug.LogError($"FAIL size: room {r.Id} is {r.SizeW}x{r.SizeH}, want 4..6 (building-modest, not RoomSizing/Boss-sized)");
                        ok = false;
                    }

            // ---- 7. Coherence: every floor's footprint bbox is within FLOOR 0's outline (+ the contour margin)
            // Rooms are packed inside floor 0's footprint SHAPE, which extends ContourMargin beyond floor 0's
            // rooms, so an upper floor may reach up to that margin past floor 0's bbox — the tolerance allows it.
            // WEAK guard on its own (floor 0's entrance root is field-centred); test 8 is the STRONG, non-vacuous
            // shape-fit guard.
            var bc = BuildingGenerator.Generate(seed: 7, ownerPoiId: "p", roomCount: 8, floorCount: 3);
            var f0box = Bbox(bc.Floors[0]);
            float coTol = FloorFootprint.ContourMargin + Eps;
            for (int f = 1; f < bc.Floors.Count; f++)
            {
                var hi = Bbox(bc.Floors[f]);
                bool contained = hi.minX >= f0box.minX - coTol && hi.minY >= f0box.minY - coTol
                              && hi.maxX <= f0box.maxX + coTol && hi.maxY <= f0box.maxY + coTol;
                if (!contained)
                { Debug.LogError($"FAIL coherence: floor {f} bbox [{hi.minX:F1},{hi.minY:F1}..{hi.maxX:F1},{hi.maxY:F1}] not within floor 0 [{f0box.minX:F1},{f0box.minY:F1}..{f0box.maxX:F1},{f0box.maxY:F1}]"); ok = false; }
            }

            // ---- 7c. COLUMN: the Лестница sits at the SAME (x,y) on EVERY floor (one vertical shaft) -------
            // The defining invariant. PackAroundColumnWithinFootprint PINS the column exactly at floor 0's
            // column position, so every floor's Лестница shares it. NON-VACUOUS: drop the pin and an upper
            // Лестница keeps its graph-build default (~0,0), far off floor 0's off-centre column. (seed 7.)
            {
                Room col0 = null;
                foreach (var r in bc.Floors[0].Rooms) if (r.TypeId == 2) col0 = r;
                if (col0 == null)
                { Debug.LogError("FAIL column: floor 0 has no Лестница to define the column"); ok = false; }
                else
                    for (int f = 1; f < bc.Floors.Count; f++)
                    {
                        Room colF = null;
                        foreach (var r in bc.Floors[f].Rooms) if (r.TypeId == 2) colF = r;
                        if (colF == null) continue;   // missing column is caught by test 3
                        float dx = Mathf.Abs(colF.X - col0.X) * DungeonLayout.TilesPerAxis;
                        float dy = Mathf.Abs(colF.Y - col0.Y) * DungeonLayout.TilesPerAxis;
                        if (dx > 1f || dy > 1f)
                        { Debug.LogError($"FAIL column: floor {f} Лестница off the column by ({dx:F1},{dy:F1}) tiles"); ok = false; }
                    }
            }

            // ---- 8. Shape-aware packing: rooms APPEAR inside the contour SHAPE (the "only-Лестница" bug fix) --
            // contourFloor = an L (rooms at (14,14),(26,14),(14,26)) — non-convex, with an empty bottom-right
            // corner NOT in the footprint. Packing 6 rooms around a central column must: (a) place MORE than
            // just the column (rooms actually appear — not reduce-to-1), (b) keep EVERY placed room inside the
            // L-shape (the SAME ContainsRect the renderer's flag uses → flag-free), (c) pin the Лестница exactly
            // on the column. Non-vacuous: reduce-to-1 fails (a); a bbox-only fit fails (b).
            {
                int T2 = DungeonLayout.TilesPerAxis;
                var contour = new InteriorFloor();
                contour.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 12, SizeH = 12, X = 14f / T2, Y = 14f / T2 });
                contour.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 12, SizeH = 12, X = 26f / T2, Y = 14f / T2 });
                contour.Rooms.Add(new Room { Id = 3, TypeId = 1, SizeW = 12, SizeH = 12, X = 14f / T2, Y = 26f / T2 });
                var upper = BuildingGenerator.GenerateFloorAroundColumn(
                    new System.Random(20260718), roomBudget: 6, 14f, 14f, 4, 4, contour, out var upStair);

                if (upper.Rooms.Count < 2)
                { Debug.LogError($"FAIL packing: only {upper.Rooms.Count} room(s) placed — rooms must pack around the column, not collapse to just the Лестница"); ok = false; }
                foreach (var r in upper.Rooms)
                {
                    var (w, h) = DungeonProjection.EffectiveSize(r);
                    if (!FloorFootprint.ContainsRect(contour, FloorFootprint.ContourMargin, r.X * T2, r.Y * T2, w, h))
                    { Debug.LogError($"FAIL packing: room {r.Id} is outside the L-shaped contour"); ok = false; }
                }
                if (Mathf.Abs(upStair.X * T2 - 14f) > 0.5f || Mathf.Abs(upStair.Y * T2 - 14f) > 0.5f)
                { Debug.LogError("FAIL packing: Лестница not pinned exactly on the column"); ok = false; }
            }

            // ---- 6. Determinism: same seed -> identical structure AND identical positions/bboxes ---------
            // Per-floor room/link counts, each room's TypeId, size AND X/Y, each floor pair's stair target,
            // and every floor's bbox must match between two independent runs of the same seed. Adding the X/Y
            // and bbox comparison locks the new coherence placement (a non-deterministic nest would diverge).
            var b2 = BuildingGenerator.Generate(42, "p", 6, 3);
            if (b.Floors.Count != b2.Floors.Count)
            {
                Debug.LogError("FAIL determinism: floor count differs between two runs of the same seed");
                ok = false;
            }
            else
            {
                for (int f = 0; f < b.Floors.Count; f++)
                {
                    var fa = b.Floors[f]; var fb = b2.Floors[f];
                    if (fa.Rooms.Count != fb.Rooms.Count || fa.Links.Count != fb.Links.Count)
                    {
                        Debug.LogError($"FAIL determinism: floor {f} room/link count differs between two runs");
                        ok = false;
                        continue;
                    }
                    for (int i = 0; i < fa.Rooms.Count; i++)
                    {
                        if (fa.Rooms[i].TypeId != fb.Rooms[i].TypeId
                            || fa.Rooms[i].SizeW != fb.Rooms[i].SizeW
                            || fa.Rooms[i].SizeH != fb.Rooms[i].SizeH
                            || fa.Rooms[i].X != fb.Rooms[i].X
                            || fa.Rooms[i].Y != fb.Rooms[i].Y)
                        {
                            Debug.LogError($"FAIL determinism: floor {f} room index {i} TypeId/size/position differs between two runs");
                            ok = false;
                        }
                    }
                    var ba = Bbox(fa); var bb = Bbox(fb);
                    if (ba.minX != bb.minX || ba.minY != bb.minY || ba.maxX != bb.maxX || ba.maxY != bb.maxY)
                    {
                        Debug.LogError($"FAIL determinism: floor {f} bbox differs between two runs");
                        ok = false;
                    }
                }
                for (int f = 0; f < b.Floors.Count - 1; f++)
                {
                    int ta = StairTarget(b, f), tb = StairTarget(b2, f);
                    if (ta != tb)
                    {
                        Debug.LogError($"FAIL determinism: floor {f}->{f + 1} stair target differs between two runs ({ta} vs {tb})");
                        ok = false;
                    }
                }
            }

            // ---- 9. A generated building VALIDATES CLEANLY: no boss warnings, every floor has its entrance -
            // Buildings have no boss, so DungeonValidator must be Kind-gated. Fails with a per-floor "нет
            // комнаты босса" warning if the boss check isn't gated, or a "ровно один вход" error if any floor
            // (esp. an upper one, whose entrance is the stair-arrival room) lacks its entrance.
            bool anyBossIssue = false, anyEntranceError = false;
            foreach (var iss in DungeonValidator.Validate(b))
            {
                if (iss.Message.Contains("босс")) anyBossIssue = true;
                if (iss.Severity == IssueSeverity.Error && iss.Message.Contains("вход")) anyEntranceError = true;
            }
            if (anyBossIssue)
            { Debug.LogError("FAIL validate: building produced a boss-room issue (DungeonValidator not Kind-gated)"); ok = false; }
            if (anyEntranceError)
            { Debug.LogError("FAIL validate: a building floor is missing its entrance"); ok = false; }

            // ---- 10. NormalizeTypes collapses only the DROPPED legacy types (3/4) to the plain room --------
            // Valid ids 0/1/2 stay (2 is now the Лестница!); only the removed Служебная/Особая (3/4) → 1. Fails
            // if the remap is dropped, or if it still collapses 2 (which would wipe every generated stairwell).
            var legacy = new InteriorData { Kind = InteriorKind.Building };
            var lf = new InteriorFloor();
            lf.Rooms.Add(new Room { Id = 1, TypeId = 0 });   // entrance stays
            lf.Rooms.Add(new Room { Id = 2, TypeId = 1 });   // plain stays
            lf.Rooms.Add(new Room { Id = 3, TypeId = 2 });   // Лестница — must STAY 2
            lf.Rooms.Add(new Room { Id = 4, TypeId = 3 });   // legacy Служебная -> 1
            lf.Rooms.Add(new Room { Id = 5, TypeId = 4 });   // legacy Особая -> 1
            legacy.Floors.Add(lf);
            BuildingGenerator.NormalizeTypes(legacy);
            var lr = legacy.Floors[0];
            if (lr.GetRoom(1).TypeId != 0 || lr.GetRoom(2).TypeId != 1 || lr.GetRoom(3).TypeId != 2
                || lr.GetRoom(4).TypeId != 1 || lr.GetRoom(5).TypeId != 1)
            { Debug.LogError("FAIL normalize: want {0→0, 1→1, 2→2 (Лестница kept), 3→1, 4→1}"); ok = false; }

            // ---- 11. EnsureFloorZeroColumn: returns the existing column, or designates a central non-entrance
            //          room (the editor's +этаж relies on this to anchor a new floor on the column) -----------
            // Case A: the generated building `b` already has a floor-0 column → it is returned, still the single
            // Лестница. Case B: a building whose floor 0 has NO Лестница → a non-entrance room is designated.
            var existing = BuildingGenerator.EnsureFloorZeroColumn(b);
            int cols = 0; foreach (var r in b.Floors[0].Rooms) if (r.TypeId == 2) cols++;
            if (existing == null || existing.TypeId != 2 || cols != 1)
            { Debug.LogError("FAIL column-ensure: existing floor-0 column not returned as the single Лестница"); ok = false; }

            var noCol = new InteriorData { Kind = InteriorKind.Building };
            var g = new InteriorFloor();
            g.Rooms.Add(new Room { Id = 1, TypeId = 0, SizeW = 4, SizeH = 4, X = 0.50f, Y = 0.5f });   // entrance AT the bbox centre
            g.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 4, SizeH = 4, X = 0.40f, Y = 0.5f });   // plain, off to the left
            g.Rooms.Add(new Room { Id = 3, TypeId = 1, SizeW = 4, SizeH = 4, X = 0.60f, Y = 0.5f });   // plain, off to the right
            noCol.Floors.Add(g);
            var made = BuildingGenerator.EnsureFloorZeroColumn(noCol);
            if (made == null || made.TypeId != 2)
            { Debug.LogError("FAIL column-ensure: no column designated on a building lacking one"); ok = false; }
            if (made != null && made.Id == 1)
            { Debug.LogError("FAIL column-ensure: the entrance was designated as the column"); ok = false; }

            // ---- 12. Packable capacity + GUARANTEED exact-count generation (the «Перегенерировать» fix) --------
            // Area only bounds the count; the displayed «из N» is the PROBED packable max, and every count ≤ N must
            // generate EXACTLY that many rooms (the user's requirement: an in-range count always finds a layout).
            // Non-vacuous: a broken area sum, a non-deterministic/over-promising cap, or a trim that keeps the wrong
            // count each flips an assertion.
            {
                int T3 = DungeonLayout.TilesPerAxis;
                float m = FloorFootprint.ContourMargin;

                // (a) EXACT union area of a single 6×6 room = (6+2m)² (margin is added on BOTH sides).
                var one = new InteriorFloor();
                one.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 6, SizeH = 6, X = 0.5f, Y = 0.5f });
                float wantArea = (6f + 2f * m) * (6f + 2f * m);
                float gotArea = FloorFootprint.UsableAreaTiles(one, m);
                if (Mathf.Abs(gotArea - wantArea) > 0.01f)
                { Debug.LogError($"FAIL area: single 6×6 room usable area {gotArea:F2}, want {wantArea:F2}"); ok = false; }

                var big = new InteriorFloor();
                big.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 16, SizeH = 16, X = 0.5f, Y = 0.5f });
                float cx = 0.5f * T3, cy = 0.5f * T3;

                // (b) The probed max is DETERMINISTIC (stable «из N»), ACHIEVABLE (≥1), and ≤ the area upper bound.
                int capBig = BuildingGenerator.MaxRoomsPackable(cx, cy, 4, 4, big);
                if (capBig != BuildingGenerator.MaxRoomsPackable(cx, cy, 4, 4, big))
                { Debug.LogError("FAIL packable: MaxRoomsPackable not deterministic"); ok = false; }
                if (capBig < 2 || capBig > BuildingGenerator.MaxRoomsByArea(big, 4, 4))
                { Debug.LogError($"FAIL packable: capBig {capBig} out of [2, areaBound]"); ok = false; }

                // (c) GUARANTEE: EVERY count 1..capBig builds EXACTLY that many rooms, column present + pinned, all
                //     rooms inside the contour. This is the property the user asked for — an in-range count always
                //     generates. A packer that can't reach a count, or a trim that keeps the wrong number, fails here.
                for (int c = 1; c <= capBig; c++)
                {
                    bool built = BuildingGenerator.TryBuildUpperFloorExact(c, 777 + c, 8, cx, cy, 4, 4, big, out var fl, out var st);
                    if (!built) { Debug.LogError($"FAIL guarantee: TryBuildUpperFloorExact failed for in-range count {c}/{capBig}"); ok = false; continue; }
                    if (fl.Rooms.Count != c) { Debug.LogError($"FAIL guarantee: asked {c} rooms, got {fl.Rooms.Count}"); ok = false; }
                    if (st == null || st.TypeId != 2 || fl.GetRoom(st.Id) == null)
                    { Debug.LogError($"FAIL guarantee: column missing from the {c}-room floor"); ok = false; }
                    if (st != null && (Mathf.Abs(st.X * T3 - cx) > 0.5f || Mathf.Abs(st.Y * T3 - cy) > 0.5f))
                    { Debug.LogError($"FAIL guarantee: column not pinned on the {c}-room floor"); ok = false; }
                }

                // (d) TrimToRoomCount keeps EXACTLY the target and the column. Build a max floor, trim to 2.
                if (capBig >= 3 && BuildingGenerator.TryBuildUpperFloorExact(capBig, 42, 8, cx, cy, 4, 4, big, out var full, out var fullSt))
                {
                    BuildingGenerator.TrimToRoomCount(full, 2, fullSt.Id);
                    if (full.Rooms.Count != 2 || full.GetRoom(fullSt.Id) == null)
                    { Debug.LogError($"FAIL trim: expected 2 rooms incl. column, got {full.Rooms.Count}"); ok = false; }
                }

                // (e) The build does NOT over-promise: a count above the AREA bound (min-footprint impossible) can
                //     never be built, for any seed. Probing areaBound+1 (not capBig+N) keeps this reliable regardless
                //     of how the probe/params shift capBig.
                int areaBound = BuildingGenerator.MaxRoomsByArea(big, 4, 4);
                if (BuildingGenerator.TryBuildUpperFloorExact(areaBound + 1, 5, 8, cx, cy, 4, 4, big, out _, out _))
                { Debug.LogError($"FAIL cap: built {areaBound + 1} rooms though area allows at most {areaBound}"); ok = false; }
            }

            // ---- 13. Validator BUILDING rules: one Лестница per floor (multi-floor) + no stairs to nowhere -----
            // Non-vacuous: a fresh building must NOT report a stair issue; removing an upper Лестница or dangling a
            // Stairs portal each must raise the matching error.
            {
                var fresh = BuildingGenerator.Generate(seed: 3, ownerPoiId: "p", roomCount: 6, floorCount: 3);
                if (HasMsg(DungeonValidator.Validate(fresh), "лестниц"))
                { Debug.LogError("FAIL validate: a fresh building reports a stair issue"); ok = false; }

                // (a) Upper floor missing its Лестница → "одна лестница". Retype floor 1's column to a plain room.
                var missing = BuildingGenerator.Generate(3, "p", 6, 3);
                foreach (var r in missing.Floors[1].Rooms) if (r.TypeId == 2) { r.TypeId = 1; break; }
                if (!HasMsg(DungeonValidator.Validate(missing), "одна лестница"))
                { Debug.LogError("FAIL validate: missing upper-floor Лестница not flagged"); ok = false; }

                // (b) Stairs to nowhere → dangling-stair error. Point a floor-0 Stairs portal at a missing room.
                var dangling = BuildingGenerator.Generate(3, "p", 6, 3);
                bool pointed = false;
                foreach (var r in dangling.Floors[0].Rooms)
                {
                    foreach (var s in r.Portals) if (s.Kind == PortalKind.Stairs) { s.TargetRoomId = 99999; pointed = true; break; }
                    if (pointed) break;
                }
                if (pointed && !HasMsg(DungeonValidator.Validate(dangling), "несуществующий этаж"))
                { Debug.LogError("FAIL validate: dangling Stairs portal not flagged"); ok = false; }

                // (c) Broken shaft is FLAGGED, and auto-realign re-syncs the WHOLE upper floor to the floor-0 column.
                var desync = BuildingGenerator.Generate(3, "p", 6, 3);
                Room upCol = desync.Floors[1].Rooms.Find(r => r.TypeId == 2);
                Room sibling = desync.Floors[1].Rooms.Find(r => r.TypeId != 2);
                float sibBefore = sibling != null ? sibling.X : 0f;
                upCol.X += 0.1f;   // shove the upper Лестница off the column
                if (!HasMsg(DungeonValidator.Validate(desync), "не совпадает со столбом"))
                { Debug.LogError("FAIL validate: desynced stairwell column not flagged"); ok = false; }

                BuildingGenerator.RealignUpperFloorsToColumn(desync);
                Room col0d = desync.Floors[0].Rooms.Find(r => r.TypeId == 2);
                if (Mathf.Abs(upCol.X - col0d.X) > 1e-4f || Mathf.Abs(upCol.Y - col0d.Y) > 1e-4f)
                { Debug.LogError("FAIL realign: upper Лестница not re-aligned to the floor-0 column"); ok = false; }
                if (sibling != null && Mathf.Abs((sibling.X - sibBefore) - (-0.1f)) > 1e-3f)
                { Debug.LogError("FAIL realign: the upper floor did not translate as a whole with its column"); ok = false; }
                if (HasMsg(DungeonValidator.Validate(desync), "не совпадает со столбом"))
                { Debug.LogError("FAIL realign: shaft still flagged after auto-realign"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Building Generator: PASS" : "Self-Test Building Generator: FAIL");
        }

        /// <summary>[F3] «× Этаж» must work for any Building floor except floor 0 — RewireStairChain must
        /// repair the vertical Stairs chain after ANY removal (not just the previously-supported top-floor
        /// case), floor 0 must stay protected, repeated calls must be idempotent, and SecretDoor portals must
        /// survive untouched. See DungeonEditorScreen.RemoveCurrentLevel/FloorToRemove for the UI-side half.</summary>
        [ContextMenu("Self-Test: Building Floor Removal (F3)")]
        public void SelfTestFloorRemoval()
        {
            bool ok = true;

            // ---- 1. THE FIX: removing a MIDDLE floor must not sever the shaft ---------------------------
            // Build 4 floors (0..3), remove floor 1 (a middle floor — old floors 2,3 shift down to 1,2).
            // FIRST assert the bug is real: DungeonOps.RemoveLevel alone drops floor 0's up-portal, because
            // that portal's Kind==Stairs && TargetFloorIndex==1 (the removed index) matches RemoveLevel's own
            // "drop any inter-floor portal that targeted the removed level" rule — the exact mechanism the
            // task brief describes. WITHOUT RewireStairChain, floor 0 would stay stranded (0 Stairs portals)
            // and the floors above unreachable from it. THEN call RewireStairChain and assert every
            // consecutive floor pair carries exactly one correctly-targeted Stairs portal, the new top floor
            // has none, and DungeonValidator reports no errors.
            {
                var mid = BuildingGenerator.Generate(seed: 99, ownerPoiId: "p", roomCount: 6, floorCount: 4);
                Room col0 = null; foreach (var r in mid.Floors[0].Rooms) if (r.TypeId == 2) col0 = r;

                DungeonOps.RemoveLevel(mid, 1);   // remove the middle floor

                if (mid.Floors.Count != 3)
                { Debug.LogError($"FAIL midremove: expected 3 floors after removal, got {mid.Floors.Count}"); ok = false; }

                int brokenStairs = 0; if (col0 != null) foreach (var p in col0.Portals) if (p.Kind == PortalKind.Stairs) brokenStairs++;
                if (brokenStairs != 0)
                {
                    Debug.LogError($"FAIL midremove: test premise broken — expected RemoveLevel ALONE to strand " +
                        $"floor 0 (0 Stairs portals), found {brokenStairs}. If this fires, re-derive the test: the " +
                        $"bug this task fixes may no longer reproduce this way.");
                    ok = false;
                }

                BuildingGenerator.RewireStairChain(mid);

                for (int f = 0; f < mid.Floors.Count - 1; f++)
                {
                    Room stairF = null; foreach (var r in mid.Floors[f].Rooms) if (r.TypeId == 2) stairF = r;
                    Room stairNext = null; foreach (var r in mid.Floors[f + 1].Rooms) if (r.TypeId == 2) stairNext = r;
                    if (stairF == null || stairNext == null)
                    { Debug.LogError($"FAIL midremove: floor {f}/{f + 1} missing its Лестница"); ok = false; continue; }
                    int n = 0, target = -1;
                    foreach (var p in stairF.Portals) if (p.Kind == PortalKind.Stairs) { n++; target = p.TargetRoomId; }
                    if (n != 1 || target != stairNext.Id)
                    { Debug.LogError($"FAIL midremove: floor {f} has {n} Stairs portal(s) (want 1) targeting {target} (want {stairNext.Id})"); ok = false; }
                }
                Room topStair = null; foreach (var r in mid.Floors[mid.Floors.Count - 1].Rooms) if (r.TypeId == 2) topStair = r;
                if (topStair != null)
                {
                    int nTop = 0; foreach (var p in topStair.Portals) if (p.Kind == PortalKind.Stairs) nTop++;
                    if (nTop != 0)
                    { Debug.LogError($"FAIL midremove: new top floor's Лестница has {nTop} Stairs portal(s), want 0"); ok = false; }
                }

                foreach (var iss in DungeonValidator.Validate(mid))
                    if (iss.Severity == IssueSeverity.Error)
                    { Debug.LogError($"FAIL midremove: validator reports an error after re-wire — {iss.Message}"); ok = false; }
            }

            // ---- 2. Floor 0 is protected from removal (task requirement 2) --------------------------------
            // The refusal lives in DungeonEditorScreen.FloorToRemove (UI-level: returns -1, which
            // RemoveCurrentLevel/RequestRemoveCurrentLevel both check), and it now calls the SAME headless
            // predicate this test asserts — BuildingGenerator.IsFloorZeroLocked — so the UI guard and this test
            // can never drift apart (previously this was a hand copy of FloorToRemove's condition). PLUS the
            // real consequence that predicate exists to prevent: a building missing floor 0 fails validation
            // (no entrance anywhere), which is WHY the removal must be refused in the first place.
            {
                if (!BuildingGenerator.IsFloorZeroLocked(InteriorKind.Building, 0))
                { Debug.LogError("FAIL floor0-guard: a Building must refuse removing floor 0"); ok = false; }
                if (BuildingGenerator.IsFloorZeroLocked(InteriorKind.Building, 1) || BuildingGenerator.IsFloorZeroLocked(InteriorKind.Building, 2))
                { Debug.LogError("FAIL floor0-guard: a Building must NOT refuse removing floor 1+"); ok = false; }
                if (BuildingGenerator.IsFloorZeroLocked(InteriorKind.Dungeon, 0))
                { Debug.LogError("FAIL floor0-guard: a Dungeon has no floor-0 protection"); ok = false; }

                var noFloor0 = BuildingGenerator.Generate(seed: 23, ownerPoiId: "p", roomCount: 6, floorCount: 3);
                DungeonOps.RemoveLevel(noFloor0, 0);
                BuildingGenerator.RewireStairChain(noFloor0);
                bool entranceError = false;
                foreach (var iss in DungeonValidator.Validate(noFloor0))
                    if (iss.Severity == IssueSeverity.Error && iss.LevelIndex == 0 && iss.Message.Contains("вход")) entranceError = true;
                if (!entranceError)
                { Debug.LogError("FAIL floor0-guard: a building missing floor 0 must fail validation (no entrance) — this is WHY the UI refuses it"); ok = false; }
            }

            // ---- 3. Idempotence: a second RewireStairChain call changes nothing -----------------------------
            // Compare each floor's Лестница Stairs-portal COUNT and TARGET (not a hash of the whole object,
            // per the task brief) before and after a second call.
            {
                var idem = BuildingGenerator.Generate(seed: 61, ownerPoiId: "p", roomCount: 6, floorCount: 4);
                DungeonOps.RemoveLevel(idem, 1);
                BuildingGenerator.RewireStairChain(idem);   // first call repairs the chain (as test 1 established)

                (int count, int targetFloor, int targetRoom) Snap(Room st)
                {
                    if (st == null) return (-1, -1, -1);
                    int n = 0, tf = -1, tr = -1;
                    foreach (var p in st.Portals) if (p.Kind == PortalKind.Stairs) { n++; tf = p.TargetFloorIndex; tr = p.TargetRoomId; }
                    return (n, tf, tr);
                }
                var before = new (int count, int targetFloor, int targetRoom)[idem.Floors.Count];
                for (int f = 0; f < idem.Floors.Count; f++)
                {
                    Room st = null; foreach (var r in idem.Floors[f].Rooms) if (r.TypeId == 2) st = r;
                    before[f] = Snap(st);
                }

                BuildingGenerator.RewireStairChain(idem);   // second call — must be a no-op

                for (int f = 0; f < idem.Floors.Count; f++)
                {
                    Room st = null; foreach (var r in idem.Floors[f].Rooms) if (r.TypeId == 2) st = r;
                    var after = Snap(st);
                    if (after.count != before[f].count || after.targetFloor != before[f].targetFloor || after.targetRoom != before[f].targetRoom)
                    {
                        Debug.LogError($"FAIL idempotence: floor {f} Stairs snapshot changed on the second call " +
                            $"({before[f].count}/{before[f].targetFloor}/{before[f].targetRoom} -> {after.count}/{after.targetFloor}/{after.targetRoom})");
                        ok = false;
                    }
                }
            }

            // ---- 4. Removing the TOP floor still works (the previously-supported case) --------------------
            // The new top floor's Лестница must end with NO upward portal, both BEFORE and AFTER
            // RewireStairChain — this case never broke (RemoveLevel's own drop rule already covers it), so a
            // call here must be a true no-op, not merely "compatible".
            {
                var top = BuildingGenerator.Generate(seed: 17, ownerPoiId: "p", roomCount: 6, floorCount: 3);
                DungeonOps.RemoveLevel(top, 2);   // remove the TOP floor
                if (top.Floors.Count != 2)
                { Debug.LogError($"FAIL topremove: expected 2 floors, got {top.Floors.Count}"); ok = false; }

                Room col0 = null; foreach (var r in top.Floors[0].Rooms) if (r.TypeId == 2) col0 = r;
                Room col1 = null; foreach (var r in top.Floors[1].Rooms) if (r.TypeId == 2) col1 = r;

                void AssertChain(string when)
                {
                    if (col0 == null || col1 == null) { Debug.LogError($"FAIL topremove ({when}): missing Лестница"); ok = false; return; }
                    int n0 = 0, target = -1; foreach (var p in col0.Portals) if (p.Kind == PortalKind.Stairs) { n0++; target = p.TargetRoomId; }
                    if (n0 != 1 || target != col1.Id)
                    { Debug.LogError($"FAIL topremove ({when}): floor 0 has {n0} Stairs portal(s) targeting {target}, want 1 targeting {col1.Id}"); ok = false; }
                    int n1 = 0; foreach (var p in col1.Portals) if (p.Kind == PortalKind.Stairs) n1++;
                    if (n1 != 0)
                    { Debug.LogError($"FAIL topremove ({when}): new top floor's Лестница has {n1} Stairs portal(s), want 0"); ok = false; }
                }
                AssertChain("before re-wire");
                BuildingGenerator.RewireStairChain(top);
                AssertChain("after re-wire, must be a no-op");
            }

            // ---- 5. SecretDoor survival across a middle-floor removal + re-wire -----------------------------
            // RemoveLevel's TargetFloorIndex decrement (untouched by this task) must still correctly shift a
            // SecretDoor's target, and RewireStairChain must never touch it (it only rebuilds Kind==Stairs
            // portals on Лестница rooms). Non-vacuous: clobber either and the portal's target is wrong or gone.
            {
                var sd = BuildingGenerator.Generate(seed: 55, ownerPoiId: "p", roomCount: 6, floorCount: 4);
                Room col0 = null; foreach (var r in sd.Floors[0].Rooms) if (r.TypeId == 2) col0 = r;
                col0.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, Hidden = true, TargetFloorIndex = 3, TargetRoomId = 12345, Bidirectional = true, Label = "s" });

                DungeonOps.RemoveLevel(sd, 1);   // remove the middle floor — old floor 3 target shifts 3->2
                BuildingGenerator.RewireStairChain(sd);

                bool found = false;
                foreach (var p in col0.Portals)
                    if (p.Kind == PortalKind.SecretDoor && p.TargetFloorIndex == 2 && p.TargetRoomId == 12345) found = true;
                if (!found)
                { Debug.LogError("FAIL secret-survival: SecretDoor target should shift 3→2 after removing floor 1, and survive the re-wire"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Building Floor Removal (F3): PASS" : "Self-Test Building Floor Removal (F3): FAIL");
        }

        /// <summary>The ORDER of the two floor-0 drag corrections — the one thing a test that calls
        /// RealignUpperFloorsToColumn directly CANNOT see. The anti-overlap nudge may MOVE the room it is
        /// given, and on floor 0 that room can be the stairwell column itself (floor 0 is free-edit, the DM
        /// can drag the Лестница). So a realign that runs BEFORE the nudge syncs the upper floors to the
        /// DROPPED position and the nudge then slides the column out from under them — the shaft invariant is
        /// broken AT REST, with nothing scheduled to re-run the realign.
        ///
        /// Structure: the SAME stimulus (drop the column dead on top of a neighbour) is run twice — once in
        /// the WRONG order as a live negative control, once through
        /// <see cref="BuildingGenerator.SettleDraggedRoom"/>. The control MUST fail the shaft check and the
        /// real path MUST pass it; if a future edit reverses SettleDraggedRoom's two lines, the two swap and
        /// both halves of this test fail.</summary>
        [ContextMenu("Self-Test: Drag-Settle Ordering (shaft)")]
        public void SelfTestDragSettleOrdering()
        {
            bool ok = true;
            const string ShaftMsg = "не совпадает со столбом";

            // ---- 1. The stimulus actually moves the column (guards the whole test from being vacuous) ----
            // Drop floor 0's Лестница exactly onto a neighbour's centre: a full overlap, so the anti-overlap
            // nudge is GUARANTEED to shove it clear. If it did not, both halves below would trivially pass.
            {
                var probe = DropColumnOnNeighbour(seed: 71, out var pCol, out float dropX, out float dropY);
                CompactLayout.NudgeRoomOffOverlaps(probe.Floors[0], pCol.Id);
                if (Mathf.Abs(pCol.X - dropX) < 1e-4f && Mathf.Abs(pCol.Y - dropY) < 1e-4f)
                { Debug.LogError("FAIL drag-order: the fixture's dropped column was NOT moved by the nudge — the ordering test would be vacuous"); ok = false; }
            }

            // ---- 2. NEGATIVE CONTROL: realign BEFORE the nudge leaves the shaft broken ------------------
            // This is the shipped bug, reproduced: DungeonEditorScreen.RevalidateAndRefresh used to realign
            // and only THEN let BeginCascade nudge.
            {
                var bad = DropColumnOnNeighbour(seed: 71, out var badCol, out _, out _);
                BuildingGenerator.RealignUpperFloorsToColumn(bad);            // stale-order step 1
                CompactLayout.NudgeRoomOffOverlaps(bad.Floors[0], badCol.Id); // stale-order step 2 — moves the column
                if (!HasMsg(DungeonValidator.Validate(bad), ShaftMsg))
                { Debug.LogError("FAIL drag-order: the WRONG order (realign, then nudge) did NOT break the shaft — the control is not exercising the bug"); ok = false; }
            }

            // ---- 3. THE RULE: SettleDraggedRoom pins nudge-then-realign, so the shaft holds AT REST -----
            {
                var good = DropColumnOnNeighbour(seed: 71, out var goodCol, out _, out _);
                BuildingGenerator.SettleDraggedRoom(good, good.Floors[0], goodCol.Id);
                if (HasMsg(DungeonValidator.Validate(good), ShaftMsg))
                { Debug.LogError("FAIL drag-order: the shaft is still misaligned after SettleDraggedRoom — the realign is not running after the nudge"); ok = false; }

                // And directly, not only through the validator's message: every upper Лестница must sit on the
                // column's FINAL (post-nudge) position, tighter than the validator's own 1e-3 tolerance.
                for (int k = 1; k < good.Floors.Count; k++)
                {
                    Room colK = good.Floors[k].Rooms.Find(r => r.TypeId == 2);
                    if (colK == null) continue;
                    if (Mathf.Abs(colK.X - goodCol.X) > 1e-4f || Mathf.Abs(colK.Y - goodCol.Y) > 1e-4f)
                    { Debug.LogError($"FAIL drag-order: floor {k}'s Лестница at ({colK.X:F4},{colK.Y:F4}) does not sit on the settled column ({goodCol.X:F4},{goodCol.Y:F4})"); ok = false; }
                }
            }

            // ---- 4. A drag of an ORDINARY floor-0 room must not disturb the shaft either ----------------
            // The column never moves here, so the realign is a no-op — this pins that SettleDraggedRoom did
            // not become "always translate the upper floors" (which would pass 3 for the wrong reason).
            {
                var plain = BuildingGenerator.Generate(seed: 71, ownerPoiId: "p", roomCount: 6, floorCount: 3);
                Room col0 = plain.Floors[0].Rooms.Find(r => r.TypeId == 2);
                Room other = plain.Floors[0].Rooms.Find(r => r.TypeId != 2 && r.Id != col0.Id);
                Room up1 = plain.Floors[1].Rooms.Find(r => r.TypeId != 2);
                float upBeforeX = up1 != null ? up1.X : 0f, upBeforeY = up1 != null ? up1.Y : 0f;
                other.X = col0.X; other.Y = col0.Y;   // drop it on the column
                BuildingGenerator.SettleDraggedRoom(plain, plain.Floors[0], other.Id);
                if (HasMsg(DungeonValidator.Validate(plain), ShaftMsg))
                { Debug.LogError("FAIL drag-order: dragging a NON-column room broke the shaft"); ok = false; }
                if (up1 != null && (Mathf.Abs(up1.X - upBeforeX) > 1e-6f || Mathf.Abs(up1.Y - upBeforeY) > 1e-6f))
                { Debug.LogError("FAIL drag-order: dragging a NON-column room moved an upper floor"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test Drag-Settle Ordering: PASS" : "Self-Test Drag-Settle Ordering: FAIL");
        }

        /// <summary>[F5] Link.Authored distinguishes a DM's hand-drawn «Связать» corridor from every
        /// generator-made one, so DungeonOps.HasAuthoredContent — the predicate DungeonEditorScreen's «×
        /// Этаж» / «Перегенерировать» confirm gates on — fires ONLY when the DM actually authored something,
        /// not on every floor a fresh spanning tree happens to populate. Non-vacuous: asserts the
        /// PREDICATE's result (not merely the flag) in both directions, so a regression that flips the
        /// predicate back to "any Link" (the pre-fix bug) OR one that forgets to set Authored on the
        /// «Связать» path is caught either way. Also pins that TrimToRoomCount — a filter that RemoveAlls
        /// from the SAME list rather than rebuilding fresh Link objects — cannot silently lose the flag off
        /// a surviving link.</summary>
        [ContextMenu("Self-Test: Authored Link Flag (F5)")]
        public void SelfTestAuthoredLinkFlag()
        {
            bool ok = true;

            // ---- 1. A freshly generated building floor has links (BuildLinks' spanning tree + loops), but
            //         NONE authored, and the predicate agrees: a bare regen must NOT prompt. -----------------
            var gen = BuildingGenerator.Generate(seed: 42, ownerPoiId: "p", roomCount: 6, floorCount: 2);
            var upper = gen.Floors[1];
            if (upper.Links.Count == 0)
            { Debug.LogError("FAIL authored: fixture premise broken — the generated upper floor has no links to test against"); ok = false; }
            foreach (var l in upper.Links)
                if (l.Authored)
                { Debug.LogError($"FAIL authored: generator-made link {l.RoomA}-{l.RoomB} came out Authored==true"); ok = false; }
            if (DungeonOps.HasAuthoredContent(upper))
            { Debug.LogError("FAIL authored: HasAuthoredContent fired on a freshly generated floor with zero authored links"); ok = false; }

            // ---- 2. The «Связать» path — DungeonOps.AddCorridor(authored: true), what DungeonViewController.
            //         OnRoomActivated calls — marks its link Authored, and the SAME predicate now fires. ------
            if (upper.Rooms.Count < 2)
            { Debug.LogError("FAIL authored: fixture premise broken — the generated upper floor has under 2 rooms"); ok = false; }
            else
            {
                var r1 = upper.Rooms[0]; var r2 = upper.Rooms[1];
                DungeonOps.RemoveCorridor(upper, r1.Id, r2.Id);   // clear any pre-existing edge so AddCorridor cannot reject as a duplicate
                string reason = DungeonOps.AddCorridor(upper, r1.Id, r2.Id, authored: true);
                if (reason != null)
                { Debug.LogError($"FAIL authored: AddCorridor(authored:true) rejected: {reason}"); ok = false; }
                var made = upper.Links.Find(l => (l.RoomA == r1.Id && l.RoomB == r2.Id) || (l.RoomA == r2.Id && l.RoomB == r1.Id));
                if (made == null || !made.Authored)
                { Debug.LogError("FAIL authored: the «Связать»-path link was not created with Authored==true"); ok = false; }
                if (!DungeonOps.HasAuthoredContent(upper))
                { Debug.LogError("FAIL authored: HasAuthoredContent did not fire once an authored link exists"); ok = false; }
            }

            // ---- 3. TrimToRoomCount preserves Authored on a SURVIVING link — it removes entries from the
            //         SAME list (RemoveAll), never rebuilds fresh Link objects; a rewrite that reconstructed
            //         links from scratch would silently drop the flag here. -----------------------------------
            {
                int T = DungeonLayout.TilesPerAxis;
                var big = new InteriorFloor();
                big.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 16, SizeH = 16, X = 0.5f, Y = 0.5f });
                float cx = 0.5f * T, cy = 0.5f * T;
                int capBig = BuildingGenerator.MaxRoomsPackable(cx, cy, 4, 4, big);
                if (capBig >= 3 && BuildingGenerator.TryBuildUpperFloorExact(capBig, 909, 8, cx, cy, 4, 4, big, out var full, out var st))
                {
                    var firstLink = full.Links.Find(l => l.RoomA == st.Id || l.RoomB == st.Id);
                    if (firstLink == null)
                    { Debug.LogError("FAIL authored-trim: fixture premise broken — the column has no link to mark"); ok = false; }
                    else
                    {
                        firstLink.Authored = true;   // mark BEFORE trimming — the flag must not affect which link survives
                        int otherId = firstLink.RoomA == st.Id ? firstLink.RoomB : firstLink.RoomA;
                        BuildingGenerator.TrimToRoomCount(full, 2, st.Id);
                        var survivor = full.Links.Find(l => (l.RoomA == st.Id && l.RoomB == otherId) || (l.RoomA == otherId && l.RoomB == st.Id));
                        if (survivor == null || !survivor.Authored)
                        { Debug.LogError("FAIL authored-trim: TrimToRoomCount dropped the Authored flag off a surviving link"); ok = false; }
                    }
                }
            }

            Debug.Log(ok ? "Self-Test Authored Link Flag (F5): PASS" : "Self-Test Authored Link Flag (F5): FAIL");
        }

        // A 3-floor building whose floor-0 Лестница has been "dropped" dead on top of a neighbouring room —
        // the stimulus for the drag-settle ordering test. Reports the column and the position it was dropped
        // at, so the caller can assert the nudge really moved it.
        static InteriorData DropColumnOnNeighbour(int seed, out Room column, out float dropX, out float dropY)
        {
            var d = BuildingGenerator.Generate(seed, ownerPoiId: "p", roomCount: 6, floorCount: 3);
            Room col = d.Floors[0].Rooms.Find(r => r.TypeId == 2);
            Room neighbour = d.Floors[0].Rooms.Find(r => r.TypeId != 2 && r.Id != col.Id);
            col.X = neighbour.X; col.Y = neighbour.Y;
            column = col;
            dropX = col.X; dropY = col.Y;
            return d;
        }

        static bool HasMsg(List<DungeonIssue> issues, string substr)
        {
            foreach (var i in issues) if (i.Message.Contains(substr)) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------
        // Independent helpers (read DungeonLayout.TilesPerAxis + DungeonProjection.EffectiveSize -- never
        // hardcode 128, and reimplement the bbox rather than calling ContentBoundsTiles, so this is an
        // independent check of the generator's placement).
        // ------------------------------------------------------------------------------------------------

        // Footprint bbox of a floor in TILE space (over every room's footprint, not just centres). Empty
        // floors never occur here; a defensive centred box keeps the helper total.
        static (float minX, float minY, float maxX, float maxY) Bbox(InteriorFloor floor)
        {
            int T = DungeonLayout.TilesPerAxis;
            if (floor == null || floor.Rooms.Count == 0)
            {
                float c = T * 0.5f;
                return (c, c, c, c);
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var r in floor.Rooms)
            {
                float cx = r.X * T, cy = r.Y * T;
                var (w, h) = DungeonProjection.EffectiveSize(r);
                if (cx - w * 0.5f < minX) minX = cx - w * 0.5f;
                if (cx + w * 0.5f > maxX) maxX = cx + w * 0.5f;
                if (cy - h * 0.5f < minY) minY = cy - h * 0.5f;
                if (cy + h * 0.5f > maxY) maxY = cy + h * 0.5f;
            }
            return (minX, minY, maxX, maxY);
        }

        // The non-hidden Stairs portal target room id for floor pair f -> f+1, or -1 if none is found.
        static int StairTarget(InteriorData data, int f)
        {
            foreach (var r in data.Floors[f].Rooms)
                foreach (var p in r.Portals)
                    if (p.Kind == PortalKind.Stairs && !p.Hidden && p.TargetFloorIndex == f + 1)
                        return p.TargetRoomId;
            return -1;
        }
    }
}
