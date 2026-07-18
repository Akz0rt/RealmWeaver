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

            // ---- 2. Exactly one entrance (TypeId 0) on floor 0 ------------------------------------------
            // Remove the "room 1 on floor 0 = entrance" rule and this count drifts away from 1.
            int e0 = 0;
            foreach (var r in b.Floors[0].Rooms) if (r.TypeId == 0) e0++;
            if (e0 != 1)
            { Debug.LogError($"FAIL entrance: floor 0 has {e0} TypeId==0 rooms, want exactly 1"); ok = false; }

            // ---- 3. NO entrance anywhere on floors > 0 --------------------------------------------------
            // Remove the "floor 0 only" guard (e.g. let every floor's room 0 roll a raw 0..4 type) and this
            // fires on floor 1/2.
            for (int f = 1; f < b.Floors.Count; f++)
                foreach (var r in b.Floors[f].Rooms)
                    if (r.TypeId == 0)
                    {
                        Debug.LogError($"FAIL entrance: floor {f} room {r.Id} has TypeId 0 (entrance leaked to an upper floor)");
                        ok = false;
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

            // ---- 7. Floor coherence: every upper floor's footprint bbox is CONTAINED in the floor below ---
            // The invariant that makes floors read as one nesting building. NON-VACUOUS here because each
            // upper floor is placed OFF-CENTRE (biased over the lower floor's peripheral stair room), so the
            // floors are NOT concentric: pair (1,2) in particular has floor 1 well off the field centre, and
            // an upper floor placed without the ArrangeWithin/nest step (Arrange centres it on the field)
            // would stick out. A concentric small-inside-big case could pass without the constraint; these
            // off-centre floors cannot. (Test 8 below pins this proof with a hand-built off-centre fixture.)
            var bc = BuildingGenerator.Generate(seed: 7, ownerPoiId: "p", roomCount: 8, floorCount: 3);
            for (int f = 1; f < bc.Floors.Count; f++)
            {
                var lo = Bbox(bc.Floors[f - 1]);
                var hi = Bbox(bc.Floors[f]);
                bool contained = hi.minX >= lo.minX - Eps && hi.minY >= lo.minY - Eps
                              && hi.maxX <= lo.maxX + Eps && hi.maxY <= lo.maxY + Eps;
                if (!contained)
                { Debug.LogError($"FAIL coherence: floor {f} bbox [{hi.minX:F1},{hi.minY:F1}..{hi.maxX:F1},{hi.maxY:F1}] not within floor {f - 1} [{lo.minX:F1},{lo.minY:F1}..{lo.maxX:F1},{lo.maxY:F1}]"); ok = false; }
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

            // ---- 7c. Stairs roughly above: each pair's two stair rooms are within StairAlignTol on X AND Y -
            // Fails if the alignment/nesting bias is removed: the lower stair room is a PERIPHERAL room, so an
            // un-nested (field-centred) upper floor's rooms all sit far from it, exceeding the tolerance.
            for (int f = 0; f < bc.Floors.Count - 1; f++)
            {
                if (!StairRooms(bc, f, out var lowerRoom, out var upperRoom))
                { Debug.LogError($"FAIL stair-align: pair {f} has no resolvable stair endpoints"); ok = false; continue; }
                float dx = Mathf.Abs(lowerRoom.X - upperRoom.X) * DungeonLayout.TilesPerAxis;
                float dy = Mathf.Abs(lowerRoom.Y - upperRoom.Y) * DungeonLayout.TilesPerAxis;
                if (dx > BuildingGenerator.StairAlignTol || dy > BuildingGenerator.StairAlignTol)
                { Debug.LogError($"FAIL stair-align: pair {f} stair rooms off by ({dx:F1},{dy:F1}) tiles, tol {BuildingGenerator.StairAlignTol}"); ok = false; }
            }

            // ---- 8. Nesting is REAL, not concentric-luck (the non-vacuous proof) ------------------------
            // Feed the nesting entry point a hand-built OFF-CENTRE lower bbox: a 24x24-tile box in the field's
            // top-left, whose CENTRE (~20,20) is nowhere near the field centre (64,64). A correctly nested
            // upper floor lands inside it. Remove the ArrangeWithin translate (Arrange alone centres the floor
            // at the field centre, bbox ~[54,74]) and the result falls ENTIRELY outside [8,32] -> contained
            // goes false -> FAIL. That is exactly what makes this containment assertion non-vacuous: the
            // lower bbox does not straddle the field centre, so a field-centred floor cannot accidentally sit
            // inside it.
            {
                float loMinX = 8f, loMinY = 8f, loMaxX = 32f, loMaxY = 32f;
                float stairX = 28f, stairY = 28f;   // a stair point near the box's far corner
                var upper = BuildingGenerator.GenerateNestedUpperFloor(
                    new System.Random(20260718), roomBudget: 6,
                    loMinX, loMinY, loMaxX, loMaxY, stairX, stairY, out var downStair);

                var hi = Bbox(upper);
                bool contained = hi.minX >= loMinX - Eps && hi.minY >= loMinY - Eps
                              && hi.maxX <= loMaxX + Eps && hi.maxY <= loMaxY + Eps;
                if (!contained)
                { Debug.LogError($"FAIL nesting: off-centre fixture upper bbox [{hi.minX:F1},{hi.minY:F1}..{hi.maxX:F1},{hi.maxY:F1}] escaped lower box [8,8..32,32] (ArrangeWithin nest missing?)"); ok = false; }

                // The nudged stair room sits within tolerance of the (off-centre) lower stair point. Remove
                // the nudge/nearest-room bias and this fires -- a field-centred floor's stair room is ~36 tiles
                // from (28,28).
                float sdx = Mathf.Abs(downStair.X * DungeonLayout.TilesPerAxis - stairX);
                float sdy = Mathf.Abs(downStair.Y * DungeonLayout.TilesPerAxis - stairY);
                if (sdx > BuildingGenerator.StairAlignTol || sdy > BuildingGenerator.StairAlignTol)
                { Debug.LogError($"FAIL nesting: off-centre fixture stair room off by ({sdx:F1},{sdy:F1}) from (28,28)"); ok = false; }
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

        // Resolve the two stair-portal ENDPOINT rooms for floor pair f -> f+1: the lower room hosting the
        // non-hidden Stairs portal, and the upper room it targets. False if no such portal.
        static bool StairRooms(InteriorData data, int f, out Room lowerRoom, out Room upperRoom)
        {
            lowerRoom = null; upperRoom = null;
            foreach (var r in data.Floors[f].Rooms)
                foreach (var p in r.Portals)
                    if (p.Kind == PortalKind.Stairs && !p.Hidden && p.TargetFloorIndex == f + 1)
                    {
                        lowerRoom = r;
                        upperRoom = data.Floors[f + 1].GetRoom(p.TargetRoomId);
                        return upperRoom != null;
                    }
            return false;
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
