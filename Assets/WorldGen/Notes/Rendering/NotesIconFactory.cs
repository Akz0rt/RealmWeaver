using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Generates a 32x32 flat glyph sprite per NotesTool at first request and caches it,
    /// same runtime-drawn-texture technique as PoiPlaceholderFactory. No external assets.
    /// </summary>
    public static class NotesIconFactory
    {
        static readonly Dictionary<NotesTool, Sprite> cache = new Dictionary<NotesTool, Sprite>();

        public static Sprite GetIcon(NotesTool tool)
        {
            if (cache.TryGetValue(tool, out var cached)) return cached;
            var sprite = Build(tool);
            cache[tool] = sprite;
            return sprite;
        }

        static Sprite Build(NotesTool tool)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.name = $"NotesIcon_{tool}";

            var transparent = new Color32(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, transparent);

            switch (tool)
            {
                case NotesTool.Select: DrawCursor(tex, size); break;
                case NotesTool.Note: DrawNote(tex, size); break;
                case NotesTool.Link: DrawLink(tex, size); break;
                case NotesTool.Drawing: DrawPencil(tex, size); break;
                case NotesTool.Image: DrawPicture(tex, size); break;
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static void DrawCursor(Texture2D tex, int size)
        {
            var top = new Vector2(size * 0.22f, size * 0.85f);
            var tip = new Vector2(size * 0.22f, size * 0.15f);
            var side = new Vector2(size * 0.78f, size * 0.62f);
            FillTriangle(tex, size, top, tip, side, Color.white);
            var notchA = new Vector2(size * 0.42f, size * 0.62f);
            var notchB = new Vector2(size * 0.6f, size * 0.85f);
            DrawLine(tex, size, notchA, notchB, 2.5f, Color.white);
        }

        static void DrawNote(Texture2D tex, int size)
        {
            float m = size * 0.2f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            float fold = (max.x - min.x) * 0.35f;
            DrawLine(tex, size, new Vector2(max.x - fold, max.y), new Vector2(max.x, max.y - fold), 2f, Color.white);
        }

        static void DrawLink(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.2f, size * 0.2f);
            var to = new Vector2(size * 0.75f, size * 0.75f);
            DrawLine(tex, size, from, to, 2.5f, Color.white);
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.08f);
            Vector2 left = to - dir * (size * 0.06f) + perp * (size * 0.08f);
            Vector2 right = to - dir * (size * 0.06f) - perp * (size * 0.08f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }

        static void DrawPencil(Texture2D tex, int size)
        {
            var from = new Vector2(size * 0.25f, size * 0.8f);
            var to = new Vector2(size * 0.75f, size * 0.3f);
            DrawLine(tex, size, from, to, 4f, Color.white);
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip = to + dir * (size * 0.12f);
            Vector2 left = to + perp * (size * 0.05f);
            Vector2 right = to - perp * (size * 0.05f);
            FillTriangle(tex, size, tip, left, right, Color.white);
        }

        static void DrawPicture(Texture2D tex, int size)
        {
            float m = size * 0.18f;
            var min = new Vector2(m, m);
            var max = new Vector2(size - m, size - m);
            DrawRectOutline(tex, size, min, max, 2f, Color.white);
            FillCircle(tex, size, new Vector2(min.x + (max.x - min.x) * 0.3f, max.y - (max.y - min.y) * 0.28f), size * 0.07f, Color.white);
            var peak = new Vector2(min.x + (max.x - min.x) * 0.6f, min.y + (max.y - min.y) * 0.25f);
            var baseL = new Vector2(min.x + 2f, max.y - 2f);
            var baseR = new Vector2(max.x - 2f, max.y - 2f);
            FillTriangle(tex, size, peak, baseL, baseR, Color.white);
        }

        static void FillTriangle(Texture2D tex, int size, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (PointInTriangle(p, a, b, c))
                        tex.SetPixel(x, y, color);
                }
        }

        static void DrawLine(Texture2D tex, int size, Vector2 a, Vector2 b, float width, Color color)
        {
            float halfWidth = width * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (PointNearSegment(p, a, b, halfWidth))
                        tex.SetPixel(x, y, color);
                }
        }

        static void DrawRectOutline(Texture2D tex, int size, Vector2 min, Vector2 max, float thickness, Color color)
        {
            DrawLine(tex, size, new Vector2(min.x, min.y), new Vector2(max.x, min.y), thickness, color);
            DrawLine(tex, size, new Vector2(max.x, min.y), new Vector2(max.x, max.y), thickness, color);
            DrawLine(tex, size, new Vector2(max.x, max.y), new Vector2(min.x, max.y), thickness, color);
            DrawLine(tex, size, new Vector2(min.x, max.y), new Vector2(min.x, min.y), thickness, color);
        }

        static void FillCircle(Texture2D tex, int size, Vector2 center, float radius, Color color)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if ((p - center).sqrMagnitude <= radius * radius)
                        tex.SetPixel(x, y, color);
                }
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointNearSegment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 closest = a + ab * t;
            return (p - closest).magnitude <= halfWidth;
        }
    }
}
