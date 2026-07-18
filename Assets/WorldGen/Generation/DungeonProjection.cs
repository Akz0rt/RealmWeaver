namespace WorldGen.Generation
{
    /// <summary>
    /// 2D screen-space projection shared by BOTH dungeon views (sub-project 3 revision). Pure + headless:
    /// no UnityEngine types, so it self-tests without a scene.
    ///
    /// The Граф and Изо views differ by exactly ONE number — SquashY (1.0 = top-down/flat,
    /// 0.5 = oblique/"iso"). There is deliberately NO 45° rotation: the revision locked "the iso view
    /// preserves the arrangement the DM drew in the graph" (spec R1/R2), which is why the old
    /// IsoProjection (sx=(x−y)·Tw/2, sy=(x+y)·Th/2) was deleted rather than parameterized.
    ///
    /// Input is TILE space (Room.X/Y × DungeonLayout.TilesPerAxis). Output is LOCAL pixels relative to the
    /// host rect's CENTRE — i.e. anchoredPosition space for a stretched, 0.5-pivoted layer, matching the
    /// old DungeonGraphView.PointCenter convention.
    ///
    /// Y IS INVERTED HERE, in exactly one place: tile Y grows DOWN (south/deeper), UI local Y grows UP.
    /// This is the single inversion point for both renderers — do not re-invert downstream.
    ///
    /// Because BOTH position and size flow through this struct, they cannot drift into different scales
    /// (the old flat view sized cards at a fixed 14px/tile while positioning them at ~29px/tile — spec B2)
    /// and a tile is the same pixel count on both axes (spec B3).
    /// </summary>
    public struct DungeonProjection
    {
        public float PxPerTile;
        public float SquashY;     // 1.0 = Граф (top-down); 0.5 = Изо (oblique)
        public float PanX, PanY;  // local-pixel offset applied AFTER scaling (Task 5 pan/zoom mutates these)

        /// <summary>Leaves a margin around the fitted content so nothing kisses the host rect's edge.</summary>
        public const float FitPadding = 0.9f;

        /// <summary>Floor for a content span (tiles). Guards Fit() against a zero/near-zero span (a
        /// single-room level, or every room stacked) producing an infinite PxPerTile.</summary>
        public const float MinSpanTiles = 8f;

        public (float lx, float ly) TileToLocal(float tx, float ty)
            => (tx * PxPerTile + PanX, -(ty * PxPerTile * SquashY) + PanY);

        /// <summary>Exact inverse of TileToLocal. Returns (0,0) rather than NaN/∞ on a degenerate
        /// projection (PxPerTile or SquashY <= 0) — a caller mid-first-frame must not poison Room.X/Y.</summary>
        public (float tx, float ty) LocalToTile(float lx, float ly)
        {
            if (PxPerTile <= 0f || SquashY <= 0f) return (0f, 0f);
            return ((lx - PanX) / PxPerTile, -(ly - PanY) / (PxPerTile * SquashY));
        }

        /// <summary>Tile-space AABB over every room FOOTPRINT (not just centres) on the level. Returns a
        /// centred MinSpanTiles-sized box for an empty level so Fit() stays well-defined.</summary>
        public static (float minX, float minY, float maxX, float maxY) ContentBoundsTiles(InteriorFloor lvl)
        {
            if (lvl == null || lvl.Rooms.Count == 0)
            {
                float c = DungeonLayout.TilesPerAxis * 0.5f, h = MinSpanTiles * 0.5f;
                return (c - h, c - h, c + h, c + h);
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var r in lvl.Rooms)
            {
                float cx = r.X * DungeonLayout.TilesPerAxis;
                float cy = r.Y * DungeonLayout.TilesPerAxis;
                var (w, h) = EffectiveSize(r);
                float hw = w * 0.5f, hh = h * 0.5f;
                if (cx - hw < minX) minX = cx - hw;
                if (cx + hw > maxX) maxX = cx + hw;
                if (cy - hh < minY) minY = cy - hh;
                if (cy + hh > maxY) maxY = cy + hh;
            }
            return (minX, minY, maxX, maxY);
        }

        /// <summary>Resolve PxPerTile + Pan so the level's occupied bounds fill the host rect (uniform
        /// scale, centred, FitPadding margin). Call ONCE per level bind — NEVER during drag/cascade, or
        /// the map rescales under the cursor (spec R6). Safe with rectW/rectH == 0 (returns PxPerTile=1);
        /// the caller defers to the first valid-rect frame anyway (rect gotcha).</summary>
        public static DungeonProjection Fit(InteriorFloor lvl, float rectW, float rectH, float squashY)
        {
            if (squashY <= 0f) squashY = 1f;

            var (minX, minY, maxX, maxY) = ContentBoundsTiles(lvl);
            float spanX = Max(maxX - minX, MinSpanTiles);
            float spanY = Max(maxY - minY, MinSpanTiles);

            float px;
            if (rectW <= 0f || rectH <= 0f) px = 1f;   // not laid out yet — caller retries; never divide by 0
            else px = Min(rectW / spanX, rectH / (spanY * squashY)) * FitPadding;
            if (px <= 0f) px = 1f;

            // Centre the content: the bounds centre must project to local (0,0).
            float ctx = (minX + maxX) * 0.5f;
            float cty = (minY + maxY) * 0.5f;
            return new DungeonProjection
            {
                PxPerTile = px,
                SquashY = squashY,
                PanX = -ctx * px,
                PanY = cty * px * squashY,
            };
        }

        /// <summary>Room footprint in tiles, with the same &lt;=0 fallback + clamp RoomSizing.ApplyDefaults
        /// applies — serialized data can predate sizing or drift out of range.</summary>
        public static (int w, int h) EffectiveSize(Room r)
        {
            int w = r.SizeW, h = r.SizeH;
            if (w <= 0 || h <= 0)
            {
                var (dw, dh) = RoomSizing.Default(r.TypeId);
                if (w <= 0) w = dw;
                if (h <= 0) h = dh;
            }
            return (RoomSizing.Clamp(w), RoomSizing.Clamp(h));
        }

        /// <summary>True if tile-space point (tx,ty) is inside room r's footprint. The controller's hit-test
        /// (spec: hit-testing is renderer-agnostic because it happens in TILE space).</summary>
        public static bool HitTest(Room r, float tx, float ty)
        {
            float cx = r.X * DungeonLayout.TilesPerAxis;
            float cy = r.Y * DungeonLayout.TilesPerAxis;
            var (w, h) = EffectiveSize(r);
            return tx >= cx - w * 0.5f && tx <= cx + w * 0.5f
                && ty >= cy - h * 0.5f && ty <= cy + h * 0.5f;
        }

        static float Min(float a, float b) => a < b ? a : b;
        static float Max(float a, float b) => a > b ? a : b;
    }
}
