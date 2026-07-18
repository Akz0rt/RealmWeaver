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
            // Remove the stair-linking loop entirely -> stairs == 0 -> fails. Point TargetFloorIndex at the
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

            // ---- 5. Rooms sized in the generator's OWN modest range 4..6 -------------------------------
            // Bound = the generator's exact roll range (rng.Next(4,7)). Tighter than "1..8" ON PURPOSE:
            // routing sizes through RoomSizing.Roll instead (dungeon ranges 4..8 / 5..8, Boss 8..14) would
            // roll a 7 or 8 — for THIS seed/params the review verified the RoomSizing path peaks at 8 > 6, so
            // the regression this guard names actually FAILS it. A "<=8" bound passed vacuously (no TypeId-2
            // room rolls for seed 42, and the other types' RoomSizing max is 8 anyway).
            foreach (var fl in b.Floors)
                foreach (var r in fl.Rooms)
                    if (!(r.SizeW >= 4 && r.SizeW <= 6 && r.SizeH >= 4 && r.SizeH <= 6))
                    {
                        Debug.LogError($"FAIL size: room {r.Id} is {r.SizeW}x{r.SizeH}, want 4..6 (building-modest, not RoomSizing/Boss-sized)");
                        ok = false;
                    }

            // ---- 6. Determinism: same seed -> identical structure ---------------------------------------
            // Per-floor room/link counts, each room's TypeId & size, and each floor pair's stair target must
            // all match between two independent runs of the same seed.
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
                            || fa.Rooms[i].SizeH != fb.Rooms[i].SizeH)
                        {
                            Debug.LogError($"FAIL determinism: floor {f} room index {i} TypeId/size differs between two runs");
                            ok = false;
                        }
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
