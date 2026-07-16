using System.Linq;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for DungeonOps + DungeonValidator — add to any GameObject,
    /// run from the Inspector, remove after (don't save the scene).</summary>
    public class DungeonGraphSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Dungeon Ops")]
        public void SelfTestOps()
        {
            bool ok = true;

            // AddRoom assigns a fresh id and bumps NextRoomId.
            var lvl = new DungeonLevel();
            var r1 = DungeonOps.AddRoom(lvl, 0.1f, 0.1f);
            var r2 = DungeonOps.AddRoom(lvl, 0.2f, 0.2f);
            ok &= r1.Id == 1 && r2.Id == 2 && lvl.NextRoomId == 3 && lvl.Rooms.Count == 2;

            // AddCorridor: happy path, self-loop, duplicate, missing.
            ok &= DungeonOps.AddCorridor(lvl, 1, 2) == null && lvl.Corridors.Count == 1;
            ok &= DungeonOps.AddCorridor(lvl, 1, 1) != null;      // self
            ok &= DungeonOps.AddCorridor(lvl, 2, 1) != null;      // duplicate (order-independent)
            ok &= DungeonOps.AddCorridor(lvl, 1, 99) != null;     // missing
            ok &= lvl.Corridors.Count == 1;

            // Singleton conflict + SetRoomType demote.
            DungeonOps.SetRoomType(lvl, 1, RoomType.Entrance);
            ok &= DungeonOps.FindSingletonConflict(lvl, 2, RoomType.Entrance) == 1;
            DungeonOps.SetRoomType(lvl, 2, RoomType.Entrance);    // should demote r1
            ok &= lvl.GetRoom(1).Type == RoomType.Normal && lvl.GetRoom(2).Type == RoomType.Entrance;
            ok &= DungeonOps.FindSingletonConflict(lvl, 2, RoomType.Normal) == 0;   // Normal is not singleton

            // RemoveRoom integrity: corridors + secrets (owned and cross-level targeting) vanish.
            var dungeon = new DungeonData();
            var l0 = new DungeonLevel(); var l1 = new DungeonLevel();
            dungeon.Levels.Add(l0); dungeon.Levels.Add(l1);
            var a = DungeonOps.AddRoom(l0, 0, 0); var b = DungeonOps.AddRoom(l0, 0, 0);
            var c = DungeonOps.AddRoom(l1, 0, 0);
            DungeonOps.AddCorridor(l0, a.Id, b.Id);
            b.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 0, TargetRoomId = a.Id });
            c.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 0, TargetRoomId = a.Id });
            DungeonOps.RemoveRoom(dungeon, 0, a.Id);
            ok &= l0.GetRoom(a.Id) == null && l0.Corridors.Count == 0;
            ok &= b.Secrets.Count == 0 && c.Secrets.Count == 0;   // both the owned and the cross-level target-secret removed

            Debug.Log(ok ? "Self-Test Dungeon Ops: PASS" : "Self-Test Dungeon Ops: FAIL");
        }

        [ContextMenu("Self-Test: Dungeon Validator")]
        public void SelfTestValidator()
        {
            bool ok = true;

            // Clean floor from the generator → no errors.
            var clean = new DungeonData { Levels = { DungeonGraphGenerator.Generate(5, 8) } };
            var cleanIssues = DungeonValidator.Validate(clean);
            ok &= cleanIssues.All(i => i.Severity != IssueSeverity.Error);

            // No entrance → error. Two entrances → error.
            var lvl = new DungeonLevel();
            var a = DungeonOps.AddRoom(lvl, 0, 0); var b = DungeonOps.AddRoom(lvl, 0, 0);
            var d0 = new DungeonData { Levels = { lvl } };
            ok &= DungeonValidator.Validate(d0).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("вход"));
            DungeonOps.SetRoomType(lvl, a.Id, RoomType.Entrance);
            b.Type = RoomType.Entrance;   // force a second entrance without demote
            ok &= DungeonValidator.Validate(d0).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("вход"));

            // Two bosses → error.
            var lvl2 = new DungeonLevel();
            var e = DungeonOps.AddRoom(lvl2, 0, 0); var f = DungeonOps.AddRoom(lvl2, 0, 0); var g = DungeonOps.AddRoom(lvl2, 0, 0);
            DungeonOps.SetRoomType(lvl2, e.Id, RoomType.Entrance);
            f.Type = RoomType.Boss; g.Type = RoomType.Boss;
            var d1 = new DungeonData { Levels = { lvl2 } };
            ok &= DungeonValidator.Validate(d1).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("босс"));

            // Orphan warning: entrance + a disconnected room.
            var lvl3 = new DungeonLevel();
            var h = DungeonOps.AddRoom(lvl3, 0, 0); var iRoom = DungeonOps.AddRoom(lvl3, 0, 0);
            DungeonOps.SetRoomType(lvl3, h.Id, RoomType.Entrance);   // iRoom left unconnected
            var d2 = new DungeonData { Levels = { lvl3 } };
            ok &= DungeonValidator.Validate(d2).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("недостижим"));

            // Dangling secret target → error.
            var lvl4 = new DungeonLevel();
            var j = DungeonOps.AddRoom(lvl4, 0, 0);
            DungeonOps.SetRoomType(lvl4, j.Id, RoomType.Entrance);
            j.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 0, TargetRoomId = 999 });
            var d3 = new DungeonData { Levels = { lvl4 } };
            ok &= DungeonValidator.Validate(d3).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("секретный"));

            Debug.Log(ok ? "Self-Test Dungeon Validator: PASS" : "Self-Test Dungeon Validator: FAIL");
        }
    }
}
