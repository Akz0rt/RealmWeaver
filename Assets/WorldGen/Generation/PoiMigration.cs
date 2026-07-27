using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// Legacy POI normalization. The ONLY place that knows PoiType.Village == 5 ever existed.
    public static class PoiMigration
    {
        public const int LegacyVillageTypeId = 5;

        /// Every POI whose stored type is the removed Village becomes a City. Idempotent: a list with no
        /// legacy types is untouched. Never renumbers any other value — Tower=6, Temple=7, Port=10 keep
        /// their numbers or every older save breaks.
        public static void NormalizeLegacyTypes(List<PoiData> pois)
        {
            if (pois == null) return;
            for (int i = 0; i < pois.Count; i++)
            {
                var p = pois[i];
                if (p != null && (int)p.Type == LegacyVillageTypeId) p.Type = PoiType.City;
            }
        }
    }
}
