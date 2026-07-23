using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Pure queries/mutations over the project's FLAT interior list for building-in-town
    /// ownership (Ц2): a building interior's OwnerPoiId is the town's POI and OwnerRoomId is the
    /// building node's id; OwnerRoomId == 0 = owned by the POI directly (every pre-Ц2 interior).
    /// No Unity types — headless + self-testable.</summary>
    public static class InteriorOps
    {
        public static InteriorData FindBuildingInterior(IReadOnlyList<InteriorData> all, string poiId, int roomId)
        {
            if (all == null || string.IsNullOrEmpty(poiId) || roomId == 0) return null;
            foreach (var d in all)
                if (d != null && d.OwnerPoiId == poiId && d.OwnerRoomId == roomId) return d;
            return null;
        }

        /// <summary>Every interior of the POI — the town AND all its buildings (POI deletion).</summary>
        public static int RemoveOwnedInteriors(List<InteriorData> all, string poiId)
        {
            if (all == null || string.IsNullOrEmpty(poiId)) return 0;
            return all.RemoveAll(d => d != null && d.OwnerPoiId == poiId);
        }

        /// <summary>One building node's interior (node deletion).</summary>
        public static int RemoveOwnedInteriors(List<InteriorData> all, string poiId, int roomId)
        {
            if (all == null || string.IsNullOrEmpty(poiId) || roomId == 0) return 0;
            return all.RemoveAll(d => d != null && d.OwnerPoiId == poiId && d.OwnerRoomId == roomId);
        }

        /// <summary>Every BUILDING interior of the POI, town kept («Сгенерировать заново»: the town
        /// floor is replaced in place; its buildings' interiors die with their nodes).</summary>
        public static int RemoveBuildingInteriors(List<InteriorData> all, string poiId)
        {
            if (all == null || string.IsNullOrEmpty(poiId)) return 0;
            return all.RemoveAll(d => d != null && d.OwnerPoiId == poiId && d.OwnerRoomId != 0);
        }

        /// <summary>Feeds the regenerate confirm: does the POI own at least one building interior?</summary>
        public static bool HasBuildingInteriors(IReadOnlyList<InteriorData> all, string poiId)
        {
            if (all == null || string.IsNullOrEmpty(poiId)) return false;
            foreach (var d in all)
                if (d != null && d.OwnerPoiId == poiId && d.OwnerRoomId != 0) return true;
            return false;
        }

        /// <summary>Every building-node room id under the POI that already has its own interior on file —
        /// feeds DungeonFlatRenderer's has-interior corner mark (Ц2 Task 5). Same OwnerRoomId != 0 filter
        /// as HasBuildingInteriors above, just collected into a set instead of short-circuited into a
        /// bool. OwnerRoomId 0 (the town itself) is excluded by construction: no interior is ever added
        /// with OwnerRoomId 0 for a building (see AddInterior's doc).</summary>
        public static HashSet<int> RoomsWithInterior(IReadOnlyList<InteriorData> all, string poiId)
        {
            var result = new HashSet<int>();
            if (all == null || string.IsNullOrEmpty(poiId)) return result;
            foreach (var d in all)
                if (d != null && d.OwnerPoiId == poiId && d.OwnerRoomId != 0) result.Add(d.OwnerRoomId);
            return result;
        }

        /// <summary>Deterministic building-interior seed from its owner pair. Explicit FNV-1a over the
        /// poi-id chars, then the room id — string.GetHashCode is NOT stable across runtimes and must
        /// never feed persisted content.</summary>
        public static int BuildingSeed(string poiId, int roomId)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (poiId != null)
                    foreach (char c in poiId) { h ^= c; h *= 16777619u; }
                h ^= (uint)roomId; h *= 16777619u;
                return (int)(h & 0x7FFFFFFF);
            }
        }
    }
}
