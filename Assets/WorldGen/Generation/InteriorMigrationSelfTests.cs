using UnityEngine;
using Newtonsoft.Json;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public class InteriorMigrationSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Interior v6 Migration")]
        public void SelfTestMigration()
        {
            bool ok = true;
            // A hand-written v6-shaped dungeon JSON (old keys: Levels/Corridors/Secrets/Type + old
            // SecretPassage with TargetLevelIndex, no Kind, no Hidden).
            string v6 = @"{ ""OwnerPoiId"":""p1"", ""Levels"":[ {
                ""Rooms"":[
                    {""Id"":1,""Type"":0,""X"":0.2,""Y"":0.2,""Secrets"":[]},
                    {""Id"":2,""Type"":2,""X"":0.8,""Y"":0.8,
                     ""Secrets"":[{""Kind"":1,""TargetLevelIndex"":1,""TargetRoomId"":1,""Bidirectional"":true,""Label"":""e""}]}
                ],
                ""Corridors"":[{""RoomA"":1,""RoomB"":2}], ""NextRoomId"":3 } ] }";
            var d = JsonConvert.DeserializeObject<InteriorData>(v6);

            ok &= d.Kind == InteriorKind.Dungeon;                 // absent Kind defaults to Dungeon
            ok &= d.Floors.Count == 1 && d.Floors[0].Links.Count == 1;   // "Levels"/"Corridors" mapped
            var r2 = d.Floors[0].GetRoom(2);
            ok &= r2.TypeId == 2;                                  // "Type":2 → TypeId 2 (Boss)
            ok &= r2.Portals.Count == 1;                           // "Secrets" → Portals
            var p = r2.Portals[0];
            ok &= p.Hidden == true;                               // absent Hidden defaults to legacy-secret
            ok &= p.Kind == PortalKind.DungeonExit;              // old SecretTargetKind.DungeonExit(1) int-maps
            ok &= p.TargetFloorIndex == 1;                        // "TargetLevelIndex":1 mapped (nonzero ≠ field default 0, so a dropped attribute WOULD fail this)

            Debug.Log(ok ? "Self-Test Interior v6 Migration: PASS" : "Self-Test Interior v6 Migration: FAIL");
        }

        [ContextMenu("Self-Test: Building Round-Trip")]
        public void SelfTestBuildingRoundTrip()
        {
            // Buildings are unreleased (no FormatVersion bump needed), but they serialize as the SAME InteriorData
            // as dungeons, so a save/load must preserve Kind, every floor's single Лестница column, and all links/
            // portals — i.e. the loaded building still VALIDATES clean. NormalizeTypes is what the load path runs.
            bool ok = true;
            var b = BuildingGenerator.Generate(seed: 11, ownerPoiId: "p", roomCount: 6, floorCount: 3);
            string json = JsonConvert.SerializeObject(b);
            var d = JsonConvert.DeserializeObject<InteriorData>(json);
            BuildingGenerator.NormalizeTypes(d);

            ok &= d.Kind == InteriorKind.Building;                 // Kind survives the round-trip (else reads as Dungeon)
            ok &= d.Floors.Count == b.Floors.Count;
            for (int f = 0; f < d.Floors.Count && ok; f++)
            {
                int stairs = 0; foreach (var r in d.Floors[f].Rooms) if (r.TypeId == 2) stairs++;
                ok &= stairs == 1;                                 // the shared column survived on every floor
                ok &= d.Floors[f].Rooms.Count == b.Floors[f].Rooms.Count;
            }
            foreach (var iss in DungeonValidator.Validate(d))
                if (iss.Severity == IssueSeverity.Error) { ok = false; Debug.LogError($"FAIL round-trip: validate error — {iss.Message}"); }

            Debug.Log(ok ? "Self-Test Building Round-Trip: PASS" : "Self-Test Building Round-Trip: FAIL");
        }
    }
}
