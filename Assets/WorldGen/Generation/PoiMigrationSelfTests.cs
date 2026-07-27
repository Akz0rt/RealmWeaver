using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for legacy POI normalization.</summary>
    public class PoiMigrationSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Poi Legacy Types")]
        public void SelfTestPoiLegacyTypes()
        {
            bool ok = true;

            // A save written before Village was removed carries the raw value 5. C# lets an enum field hold
            // an undefined value, which is exactly how such a save deserializes, so this fixture is the real
            // shape of the data — not an artificial one.
            var pois = new List<PoiData>
            {
                new PoiData { Type = (PoiType)PoiMigration.LegacyVillageTypeId, Name = "Старая деревня" },
                new PoiData { Type = PoiType.Tower,  Name = "Башня" },
                new PoiData { Type = PoiType.Port,   Name = "Порт" },
            };
            PoiMigration.NormalizeLegacyTypes(pois);

            if (pois[0].Type != PoiType.City)
            { Debug.LogError($"FAIL poi migration: a legacy Village loaded as {(int)pois[0].Type}, want City({(int)PoiType.City})"); ok = false; }
            if (pois[1].Type != PoiType.Tower || pois[2].Type != PoiType.Port)
            { Debug.LogError($"FAIL poi migration: renumbered a bystander — Tower became {(int)pois[1].Type}, Port became {(int)pois[2].Type}"); ok = false; }

            // Idempotent: running it again changes nothing.
            PoiMigration.NormalizeLegacyTypes(pois);
            if (pois[0].Type != PoiType.City || pois[1].Type != PoiType.Tower || pois[2].Type != PoiType.Port)
            { Debug.LogError("FAIL poi migration: a second pass changed an already-normalized list"); ok = false; }

            // A null list must not throw — a corrupt save can deserialize Pois as null.
            PoiMigration.NormalizeLegacyTypes(null);

            if (ok) Debug.Log("Poi Legacy Types: PASS");
        }

        [ContextMenu("Self-Test: Poi Migration Sentinel")]
        public void SelfTestPoiMigrationSentinel()
        {
            // Trailing sentinel: never mutant-rebound, so the rebind's method scan always has a terminator
            // after the real test above. Asserts nothing.
            Debug.Log("Poi Migration Sentinel: PASS");
        }
    }
}
