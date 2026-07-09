using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Builds a 128x128 "medallion" sprite per PoiType (dark disc + glow + accent ring +
    /// procedural stone-tone icon) and caches one shared instance per type — reused by on-map
    /// markers, the POI list rows, and the edit-panel type buttons. Icons are procedural placeholders;
    /// a per-POI CustomIconBytes still overrides this (see PoiData/PoiMarkerView).</summary>
    public static class PoiPlaceholderFactory
    {
        const int S = 128;
        static readonly Dictionary<PoiType, Sprite> cache = new Dictionary<PoiType, Sprite>();

        // Medallion + icon palette (fixed, theme-independent).
        static readonly Color32 DiscC = new Color32(0x14, 0x1c, 0x25, 255); // disc center
        static readonly Color32 DiscE = new Color32(0x08, 0x0d, 0x14, 255); // disc edge
        static readonly Color32 Rim   = new Color32(0x0a, 0x0d, 0x12, 255);
        static readonly Color32 Acc   = new Color32(0xe6, 0xb2, 0x5c, 255);
        static readonly Color32 Dark  = new Color32(0x2b, 0x32, 0x3d, 255);
        static readonly Color32 Light = new Color32(0x41, 0x4c, 0x5b, 255);
        static readonly Color32 Black = new Color32(0x0a, 0x0d, 0x12, 255);
        static readonly Color32 Steel = new Color32(0xc9, 0xd2, 0xdc, 255);
        static readonly Color32 Wood  = new Color32(0x4a, 0x3a, 0x28, 255);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public static Sprite GetPlaceholder(PoiType type)
        {
            if (cache.TryGetValue(type, out var s)) return s;
            s = Build(type);
            cache[type] = s;
            return s;
        }

        static Sprite Build(PoiType type)
        {
            var buf = new Color32[S * S];
            DrawFrame(buf);
            DrawIcon(buf, type);

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = $"PoiMedallion_{type}" };
            tex.SetPixels32(buf);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        }

        // ---- buffer helpers (y-up: buf[y*S + x]) ----
        static void Px(Color32[] b, int x, int y, Color32 c)
        { if ((uint)x < S && (uint)y < S) b[y * S + x] = c; }

        static void FillRect(Color32[] b, int x0, int y0, int x1, int y1, Color32 c)
        { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Px(b, x, y, c); }

        static void HLine(Color32[] b, int x0, int x1, int y, Color32 c)
        { for (int x = x0; x <= x1; x++) Px(b, x, y, c); }

        static void VLine(Color32[] b, int x, int y0, int y1, Color32 c)
        { for (int y = y0; y <= y1; y++) Px(b, x, y, c); }

        static void Disc(Color32[] b, float cx, float cy, float r, Color32 c)
        {
            int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
            int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            { float dx = x - cx, dy = y - cy; if (dx * dx + dy * dy <= r * r) Px(b, x, y, c); }
        }

        // Isoceles triangle pointing up: apex at (cx, apexY), base half-width halfW at baseY (baseY<apexY).
        static void TriUp(Color32[] b, float cx, int baseY, int apexY, float halfW, Color32 c)
        {
            for (int y = baseY; y <= apexY; y++)
            {
                float t = (y - baseY) / (float)(apexY - baseY); // 0 at base, 1 at apex
                int hw = Mathf.RoundToInt(halfW * (1f - t));
                HLine(b, Mathf.RoundToInt(cx) - hw, Mathf.RoundToInt(cx) + hw, y, c);
            }
        }

        static void DrawFrame(Color32[] b)
        {
            float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f;
            float R = S * 0.5f;
            float rDisc = R - 2f;              // disc outer
            float rimW = 5f, accW = 3f;        // 2.6/1.6 * (S/64=2)
            float rAccOut = rDisc - rimW;      // accent ring outer
            float rAccIn = rAccOut - accW;     // disc interior starts here
            float rGlow = R;                   // soft glow reaches the texture edge
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rDisc)
                {
                    // soft dark outer glow, alpha fades out past the disc
                    float t = Mathf.InverseLerp(rGlow, rDisc, d); // 0 at edge → 1 at disc
                    byte a = (byte)Mathf.Clamp(t * 90f, 0, 90);
                    if (a > 0) b[y * S + x] = new Color32(0x0a, 0x0d, 0x14, a);
                }
                else if (d > rAccOut) b[y * S + x] = Rim;
                else if (d > rAccIn) b[y * S + x] = Acc;
                else
                {
                    float t = Mathf.Clamp01(d / rAccIn);        // 0 center → 1 inner edge
                    b[y * S + x] = Color32.Lerp(DiscC, DiscE, t);
                }
            }
        }

        static void DrawIcon(Color32[] b, PoiType type)
        {
            switch (type)
            {
                case PoiType.City:      City(b); break;
                case PoiType.Fortress:  Fortress(b); break;
                case PoiType.Village:   Village(b); break;
                case PoiType.Tower:     Tower(b); break;
                case PoiType.Temple:    Temple(b); break;
                case PoiType.Ruin:      Ruin(b); break;
                case PoiType.Dungeon:   Dungeon(b); break;
                case PoiType.Encounter: Encounter(b); break;
                case PoiType.Camp:      Camp(b); break;
                case PoiType.Port:      Port(b); break;
                default:                Unknown(b); break;
            }
        }

        const int C = S / 2; // center

        // Crenellated block with `teeth` merlons across the top. Returns nothing; fills [x0..x1]x[y0..topWithTeeth].
        static void Keep(Color32[] b, int x0, int x1, int y0, int bodyTop, int teeth, Color32 body, Color32 top)
        {
            FillRect(b, x0, y0, x1, bodyTop, body);
            int w = x1 - x0 + 1, step = Mathf.Max(2, w / (teeth * 2 - 1));
            for (int i = 0; i < teeth; i++)
            { int mx = x0 + i * step * 2; FillRect(b, mx, bodyTop + 1, Mathf.Min(mx + step - 1, x1), bodyTop + step, top); }
        }

        static void Flag(Color32[] b, int poleX, int topY, int h) // pole + accent pennant
        { VLine(b, poleX, topY - h, topY, Steel); FillRect(b, poleX + 1, topY - h, poleX + 8, topY - h + 5, Acc); }

        static void Unknown(Color32[] b) // accent "?"
        {
            HLine(b, C - 8, C + 6, C + 18, Acc); VLine(b, C + 6, C + 8, C + 18, Acc);
            VLine(b, C - 8, C + 4, C + 8, Acc); VLine(b, C, C - 6, C + 4, Acc); HLine(b, C - 8, C, C + 4, Acc);
            FillRect(b, C - 2, C - 16, C + 1, C - 13, Acc); // dot
        }

        static void City(Color32[] b) // crenellated keep + flag
        { Keep(b, C - 20, C + 16, C - 22, C + 10, 4, Dark, Light); FillRect(b, C - 6, C - 22, C + 2, C - 8, Black); Flag(b, C + 14, C + 12, 22); }

        static void Fortress(Color32[] b) // three towers, center tall, + flag
        {
            Keep(b, C - 24, C - 10, C - 16, C + 6, 2, Dark, Light);
            Keep(b, C + 8, C + 22, C - 16, C + 6, 2, Dark, Light);
            Keep(b, C - 8, C + 6, C - 22, C + 14, 2, Dark, Light);
            Flag(b, C - 1, C + 16, 20);
        }

        static void Village(Color32[] b) // two gabled houses
        {
            FillRect(b, C - 22, C - 16, C - 4, C + 2, Dark); TriUp(b, C - 13, C + 2, C + 12, 11, Light);
            FillRect(b, C + 2, C - 16, C + 20, C - 2, Dark);  TriUp(b, C + 11, C - 2, C + 8, 11, Light);
        }

        static void Tower(Color32[] b) // single battlemented tower + flag
        { Keep(b, C - 9, C + 9, C - 22, C + 10, 3, Dark, Light); FillRect(b, C - 3, C - 6, C + 3, C + 4, Black); Flag(b, C + 7, C + 14, 22); }

        static void Temple(Color32[] b) // colonnade + pediment
        {
            TriUp(b, C, C + 6, C + 20, 22, Light);            // pediment
            FillRect(b, C - 22, C + 4, C + 22, C + 6, Light); // architrave
            for (int i = -2; i <= 2; i++) VLine(b, C + i * 9, C - 20, C + 3, Steel); // columns
            FillRect(b, C - 24, C - 22, C + 24, C - 20, Dark); // base
        }

        static void Ruin(Color32[] b) // two broken columns + fallen lintel
        {
            VLine(b, C - 12, C - 14, C + 8, Steel); VLine(b, C - 11, C - 14, C + 4, Steel);
            VLine(b, C + 10, C - 14, C + 14, Steel); VLine(b, C + 11, C - 14, C + 10, Steel);
            FillRect(b, C - 20, C - 20, C - 2, C - 16, Dark); // fallen lintel on the ground
        }

        static void Dungeon(Color32[] b) // stone gate + dark arch + portcullis
        {
            FillRect(b, C - 18, C - 20, C + 18, C + 16, Dark); Disc(b, C, C + 16, 18, Dark);
            FillRect(b, C - 12, C - 20, C + 12, C + 12, Black); Disc(b, C, C + 12, 12, Black); // arch void
            for (int i = -2; i <= 2; i++) VLine(b, C + i * 5, C - 20, C + 10, Steel); // portcullis bars
            HLine(b, C - 12, C + 12, C - 4, Steel); HLine(b, C - 12, C + 12, C + 4, Steel);
        }

        static void Encounter(Color32[] b) // crossed swords
        {
            for (int i = -16; i <= 16; i++)
            { Px(b, C + i, C + i, Steel); Px(b, C + i + 1, C + i, Steel); Px(b, C + i, C - i, Steel); Px(b, C + i + 1, C - i, Steel); }
            FillRect(b, C - 6, C - 20, C + 6, C - 16, Acc); // crossguards hint
        }

        static void Camp(Color32[] b) // tent + crossed apex poles
        {
            TriUp(b, C, C - 18, C + 16, 22, Dark);
            VLine(b, C, C - 18, C + 20, Black); // center seam
            Px(b, C - 6, C + 20, Steel); for (int i = 0; i < 8; i++) { Px(b, C - 6 + i, C + 20 - i, Steel); Px(b, C + 6 - i, C + 20 - i, Steel); } // crossed poles
        }

        static void Port(Color32[] b) // anchor
        {
            VLine(b, C, C - 18, C + 16, Steel);              // shank
            HLine(b, C - 10, C + 10, C - 12, Steel);         // stock
            Disc(b, C, C + 16, 4, Steel); Disc(b, C, C + 16, 2, Black); // ring
            for (int i = 0; i <= 12; i++) { Px(b, C - 16 + i, C - 18 + i, Steel); Px(b, C + 16 - i, C - 18 + i, Steel); } // flukes
        }
    }
}
