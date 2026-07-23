using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for InteriorOps (Ц2 building recursion): find/remove/has over a
    /// flat interior list keyed by (OwnerPoiId, OwnerRoomId), plus BuildingSeed's determinism. Every
    /// assertion names the exact interior/count the rule changes — same discipline as SettlementSelfTests.</summary>
    public class InteriorOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Interior Ops")]
        public void SelfTestInteriorOps()
        {
            bool ok = true;

            // ---- Fixture: a town (p1,0) with two buildings (p1,3)/(p1,5), plus an unrelated foreign
            // interior (p2,3) that must never be touched by any p1 operation. -----------------------------
            var town = new InteriorData { OwnerPoiId = "p1", OwnerRoomId = 0 };
            var bld3 = new InteriorData { OwnerPoiId = "p1", OwnerRoomId = 3 };
            var bld5 = new InteriorData { OwnerPoiId = "p1", OwnerRoomId = 5 };
            var foreign = new InteriorData { OwnerPoiId = "p2", OwnerRoomId = 3 };
            var list = new System.Collections.Generic.List<InteriorData> { town, bld3, bld5, foreign };

            // ---- 0. RoomsWithInterior: (p1) == {3,5}, never the town's own id (0); (p2) == {3} only — must
            // not leak p1's building room id 5 across the poiId scope. --------------------------------------
            var p1Rooms = InteriorOps.RoomsWithInterior(list, "p1");
            if (p1Rooms.Count != 2 || !p1Rooms.Contains(3) || !p1Rooms.Contains(5) || p1Rooms.Contains(0))
            { Debug.LogError($"FAIL rooms-with-interior: RoomsWithInterior(\"p1\") = {{{string.Join(",", p1Rooms)}}}, want {{3,5}} (never 0 — that's the town)"); ok = false; }
            var p2Rooms = InteriorOps.RoomsWithInterior(list, "p2");
            if (p2Rooms.Count != 1 || !p2Rooms.Contains(3) || p2Rooms.Contains(5))
            { Debug.LogError($"FAIL rooms-with-interior: RoomsWithInterior(\"p2\") = {{{string.Join(",", p2Rooms)}}}, want {{3}} — must not leak p1's building room id 5"); ok = false; }

            // ---- 1. FindBuildingInterior: hits (p1,3), misses (p1,7)/(p2,5)/roomId 0 --------------------
            if (InteriorOps.FindBuildingInterior(list, "p1", 3) != bld3)
            { Debug.LogError("FAIL find: FindBuildingInterior(\"p1\",3) did not return the (p1,3) building"); ok = false; }
            if (InteriorOps.FindBuildingInterior(list, "p1", 7) != null)
            { Debug.LogError("FAIL find: FindBuildingInterior(\"p1\",7) matched a room that does not exist"); ok = false; }
            if (InteriorOps.FindBuildingInterior(list, "p2", 5) != null)
            { Debug.LogError("FAIL find: FindBuildingInterior(\"p2\",5) matched — wrong poi/room pairing"); ok = false; }
            if (InteriorOps.FindBuildingInterior(list, "p1", 0) != null)
            { Debug.LogError("FAIL find: FindBuildingInterior(\"p1\",0) matched the TOWN interior — roomId 0 must never resolve as a building"); ok = false; }

            // ---- 2. RemoveOwnedInteriors(list,"p1",3): removes exactly 1; every sibling survives ---------
            // THE sibling assertion (Task 2's mutant target): a broad filter that drops the roomId term
            // would remove bld5 (and even town) here too.
            int removedOne = InteriorOps.RemoveOwnedInteriors(list, "p1", 3);
            if (removedOne != 1)
            { Debug.LogError($"FAIL remove-one: RemoveOwnedInteriors(list,\"p1\",3) removed {removedOne}, want 1"); ok = false; }
            if (list.Contains(bld3))
            { Debug.LogError("FAIL remove-one: (p1,3) is still present after its own removal"); ok = false; }
            if (!list.Contains(bld5))
            { Debug.LogError("FAIL remove-one: sibling building (p1,5) was removed alongside (p1,3)"); ok = false; }
            if (!list.Contains(town))
            { Debug.LogError("FAIL remove-one: town interior (p1,0) was removed by a single-building removal"); ok = false; }
            if (!list.Contains(foreign))
            { Debug.LogError("FAIL remove-one: foreign interior (p2,3) was removed by a p1 operation"); ok = false; }

            // ---- 3. HasBuildingInteriors("p1") is true while (p1,5) still stands -------------------------
            if (!InteriorOps.HasBuildingInteriors(list, "p1"))
            { Debug.LogError("FAIL has-before: HasBuildingInteriors(\"p1\") is false while (p1,5) is still in the list"); ok = false; }

            // ---- 4. RemoveBuildingInteriors("p1"): removes exactly the remaining building; town+foreign kept
            int removedBuildings = InteriorOps.RemoveBuildingInteriors(list, "p1");
            if (removedBuildings != 1)
            { Debug.LogError($"FAIL remove-buildings: RemoveBuildingInteriors(\"p1\") removed {removedBuildings}, want 1"); ok = false; }
            if (list.Contains(bld5))
            { Debug.LogError("FAIL remove-buildings: (p1,5) is still present after RemoveBuildingInteriors"); ok = false; }
            if (!list.Contains(town))
            { Debug.LogError("FAIL remove-buildings: town interior (p1,0) was removed — buildings-only must keep the town"); ok = false; }
            if (!list.Contains(foreign))
            { Debug.LogError("FAIL remove-buildings: foreign interior (p2,3) was removed"); ok = false; }

            // ---- 5. HasBuildingInteriors("p1") flips to false now that no p1 building remains -------------
            if (InteriorOps.HasBuildingInteriors(list, "p1"))
            { Debug.LogError("FAIL has-after: HasBuildingInteriors(\"p1\") is still true after every p1 building was removed"); ok = false; }

            // ---- 6. RemoveOwnedInteriors(list,"p1"): empties p1 (the town) entirely; foreign intact -------
            int removedAll = InteriorOps.RemoveOwnedInteriors(list, "p1");
            if (removedAll != 1)
            { Debug.LogError($"FAIL remove-all: RemoveOwnedInteriors(list,\"p1\") removed {removedAll}, want 1 (the town, its buildings already gone)"); ok = false; }
            if (list.Contains(town))
            { Debug.LogError("FAIL remove-all: town interior (p1,0) is still present after RemoveOwnedInteriors(\"p1\")"); ok = false; }
            if (!list.Contains(foreign))
            { Debug.LogError("FAIL remove-all: foreign interior (p2,3) was removed by a p1 POI-deletion"); ok = false; }
            if (list.Count != 1)
            { Debug.LogError($"FAIL remove-all: {list.Count} interiors remain, want exactly 1 (the foreign one)"); ok = false; }

            Debug.Log(ok ? "Interior Ops: PASS" : "Interior Ops: FAIL");
        }

        [ContextMenu("Self-Test: Building Seed Pin")]
        public void SelfTestBuildingSeedPin()
        {
            bool ok = true;

            // ---- Determinism: same inputs -> same value ---------------------------------------------------
            int seedA = InteriorOps.BuildingSeed("p1", 3);
            int seedA2 = InteriorOps.BuildingSeed("p1", 3);
            if (seedA != seedA2)
            { Debug.LogError($"FAIL seed-stable: BuildingSeed(\"p1\",3) returned {seedA} then {seedA2} for identical inputs"); ok = false; }

            // ---- Differing roomId -> a different seed ------------------------------------------------------
            int seedRoom = InteriorOps.BuildingSeed("p1", 5);
            if (seedRoom == seedA)
            { Debug.LogError($"FAIL seed-room: BuildingSeed(\"p1\",5) == BuildingSeed(\"p1\",3) == {seedA}, want a differing roomId to change the seed"); ok = false; }

            // ---- Differing poiId -> a different seed -------------------------------------------------------
            int seedPoi = InteriorOps.BuildingSeed("p2", 3);
            if (seedPoi == seedA)
            { Debug.LogError($"FAIL seed-poi: BuildingSeed(\"p2\",3) == BuildingSeed(\"p1\",3) == {seedA}, want a differing poiId to change the seed"); ok = false; }

            // ---- PINNED: BuildingSeed("p1",3) computed once from the shipped FNV-1a formula and hard-coded --
            // here — guards against silent formula drift (offset basis / prime / XOR-multiply order) that the
            // determinism/distinctness checks above cannot catch on their own, since they only compare outputs
            // to each other, never to a value fixed outside the function.
            const int PinnedP1Room3Seed = 1427873915;   // BuildingSeed("p1", 3), computed once via the harness
            if (seedA != PinnedP1Room3Seed)
            { Debug.LogError($"FAIL seed-pin: BuildingSeed(\"p1\",3) = {seedA}, want the pinned {PinnedP1Room3Seed} — formula drift?"); ok = false; }

            Debug.Log(ok ? "Building Seed Pin: PASS" : "Building Seed Pin: FAIL");
        }
    }
}
