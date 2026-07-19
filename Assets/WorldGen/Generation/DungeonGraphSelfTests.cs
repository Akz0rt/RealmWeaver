using System.Collections.Generic;
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
            var lvl = new InteriorFloor();
            var r1 = DungeonOps.AddRoom(lvl, 0.1f, 0.1f);
            var r2 = DungeonOps.AddRoom(lvl, 0.2f, 0.2f);
            ok &= r1.Id == 1 && r2.Id == 2 && lvl.NextRoomId == 3 && lvl.Rooms.Count == 2;

            // AddCorridor: happy path, self-loop, duplicate, missing.
            ok &= DungeonOps.AddCorridor(lvl, 1, 2) == null && lvl.Links.Count == 1;
            ok &= DungeonOps.AddCorridor(lvl, 1, 1) != null;      // self
            ok &= DungeonOps.AddCorridor(lvl, 2, 1) != null;      // duplicate (order-independent)
            ok &= DungeonOps.AddCorridor(lvl, 1, 99) != null;     // missing
            ok &= lvl.Links.Count == 1;

            // Singleton conflict + SetRoomType demote.
            DungeonOps.SetRoomType(lvl, 1, 0);
            ok &= DungeonOps.FindSingletonConflict(lvl, 2, 0) == 1;
            DungeonOps.SetRoomType(lvl, 2, 0);    // should demote r1
            ok &= lvl.GetRoom(1).TypeId == 1 && lvl.GetRoom(2).TypeId == 0;
            ok &= DungeonOps.FindSingletonConflict(lvl, 2, 1) == 0;   // Normal is not singleton

            // RemoveRoom integrity: corridors + secrets (owned and cross-level targeting) vanish.
            var dungeon = new InteriorData();
            var l0 = new InteriorFloor(); var l1 = new InteriorFloor();
            dungeon.Floors.Add(l0); dungeon.Floors.Add(l1);
            var a = DungeonOps.AddRoom(l0, 0, 0); var b = DungeonOps.AddRoom(l0, 0, 0);
            var c = DungeonOps.AddRoom(l1, 0, 0);
            DungeonOps.AddCorridor(l0, a.Id, b.Id);
            b.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = a.Id });
            c.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = a.Id });
            DungeonOps.RemoveRoom(dungeon, 0, a.Id);
            ok &= l0.GetRoom(a.Id) == null && l0.Links.Count == 0;
            ok &= b.Portals.Count == 0 && c.Portals.Count == 0;   // both the owned and the cross-level target-secret removed

            Debug.Log(ok ? "Self-Test Dungeon Ops: PASS" : "Self-Test Dungeon Ops: FAIL");
        }

        [ContextMenu("Self-Test: Dungeon Validator")]
        public void SelfTestValidator()
        {
            bool ok = true;

            // Clean floor from the generator → no errors.
            var clean = new InteriorData { Floors = { DungeonGraphGenerator.Generate(5, 8) } };
            var cleanIssues = DungeonValidator.Validate(clean);
            ok &= cleanIssues.All(i => i.Severity != IssueSeverity.Error);

            // No entrance → error. Two entrances → error.
            var lvl = new InteriorFloor();
            var a = DungeonOps.AddRoom(lvl, 0, 0); var b = DungeonOps.AddRoom(lvl, 0, 0);
            var d0 = new InteriorData { Floors = { lvl } };
            ok &= DungeonValidator.Validate(d0).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("вход"));
            DungeonOps.SetRoomType(lvl, a.Id, 0);
            b.TypeId = 0;   // force a second entrance without demote
            ok &= DungeonValidator.Validate(d0).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("вход"));

            // Two bosses → error.
            var lvl2 = new InteriorFloor();
            var e = DungeonOps.AddRoom(lvl2, 0, 0); var f = DungeonOps.AddRoom(lvl2, 0, 0); var g = DungeonOps.AddRoom(lvl2, 0, 0);
            DungeonOps.SetRoomType(lvl2, e.Id, 0);
            f.TypeId = 2; g.TypeId = 2;
            var d1 = new InteriorData { Floors = { lvl2 } };
            ok &= DungeonValidator.Validate(d1).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("босс"));

            // Orphan warning: entrance + a disconnected room.
            var lvl3 = new InteriorFloor();
            var h = DungeonOps.AddRoom(lvl3, 0, 0); var iRoom = DungeonOps.AddRoom(lvl3, 0, 0);
            DungeonOps.SetRoomType(lvl3, h.Id, 0);   // iRoom left unconnected
            var d2 = new InteriorData { Floors = { lvl3 } };
            ok &= DungeonValidator.Validate(d2).Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("недостижим"));

            // Dangling secret target → error.
            var lvl4 = new InteriorFloor();
            var j = DungeonOps.AddRoom(lvl4, 0, 0);
            DungeonOps.SetRoomType(lvl4, j.Id, 0);
            j.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = 999 });
            var d3 = new InteriorData { Floors = { lvl4 } };
            ok &= DungeonValidator.Validate(d3).Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("секретный"));

            Debug.Log(ok ? "Self-Test Dungeon Validator: PASS" : "Self-Test Dungeon Validator: FAIL");
        }

        [ContextMenu("Self-Test: Dungeon Remove-Level Integrity")]
        public void SelfTestRemoveLevel()
        {
            bool ok = true;
            var d = new InteriorData();
            for (int i = 0; i < 3; i++) d.Floors.Add(new InteriorFloor());
            var r0 = DungeonOps.AddRoom(d.Floors[0], 0, 0);
            var r2 = DungeonOps.AddRoom(d.Floors[2], 0, 0);
            r0.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = r0.Id });
            r0.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 1, TargetRoomId = 5 });   // targets the removed level
            r0.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 2, TargetRoomId = r2.Id }); // level above → shifts to 1
            r0.Portals.Add(new Portal { Kind = PortalKind.DungeonExit });
            // Stairs are inter-floor too: a stair to the removed level must be dropped (no "лестница вникуда"),
            // and a stair to a level ABOVE must shift down — same integrity as secret doors.
            r0.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 1, TargetRoomId = 7 });   // to the removed level → removed
            r0.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 2, TargetRoomId = r2.Id }); // to the level above → shifts to 1

            DungeonOps.RemoveLevel(d, 1);

            ok &= d.Floors.Count == 2;
            ok &= r0.Portals.Count == 4;                                                                    // the two level-1 targets were removed
            ok &= r0.Portals.Exists(s => s.Kind == PortalKind.SecretDoor && s.TargetFloorIndex == 0 && s.TargetRoomId == r0.Id);   // unchanged
            ok &= r0.Portals.Exists(s => s.Kind == PortalKind.SecretDoor && s.TargetFloorIndex == 1 && s.TargetRoomId == r2.Id);   // was 2 → 1
            ok &= !r0.Portals.Exists(s => s.TargetRoomId == 5);                                             // secret to removed level: gone
            ok &= !r0.Portals.Exists(s => s.TargetRoomId == 7);                                             // stair to removed level: gone (no лестница вникуда)
            ok &= r0.Portals.Exists(s => s.Kind == PortalKind.Stairs && s.TargetFloorIndex == 1 && s.TargetRoomId == r2.Id);       // stair was 2 → 1
            ok &= r0.Portals.Exists(s => s.Kind == PortalKind.DungeonExit);                          // unchanged

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
            var lvl = new InteriorFloor();
            lvl.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.25f, Y = 0.25f, SizeW = 6, SizeH = 6 });
            lvl.Rooms.Add(new Room { Id = 2, TypeId = 2, X = 0.75f, Y = 0.75f, SizeW = 10, SizeH = 10 });
            var fit = DungeonProjection.Fit(lvl, 800f, 400f, 0.5f);
            var (bminX, bminY, bmaxX, bmaxY) = DungeonProjection.ContentBoundsTiles(lvl);
            var (ccx, ccy) = fit.TileToLocal((bminX + bmaxX) * 0.5f, (bminY + bmaxY) * 0.5f);
            if (Mathf.Abs(ccx) > 1e-2f || Mathf.Abs(ccy) > 1e-2f)
            { Debug.LogError($"FAIL fit centre: ({ccx},{ccy}) want (0,0)"); ok = false; }

            var (p0x, p0y) = fit.TileToLocal(bminX, bminY);
            var (p1x, p1y) = fit.TileToLocal(bmaxX, bmaxY);
            if (Mathf.Abs(p1x - p0x) > 800f || Mathf.Abs(p1y - p0y) > 400f)
            { Debug.LogError($"FAIL fit bounds: {Mathf.Abs(p1x - p0x)}x{Mathf.Abs(p1y - p0y)} exceeds 800x400"); ok = false; }

            // ...and the content actually FILLS the rect: a tiny constant scale would still centre and not overflow
            // (passing the checks above) yet render the map a speck. At least one axis must use a real fraction.
            float fillX = Mathf.Abs(p1x - p0x) / 800f, fillY = Mathf.Abs(p1y - p0y) / 400f;
            if (Mathf.Max(fillX, fillY) < 0.5f)
            { Debug.LogError($"FAIL fit scale: content fills only {fillX:F2}x{fillY:F2} of the rect (scale too small)"); ok = false; }

            // Fit must never divide by zero on a single-room (zero-span) level.
            var one = new InteriorFloor();
            one.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f, SizeW = 6, SizeH = 6 });
            var fitOne = DungeonProjection.Fit(one, 800f, 400f, 0.5f);
            if (float.IsNaN(fitOne.PxPerTile) || float.IsInfinity(fitOne.PxPerTile) || fitOne.PxPerTile <= 0f)
            { Debug.LogError($"FAIL fit single-room: PxPerTile={fitOne.PxPerTile}"); ok = false; }

            // An empty level must not throw and must yield a usable projection.
            var empty = new InteriorFloor();
            var fitEmpty = DungeonProjection.Fit(empty, 800f, 400f, 1f);
            if (fitEmpty.PxPerTile <= 0f) { Debug.LogError("FAIL fit empty level"); ok = false; }

            Debug.Log(ok ? "PASS: DungeonProjection round-trip" : "FAIL: DungeonProjection round-trip");
        }

        [ContextMenu("Self-test: generator compaction + room sizes")]
        public void SelfTestGeneratorCompaction()
        {
            bool ok = true;

            // New defaults (spec R7). t is Room.TypeId: Entrance=0, Boss=2, Normal=1.
            var checks = new (int t, int w, int h)[]
            {
                (0, 7, 5), (2, 10, 10), (1, 6, 6),
            };
            foreach (var c in checks)
            {
                var (w, h) = RoomSizing.Default(c.t);
                if (w != c.w || h != c.h) { Debug.LogError($"FAIL default {c.t}: {w}x{h} want {c.w}x{c.h}"); ok = false; }
            }
            if (RoomSizing.MaxSide != 16) { Debug.LogError($"FAIL MaxSide={RoomSizing.MaxSide} want 16"); ok = false; }
            if (RoomSizing.Clamp(20) != 16 || RoomSizing.Clamp(0) != 1)
            { Debug.LogError("FAIL Clamp bounds"); ok = false; }

            // The generator now runs EnforceCorridorLeash itself (Generate step 8), so the corridor bound is
            // guaranteed BY CONSTRUCTION — assert the real constant instead of a threshold derived by hand from
            // the layout's geometry. The previous 9 was derived from the row-height slack alone and ignored the
            // horizontal spread entirely, so it was wrong in a direction no one had measured.
            //
            // MinGap is the load-bearing assertion: a NEGATIVE gap means Separate did not converge and rooms are
            // still overlapping. An upper-bound-only check reads that stacked-rooms failure as PASS.
            float MaxGap = DungeonLayout.MaxCorridorTiles + 1f;   // +1 slack for the post-Separate shove margin
            const float MinGap = -0.5f;
            foreach (int roomCount in new[] { 6, 8 })   // 6 = what DungeonEditorScreen actually generates; 8 = deeper stress
                foreach (int seed in new[] { 1, 7, 42, 1337, 90210 })
                {
                    var lvl = DungeonGraphGenerator.Generate(seed, roomCount, 3);
                    DungeonLayout.Separate(lvl);

                    // The whole layout must fit the field — Clamp01 pinning a footprint to the edge IS the
                    // overflow failure this bound exists to catch.
                    foreach (var r in lvl.Rooms)
                    {
                        var (rw, rh) = DungeonProjection.EffectiveSize(r);
                        float cx = r.X * DungeonLayout.TilesPerAxis, cy = r.Y * DungeonLayout.TilesPerAxis;
                        if (cx - rw * 0.5f < -0.01f || cx + rw * 0.5f > DungeonLayout.TilesPerAxis + 0.01f ||
                            cy - rh * 0.5f < -0.01f || cy + rh * 0.5f > DungeonLayout.TilesPerAxis + 0.01f)
                        {
                            Debug.LogError($"FAIL seed={seed} n={roomCount} room {r.Id} ({rw}x{rh}) overflows the {DungeonLayout.TilesPerAxis}-tile field at ({cx:F1},{cy:F1})");
                            ok = false;
                        }
                    }

                    foreach (var c in lvl.Links)
                    {
                        var a = lvl.GetRoom(c.RoomA); var b = lvl.GetRoom(c.RoomB);
                        if (a == null || b == null) continue;
                        float gap = EdgeGapTiles(a, b);
                        if (gap > MaxGap)
                        {
                            Debug.LogError($"FAIL seed={seed} n={roomCount} corridor {c.RoomA}-{c.RoomB}: edge gap {gap:F1} tiles > {MaxGap}");
                            ok = false;
                        }
                        if (gap < MinGap)
                        {
                            Debug.LogError($"FAIL seed={seed} n={roomCount} corridor {c.RoomA}-{c.RoomB}: rooms OVERLAP by {-gap:F1} tiles — Separate did not converge");
                            ok = false;
                        }
                    }
                }

            Debug.Log(ok ? "PASS: generator compaction + room sizes" : "FAIL: generator compaction + room sizes");
        }

        /// <summary>Edge-to-edge emptiness between two room footprints, in tiles: centre distance minus both
        /// half-extents, taken on the axis of greatest centre separation (the axis the corridor mostly runs
        /// along). Negative means the footprints overlap.
        ///
        /// This is a Chebyshev-style measure, not the true Euclidean corner distance — for two rooms separated
        /// diagonally it can understate the visual gap by up to ~√2. That is deliberate: it matches the axis the
        /// cascade's own AABB separation works on, so the test measures the same thing the layout controls.</summary>
        static float EdgeGapTiles(Room a, Room b)
        {
            float ax = a.X * DungeonLayout.TilesPerAxis, ay = a.Y * DungeonLayout.TilesPerAxis;
            float bx = b.X * DungeonLayout.TilesPerAxis, by = b.Y * DungeonLayout.TilesPerAxis;
            var (aw, ah) = DungeonProjection.EffectiveSize(a);
            var (bw, bh) = DungeonProjection.EffectiveSize(b);
            float dx = Mathf.Abs(bx - ax) - (aw + bw) * 0.5f;
            float dy = Mathf.Abs(by - ay) - (ah + bh) * 0.5f;
            return Mathf.Max(dx, dy);
        }

        [ContextMenu("Self-test: random room sizes")]
        public void SelfTestRandomRoomSizes()
        {
            bool ok = true;

            // Ranges must sit inside RoomSizing's own clamp, or Roll would emit sizes Clamp then silently rewrites.
            // t is Room.TypeId: Entrance=0, Boss=2, Normal=1.
            foreach (int t in new[] { 0, 2, 1 })
            {
                var (min, max) = RoomSizing.Range(t);
                if (min < RoomSizing.MinSide || max > RoomSizing.MaxSide || min > max)
                { Debug.LogError($"FAIL range {t}: {min}..{max} outside {RoomSizing.MinSide}..{RoomSizing.MaxSide}"); ok = false; }

                // The manual/migration default must be a legal roll too, or a hand-added room would look like
                // nothing the generator can produce.
                var (dw, dh) = RoomSizing.Default(t);
                if (dw < min || dw > max || dh < min || dh > max)
                { Debug.LogError($"FAIL default {t} {dw}x{dh} outside its own range {min}..{max}"); ok = false; }
            }

            // Roll must stay in range, and must actually vary (a constant "roll" would pass a bounds-only check).
            var rng = new System.Random(12345);
            foreach (int t in new[] { 0, 2, 1 })
            {
                var (min, max) = RoomSizing.Range(t);
                var seen = new HashSet<int>();
                for (int i = 0; i < 200; i++)
                {
                    var (w, h) = RoomSizing.Roll(t, rng);
                    if (w < min || w > max || h < min || h > max)
                    { Debug.LogError($"FAIL roll {t}: {w}x{h} outside {min}..{max}"); ok = false; break; }
                    seen.Add(w); seen.Add(h);
                }
                if (max > min && seen.Count < 2)
                { Debug.LogError($"FAIL roll {t}: never varied across 200 rolls"); ok = false; }
            }

            // Determinism: the SAME seed must still produce byte-identical floors. This is what a UnityEngine.Random
            // roll would have broken, silently and only sometimes.
            var a = DungeonGraphGenerator.Generate(4242, 6, 3);
            var b = DungeonGraphGenerator.Generate(4242, 6, 3);
            if (a.Rooms.Count != b.Rooms.Count) { Debug.LogError("FAIL determinism: room count"); ok = false; }
            else
                for (int i = 0; i < a.Rooms.Count; i++)
                {
                    var ra = a.Rooms[i]; var rb = b.Rooms[i];
                    if (ra.SizeW != rb.SizeW || ra.SizeH != rb.SizeH || ra.TypeId != rb.TypeId ||
                        Mathf.Abs(ra.X - rb.X) > 1e-6f || Mathf.Abs(ra.Y - rb.Y) > 1e-6f)
                    { Debug.LogError($"FAIL determinism at room {ra.Id}: {ra.SizeW}x{ra.SizeH} vs {rb.SizeW}x{rb.SizeH}"); ok = false; break; }
                }

            // Two DIFFERENT seeds must differ in at least one size — proves sizes are seed-driven, not fixed.
            var c = DungeonGraphGenerator.Generate(777, 6, 3);
            bool anyDiff = false;
            for (int i = 0; i < Mathf.Min(a.Rooms.Count, c.Rooms.Count); i++)
                if (a.Rooms[i].SizeW != c.Rooms[i].SizeW || a.Rooms[i].SizeH != c.Rooms[i].SizeH) { anyDiff = true; break; }
            if (!anyDiff) { Debug.LogError("FAIL: two different seeds produced identical sizes"); ok = false; }

            Debug.Log(ok ? "PASS: random room sizes" : "FAIL: random room sizes");
        }
    }
}
