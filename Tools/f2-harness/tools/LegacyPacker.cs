using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Verbatim copy of the PRE-FIX PackAroundColumnWithinFootprint (git HEAD~ CompactLayout.cs) plus a
    /// PHASE-1-ONLY variant (d == 0), for A/B comparison in the scratch harness only.</summary>
    public static class LegacyPacker
    {
        const float OverlapEps = 1e-3f;
        static float ToTile(float norm) => norm * DungeonLayout.TilesPerAxis;
        static float ToNorm(float tile) => tile / DungeonLayout.TilesPerAxis;
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static int Pack(InteriorFloor floor, int columnRoomId, float colXTiles, float colYTiles,
            InteriorFloor contourFloor, float margin, bool flushOnly)
        {
            if (floor == null || floor.Rooms.Count == 0) return 0;
            var column = floor.GetRoom(columnRoomId);
            if (column == null || contourFloor == null) return floor.Rooms.Count;

            column.X = Clamp01(ToNorm(colXTiles));
            column.Y = Clamp01(ToNorm(colYTiles));

            var placed = new List<Room> { column };
            var placedIds = new HashSet<int> { column.Id };
            var adj = BuildAdjacency(floor);
            var queue = new Queue<int>();
            queue.Enqueue(column.Id);
            while (queue.Count > 0)
            {
                var cur = floor.GetRoom(queue.Dequeue());
                if (cur == null || !adj.TryGetValue(cur.Id, out var nbs)) continue;
                foreach (int nb in nbs)
                {
                    if (placedIds.Contains(nb)) continue;
                    var child = floor.GetRoom(nb);
                    if (child == null) continue;
                    if (TryPlaceAgainstInFootprint(child, cur, placed, contourFloor, margin, flushOnly))
                    {
                        placed.Add(child);
                        placedIds.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
            }

            floor.Rooms.RemoveAll(r => !placedIds.Contains(r.Id));
            floor.Links.RemoveAll(l => !placedIds.Contains(l.RoomA) || !placedIds.Contains(l.RoomB));
            return placed.Count;
        }

        static bool TryPlaceAgainstInFootprint(Room child, Room parent, List<Room> placed,
            InteriorFloor contourFloor, float margin, bool flushOnly)
        {
            var (cw, ch) = DungeonProjection.EffectiveSize(child);
            var (pw, ph) = DungeonProjection.EffectiveSize(parent);
            float px = ToTile(parent.X), py = ToTile(parent.Y);
            float offX = (pw + cw) * 0.5f, offY = (ph + ch) * 0.5f;
            int max = flushOnly ? 0 : DungeonLayout.TilesPerAxis;
            for (int d = 0; d <= max; d++)
                for (int s = 0; s < 4; s++)
                {
                    float cx, cy;
                    switch (s)
                    {
                        case 0: cx = px + offX + d; cy = py; break;
                        case 1: cx = px; cy = py + offY + d; break;
                        case 2: cx = px - offX - d; cy = py; break;
                        default: cx = px; cy = py - offY - d; break;
                    }
                    if (IsFree(cx, cy, cw, ch, placed)
                        && FloorFootprint.ContainsRect(contourFloor, margin, cx, cy, cw, ch))
                    {
                        child.X = Clamp01(ToNorm(cx));
                        child.Y = Clamp01(ToNorm(cy));
                        return true;
                    }
                }
            return false;
        }

        static bool IsFree(float cx, float cy, float cw, float ch, List<Room> placed)
        {
            foreach (var r in placed)
            {
                var (rw, rh) = DungeonProjection.EffectiveSize(r);
                float dx = System.Math.Abs(cx - ToTile(r.X)) - (cw + rw) * 0.5f;
                float dy = System.Math.Abs(cy - ToTile(r.Y)) - (ch + rh) * 0.5f;
                if (dx < -OverlapEps && dy < -OverlapEps) return false;
            }
            return true;
        }

        static Dictionary<int, List<int>> BuildAdjacency(InteriorFloor f)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var r in f.Rooms) if (!adj.ContainsKey(r.Id)) adj[r.Id] = new List<int>();
            foreach (var l in f.Links)
            {
                if (l.RoomA == l.RoomB) continue;
                if (adj.ContainsKey(l.RoomA) && adj.ContainsKey(l.RoomB))
                {
                    if (!adj[l.RoomA].Contains(l.RoomB)) adj[l.RoomA].Add(l.RoomB);
                    if (!adj[l.RoomB].Contains(l.RoomA)) adj[l.RoomB].Add(l.RoomA);
                }
            }
            foreach (var kv in adj) kv.Value.Sort();
            return adj;
        }
    }
}
