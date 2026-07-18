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
                     ""Secrets"":[{""Kind"":1,""TargetLevelIndex"":0,""TargetRoomId"":1,""Bidirectional"":true,""Label"":""e""}]}
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
            ok &= p.TargetFloorIndex == 0;                        // "TargetLevelIndex" mapped

            Debug.Log(ok ? "Self-Test Interior v6 Migration: PASS" : "Self-Test Interior v6 Migration: FAIL");
        }
    }
}
