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

            // ---- 7. Coherence: every floor's footprint bbox is CONTAINED in FLOOR 0's outline -------------
            // The generation boundary is floor 0 (the drawn contour), so all floors nest in it (floors are
            // decoupled — an upper floor need not be smaller than the one directly below, only within floor 0).
            // NON-VACUOUS: the column is off-centre, so an upper floor generated WITHOUT the within-outline
            // clamp (Arrange centres it on the field) would escape floor 0's off-centre bbox.
            var bc = BuildingGenerator.Generate(seed: 7, ownerPoiId: "p", roomCount: 8, floorCount: 3);
            var f0box = Bbox(bc.Floors[0]);
            for (int f = 1; f < bc.Floors.Count; f++)
            {
                var hi = Bbox(bc.Floors[f]);
                bool contained = hi.minX >= f0box.minX - Eps && hi.minY >= f0box.minY - Eps
                              && hi.maxX <= f0box.maxX + Eps && hi.maxY <= f0box.maxY + Eps;
                if (!contained)
                { Debug.LogError($"FAIL coherence: floor {f} bbox [{hi.minX:F1},{hi.minY:F1}..{hi.maxX:F1},{hi.maxY:F1}] not within floor 0 [{f0box.minX:F1},{f0box.minY:F1}..{f0box.maxX:F1},{f0box.maxY:F1}]"); ok = false; }
            }

            // ---- 7b. At least one pair is STRICTLY smaller on an axis (guards against a vacuous "always
            //          equal" containment, where bbox(upper) == bbox(lower) would satisfy ⊆ trivially) ------
            // Upper floors carry FEWER rooms than the floor below, so their packed footprint is strictly
            // smaller on at least one axis. If the shrink-by-fewer-rooms step were removed and floors matched
            // the floor below, this fails.
            {
                var lo = Bbox(bc.Floors[0]);
                var hi = Bbox(bc.Floors[1]);
                bool strictlySmaller = (hi.maxX - hi.minX) < (lo.maxX - lo.minX) - Eps
                                    || (hi.maxY - hi.minY) < (lo.maxY - lo.minY) - Eps;
                if (!strictlySmaller)
                { Debug.LogError($"FAIL coherence: floor 1 bbox is not strictly smaller than floor 0 on any axis (fewer-rooms shrink missing?)"); ok = false; }
            }

            // ---- 7c. COLUMN: the Лестница sits at the SAME (x,y) on EVERY floor (one vertical shaft) -------
            // The defining invariant of the stairwell-column model. NON-VACUOUS: without the nudge-to-column
            // step an upper Лестница lands at the field centre (Arrange centres its root), far from floor 0's
            // off-centre column, so the offset would exceed a tile. Uses `bc` (off-centre column, seed 7).
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

            // ---- 8. GenerateFloorAroundColumn fits within an OFF-CENTRE outline (non-vacuous nest) ---------
            // Off-centre box 24x24 in the field's top-left, column near its far corner. The generated floor's
            // bbox must stay inside the box — a floor built without the within-outline clamp (Arrange centres it
            // on the field, ~[54,74]) escapes [8,32]. And its Лестница must land on the column.
            {
                float loMinX = 8f, loMinY = 8f, loMaxX = 32f, loMaxY = 32f;
                float colX = 26f, colY = 26f;   // a column near the box's far corner
                var upper = BuildingGenerator.GenerateFloorAroundColumn(
                    new System.Random(20260718), roomBudget: 6, colX, colY, 4, 4,
                    loMinX, loMinY, loMaxX, loMaxY, out var upStair);

                var hi = Bbox(upper);
                bool contained = hi.minX >= loMinX - Eps && hi.minY >= loMinY - Eps
                              && hi.maxX <= loMaxX + Eps && hi.maxY <= loMaxY + Eps;
                if (!contained)
                { Debug.LogError($"FAIL nesting: off-centre fixture upper bbox [{hi.minX:F1},{hi.minY:F1}..{hi.maxX:F1},{hi.maxY:F1}] escaped box [8,8..32,32] (within-outline clamp missing?)"); ok = false; }

                float sdx = Mathf.Abs(upStair.X * DungeonLayout.TilesPerAxis - colX);
                float sdy = Mathf.Abs(upStair.Y * DungeonLayout.TilesPerAxis - colY);
                if (sdx > 1f || sdy > 1f)
                { Debug.LogError($"FAIL nesting: off-centre fixture Лестница off the column by ({sdx:F1},{sdy:F1})"); ok = false; }
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

            Debug.Log(ok ? "Self-Test Building Generator: PASS" : "Self-Test Building Generator: FAIL");
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
