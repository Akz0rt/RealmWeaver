namespace WorldGen.Generation
{
    /// <summary>An integer cell coordinate in a GridBuffer.</summary>
    public struct GridPoint { public int X, Y; }

    /// <summary>Three responsibilities around a room's battle map: (1) build the starting grid — pure and
    /// deterministic, so an untouched grid need not be persisted at all (Room.Grid stays null until the DM
    /// edits it) and still looks identical in the next session; (2) project the doors a room's LINKS imply
    /// onto that grid's walls, derived on demand and never written into the buffer; (3) count how many
    /// rooms on a floor carry a saved (DM-authored) battle map, for the confirm dialogs that would destroy
    /// them.</summary>
    public static class BattleGridGenerator
    {
        /// <summary>The grid size a room's CURRENT footprint calls for: the footprint plus a one-cell wall
        /// ring on each side, clamped. The ring is added OUTSIDE the contour rather than carved out of it —
        /// carving would leave a 4x4 room with 2x2 of usable floor.</summary>
        public static (int w, int h) NaturalSize(Room room)
        {
            var (fw, fh) = DungeonProjection.EffectiveSize(room);
            return (BattleGridCodec.Clamp(fw + 2), BattleGridCodec.Clamp(fh + 2));
        }

        /// <summary>How many rooms on this floor carry a saved battle map. Used by the confirm dialogs
        /// of the two irreversible floor operations so the DM is told what a regenerate or a floor
        /// removal actually costs.</summary>
        public static int CountGrids(InteriorFloor floor)
        {
            if (floor == null) return 0;
            int n = 0;
            foreach (var r in floor.Rooms) if (r.Grid != null) n++;
            return n;
        }

        public static GridBuffer Generate(Room room)
        {
            var (w, h) = NaturalSize(room);
            var buf = new GridBuffer(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    buf.Set(x, y, (x == 0 || y == 0 || x == w - 1 || y == h - 1) ? GridCell.Wall : GridCell.Floor);
            return buf;
        }

        /// <summary>How close (in tiles) a door point must be to a wall line to count as sitting on it.
        /// RoomLinkGeometry puts doors exactly on the wall; this only absorbs float drift.</summary>
        public const float DoorAttachEps = 0.1f;

        /// <summary>Doors the room's LINKS imply, as grid cells. DERIVED — never written into the buffer,
        /// which is what makes "doors follow the schematic automatically" safe: a layer that does not
        /// write cannot overwrite the DM's painting. Hand-painted doors are ordinary GridCell.Door cells
        /// and are untouched by this.</summary>
        public static System.Collections.Generic.List<GridPoint> ProjectDoors(InteriorFloor floor, Room room, GridBuffer buf)
        {
            var result = new System.Collections.Generic.List<GridPoint>();
            if (floor == null || room == null || buf == null) return result;
            if (buf.Width < 3 || buf.Height < 3) return result;

            var (fw, fh) = DungeonProjection.EffectiveSize(room);
            float cx = room.X * DungeonLayout.TilesPerAxis;
            float cy = room.Y * DungeonLayout.TilesPerAxis;
            float left = cx - fw * 0.5f, right = cx + fw * 0.5f;
            float bottom = cy - fh * 0.5f, top = cy + fh * 0.5f;

            var graph = DungeonLayout.BuildRenderGraph(floor, RoomLinkGeometry.RoutingMode.Clean);
            var seen = new System.Collections.Generic.HashSet<int>();

            foreach (var d in graph.Doors)
            {
                float dx = d.X * DungeonLayout.TilesPerAxis;
                float dy = d.Y * DungeonLayout.TilesPerAxis;

                bool onVerticalSpan   = dy >= bottom - DoorAttachEps && dy <= top + DoorAttachEps;
                bool onHorizontalSpan = dx >= left - DoorAttachEps && dx <= right + DoorAttachEps;

                GridPoint p;
                if (onVerticalSpan && Abs(dx - left) <= DoorAttachEps)
                    p = new GridPoint { X = 0, Y = AlongVertical(dy, bottom, top, buf) };
                else if (onVerticalSpan && Abs(dx - right) <= DoorAttachEps)
                    p = new GridPoint { X = buf.Width - 1, Y = AlongVertical(dy, bottom, top, buf) };
                else if (onHorizontalSpan && Abs(dy - top) <= DoorAttachEps)
                    p = new GridPoint { X = AlongHorizontal(dx, left, right, buf), Y = 0 };
                else if (onHorizontalSpan && Abs(dy - bottom) <= DoorAttachEps)
                    p = new GridPoint { X = AlongHorizontal(dx, left, right, buf), Y = buf.Height - 1 };
                else
                    continue;                                   // belongs to some other room's wall

                if (seen.Add(p.Y * buf.Width + p.X)) result.Add(p);
            }
            return result;
        }

        /// <summary>Map one arbitrary tile-space point onto the room's grid frame. Exposed for the
        /// self-tests, which pin the Y flip and the 1:1-vs-proportional behaviour without depending on
        /// which wall the router picked.</summary>
        public static GridPoint ProjectDoorPoint(Room room, GridBuffer buf, float tileX, float tileY)
        {
            var (fw, fh) = DungeonProjection.EffectiveSize(room);
            float cx = room.X * DungeonLayout.TilesPerAxis;
            float cy = room.Y * DungeonLayout.TilesPerAxis;
            return new GridPoint
            {
                X = AlongHorizontal(tileX, cx - fw * 0.5f, cx + fw * 0.5f, buf),
                Y = AlongVertical(tileY, cy - fh * 0.5f, cy + fh * 0.5f, buf),
            };
        }

        // Tile Y grows DOWN the screen (DungeonProjection.TileToLocal negates ty) while grid Y grows UP,
        // so this measures DOWN from `top`. Reverse it and every door lands on the opposite wall.
        static int AlongVertical(float ty, float bottom, float top, GridBuffer buf)
        {
            int inner = buf.Height - 2;
            float span = top - bottom;
            float f = span <= 0f ? 0f : (top - ty) / span * inner;
            return ClampInt(1 + (int)System.Math.Floor(f), 1, inner);
        }

        static int AlongHorizontal(float tx, float left, float right, GridBuffer buf)
        {
            int inner = buf.Width - 2;
            float span = right - left;
            float f = span <= 0f ? 0f : (tx - left) / span * inner;
            return ClampInt(1 + (int)System.Math.Floor(f), 1, inner);
        }

        static int ClampInt(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        static float Abs(float v) => v < 0f ? -v : v;
    }
}
