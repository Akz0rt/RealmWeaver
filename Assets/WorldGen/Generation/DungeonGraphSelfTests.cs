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

        [ContextMenu("Self-Test: Dungeon Remove-Level Integrity")]
        public void SelfTestRemoveLevel()
        {
            bool ok = true;
            var d = new DungeonData();
            for (int i = 0; i < 3; i++) d.Levels.Add(new DungeonLevel());
            var r0 = DungeonOps.AddRoom(d.Levels[0], 0, 0);
            var r2 = DungeonOps.AddRoom(d.Levels[2], 0, 0);
            r0.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 0, TargetRoomId = r0.Id });
            r0.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 1, TargetRoomId = 5 });   // targets the removed level
            r0.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.Room, TargetLevelIndex = 2, TargetRoomId = r2.Id }); // level above → shifts to 1
            r0.Secrets.Add(new SecretPassage { Kind = SecretTargetKind.DungeonExit });

            DungeonOps.RemoveLevel(d, 1);

            ok &= d.Levels.Count == 2;
            ok &= r0.Secrets.Count == 3;                                                                    // the level-1 target was removed
            ok &= r0.Secrets.Exists(s => s.Kind == SecretTargetKind.Room && s.TargetLevelIndex == 0 && s.TargetRoomId == r0.Id);   // unchanged
            ok &= r0.Secrets.Exists(s => s.Kind == SecretTargetKind.Room && s.TargetLevelIndex == 1 && s.TargetRoomId == r2.Id);   // was 2 → 1
            ok &= !r0.Secrets.Exists(s => s.TargetRoomId == 5);                                             // removed
            ok &= r0.Secrets.Exists(s => s.Kind == SecretTargetKind.DungeonExit);                          // unchanged

            Debug.Log(ok ? "Self-Test Dungeon Remove-Level Integrity: PASS" : "Self-Test Dungeon Remove-Level Integrity: FAIL");
        }

        [ContextMenu("Self-test: DungeonProjection round-trip")]
        public void SelfTestProjectionRoundTrip()
        {
            bool ok = true;

            // Round-trip must hold for BOTH views and with a non-zero pan.
            foreach (float squash in new[] { 1.0f, 0.5f })
            {
                var p = new DungeonProjection { PxPerTile = 17.5f, SquashY = squash, PanX = -123.4f, PanY = 77.7f };
                foreach (var t in new[] { (0f, 0f), (24f, 24f), (3.5f, 47.25f), (47f, 1f) })
                {
                    var (lx, ly) = p.TileToLocal(t.Item1, t.Item2);
                    var (tx, ty) = p.LocalToTile(lx, ly);
                    if (Mathf.Abs(tx - t.Item1) > 1e-3f || Mathf.Abs(ty - t.Item2) > 1e-3f)
                    {
                        Debug.LogError($"FAIL round-trip squash={squash} tile=({t.Item1},{t.Item2}) -> ({tx},{ty})");
                        ok = false;
                    }
                }
            }

            // Flat view must be ISOTROPIC: one tile is the same pixel count on both axes (regression for B3).
            var flat = new DungeonProjection { PxPerTile = 10f, SquashY = 1f, PanX = 0f, PanY = 0f };
            var (ax, ay) = flat.TileToLocal(1f, 0f);
            var (bx, by) = flat.TileToLocal(0f, 1f);
            if (Mathf.Abs(ax - 10f) > 1e-3f || Mathf.Abs(by + 10f) > 1e-3f)
            { Debug.LogError($"FAIL isotropy: x-step={ax} y-step={by} (want 10 / -10)"); ok = false; }

            // Iso squashes Y by exactly half, X untouched.
            var iso = new DungeonProjection { PxPerTile = 10f, SquashY = 0.5f, PanX = 0f, PanY = 0f };
            var (cx, cy) = iso.TileToLocal(1f, 1f);
            if (Mathf.Abs(cx - 10f) > 1e-3f || Mathf.Abs(cy + 5f) > 1e-3f)
            { Debug.LogError($"FAIL iso squash: ({cx},{cy}) want (10,-5)"); ok = false; }

            // Tile Y grows DOWN (south) but UI local Y grows UP → projected y must be negative for +ty.
            if (cy >= 0f) { Debug.LogError("FAIL: +tileY must project to NEGATIVE local y"); ok = false; }

            // Fit(): the content centre lands at local (0,0) and everything fits inside the rect.
            var lvl = new DungeonLevel();
            lvl.Rooms.Add(new Room { Id = 1, Type = RoomType.Normal, X = 0.25f, Y = 0.25f, SizeW = 6, SizeH = 6 });
            lvl.Rooms.Add(new Room { Id = 2, Type = RoomType.Boss,   X = 0.75f, Y = 0.75f, SizeW = 10, SizeH = 10 });
            var fit = DungeonProjection.Fit(lvl, 800f, 400f, 0.5f);
            var (bminX, bminY, bmaxX, bmaxY) = DungeonProjection.ContentBoundsTiles(lvl);
            var (ccx, ccy) = fit.TileToLocal((bminX + bmaxX) * 0.5f, (bminY + bmaxY) * 0.5f);
            if (Mathf.Abs(ccx) > 1e-2f || Mathf.Abs(ccy) > 1e-2f)
            { Debug.LogError($"FAIL fit centre: ({ccx},{ccy}) want (0,0)"); ok = false; }

            var (p0x, p0y) = fit.TileToLocal(bminX, bminY);
            var (p1x, p1y) = fit.TileToLocal(bmaxX, bmaxY);
            if (Mathf.Abs(p1x - p0x) > 800f || Mathf.Abs(p1y - p0y) > 400f)
            { Debug.LogError($"FAIL fit bounds: {Mathf.Abs(p1x - p0x)}x{Mathf.Abs(p1y - p0y)} exceeds 800x400"); ok = false; }

            // Fit must never divide by zero on a single-room (zero-span) level.
            var one = new DungeonLevel();
            one.Rooms.Add(new Room { Id = 1, Type = RoomType.Normal, X = 0.5f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            var fitOne = DungeonProjection.Fit(one, 800f, 400f, 0.5f);
            if (float.IsNaN(fitOne.PxPerTile) || float.IsInfinity(fitOne.PxPerTile) || fitOne.PxPerTile <= 0f)
            { Debug.LogError($"FAIL fit single-room: PxPerTile={fitOne.PxPerTile}"); ok = false; }

            // An empty level must not throw and must yield a usable projection.
            var empty = new DungeonLevel();
            var fitEmpty = DungeonProjection.Fit(empty, 800f, 400f, 1f);
            if (fitEmpty.PxPerTile <= 0f) { Debug.LogError("FAIL fit empty level"); ok = false; }

            Debug.Log(ok ? "PASS: DungeonProjection round-trip" : "FAIL: DungeonProjection round-trip");
        }
    }
}
