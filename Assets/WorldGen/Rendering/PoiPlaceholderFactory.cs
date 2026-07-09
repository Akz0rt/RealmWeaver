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
        static readonly Color32 IcoLight = new Color32(0xc9, 0xd2, 0xdc, 255); // main silhouette (light — pops on dark disc)
        static readonly Color32 IcoShade = new Color32(0x6a, 0x74, 0x80, 255); // internal shadow/void (still lighter than disc)

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
            DrawFrame(buf);                       // unchanged medallion frame
            var scratch = new Color32[S * S];     // transparent icon scratch (y-up, buf[y*S+x])
            DrawIcon(scratch, type);              // bold light concept, drawn at any size/position
            FitIcon(buf, scratch);                // measure bbox, scale to ~80% disc, center, blit opaque px

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

        // Measure the scratch's opaque bbox, scale it to ~80% of the disc interior, center on the disc,
        // and blit (nearest-neighbor, opaque pixels only) onto dst. Uniform large fill for every icon
        // regardless of how big/where its routine drew.
        static void FitIcon(Color32[] dst, Color32[] sc)
        {
            int minX = S, minY = S, maxX = -1, maxY = -1;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    if (sc[y * S + x].a != 0)
                    { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX < minX) return; // nothing drawn

            float bboxW = maxX - minX + 1, bboxH = maxY - minY + 1;
            float bboxDiag = Mathf.Sqrt(bboxW * bboxW + bboxH * bboxH);
            if (bboxDiag < 1f) return; // guard div-by-zero

            float R = S * 0.5f;
            float rAccIn = R - 10f;                 // disc interior radius (matches DrawFrame: rDisc-2 → -rimW5 -accW3)
            float targetDiag = (2f * rAccIn) * 0.80f;
            float s = targetDiag / bboxDiag;

            float bcx = (minX + maxX) * 0.5f, bcy = (minY + maxY) * 0.5f;
            float dcx = (S - 1) * 0.5f, dcy = (S - 1) * 0.5f;   // disc center (matches DrawFrame)

            int y0 = Mathf.FloorToInt(dcy - rAccIn), y1 = Mathf.CeilToInt(dcy + rAccIn);
            int x0 = Mathf.FloorToInt(dcx - rAccIn), x1 = Mathf.CeilToInt(dcx + rAccIn);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if ((uint)x >= S || (uint)y >= S) continue;
                    int srcX = Mathf.RoundToInt(bcx + (x - dcx) / s);
                    int srcY = Mathf.RoundToInt(bcy + (y - dcy) / s);
                    if ((uint)srcX >= S || (uint)srcY >= S) continue;
                    var c = sc[srcY * S + srcX];
                    if (c.a != 0) dst[y * S + x] = c;
                }
        }

        static void HBar(Color32[] b, int x0, int x1, int yc, int t, Color32 c)
        { FillRect(b, x0, yc - t / 2, x1, yc - t / 2 + t - 1, c); }
        static void VBar(Color32[] b, int xc, int y0, int y1, int t, Color32 c)
        { FillRect(b, xc - t / 2, y0, xc - t / 2 + t - 1, y1, c); }
        // Thick rounded stroke between two points (disc-stamped) — for swords, poles, anchor.
        static void Stroke(Color32[] b, float x0, float y0, float x1, float y1, float t, Color32 c)
        {
            float dx = x1 - x0, dy = y1 - y0;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy))) + 1;
            for (int i = 0; i <= steps; i++) { float u = i / (float)steps; Disc(b, x0 + dx * u, y0 + dy * u, t * 0.5f, c); }
        }
        // A few crenellation teeth of height h atop [x0..x1] at yBase.
        static void Merlons(Color32[] b, int x0, int x1, int yBase, int h, Color32 c)
        {
            int w = x1 - x0 + 1, tooth = Mathf.Max(3, w / 5);
            for (int mx = x0; mx <= x1 - tooth + 1; mx += tooth * 2)
                FillRect(b, mx, yBase, Mathf.Min(mx + tooth - 1, x1), yBase + h, c);
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

        const int M = S / 2; // scratch center (64)

        static void City(Color32[] b) // walled keep + banner
        {
            FillRect(b, M - 28, M - 28, M + 24, M + 20, IcoLight);
            Merlons(b, M - 28, M + 24, M + 20, 12, IcoLight);
            FillRect(b, M - 7, M - 28, M + 7, M - 4, IcoShade);                 // gate
            FillRect(b, M - 20, M, M - 11, M + 9, IcoShade);                    // window
            FillRect(b, M + 11, M, M + 20, M + 9, IcoShade);                    // window
            VBar(b, M + 20, M + 32, M + 58, 4, IcoLight);                       // flagpole
            FillRect(b, M + 22, M + 47, M + 45, M + 58, Acc);                   // pennant
        }

        static void Fortress(Color32[] b) // three towers, center tall + banner
        {
            FillRect(b, M - 34, M - 28, M - 14, M + 10, IcoLight); Merlons(b, M - 34, M - 14, M + 10, 10, IcoLight);
            FillRect(b, M + 14, M - 28, M + 34, M + 10, IcoLight); Merlons(b, M + 14, M + 34, M + 10, 10, IcoLight);
            FillRect(b, M - 11, M - 28, M + 11, M + 28, IcoLight); Merlons(b, M - 11, M + 11, M + 28, 12, IcoLight);
            FillRect(b, M - 6, M - 28, M + 6, M - 6, IcoShade);                 // center gate
            VBar(b, M, M + 40, M + 62, 4, IcoLight); FillRect(b, M + 2, M + 51, M + 24, M + 62, Acc);
        }

        static void Village(Color32[] b) // two gabled houses
        {
            FillRect(b, M - 34, M - 24, M - 6, M + 2, IcoLight); TriUp(b, M - 20, M + 2, M + 22, 15, IcoLight);
            FillRect(b, M - 23, M - 24, M - 15, M - 6, IcoShade);               // door
            FillRect(b, M - 2, M - 24, M + 30, M - 2, IcoLight); TriUp(b, M + 14, M - 2, M + 14, 17, IcoLight);
            FillRect(b, M + 8, M - 24, M + 16, M - 8, IcoShade);                // door
        }

        static void Tower(Color32[] b) // single battlemented tower + banner
        {
            FillRect(b, M - 13, M - 30, M + 13, M + 26, IcoLight); Merlons(b, M - 13, M + 13, M + 26, 12, IcoLight);
            FillRect(b, M - 5, M - 30, M + 5, M - 10, IcoShade);                // door
            FillRect(b, M - 4, M + 4, M + 4, M + 14, IcoShade);                 // window
            VBar(b, M + 12, M + 38, M + 62, 4, IcoLight); FillRect(b, M + 14, M + 51, M + 34, M + 62, Acc);
        }

        static void Temple(Color32[] b) // colonnade + pediment
        {
            FillRect(b, M - 34, M - 26, M + 34, M - 16, IcoLight);              // base/steps
            for (int i = -2; i <= 2; i++) VBar(b, M + i * 15, M - 16, M + 14, 8, IcoLight); // 5 columns
            FillRect(b, M - 36, M + 14, M + 36, M + 22, IcoLight);              // architrave
            TriUp(b, M, M + 22, M + 44, 40, IcoLight);                          // pediment
        }

        static void Ruin(Color32[] b) // broken columns + fallen lintel
        {
            FillRect(b, M - 34, M - 26, M + 34, M - 18, IcoShade);              // rubble ground
            VBar(b, M - 20, M - 18, M + 24, 9, IcoLight);                       // tall column
            VBar(b, M + 2, M - 18, M + 4, 9, IcoLight);                         // stub
            VBar(b, M + 22, M - 18, M + 14, 9, IcoLight);                       // mid column
            FillRect(b, M + 6, M + 26, M + 34, M + 34, IcoLight);               // fallen lintel
        }

        static void Dungeon(Color32[] b) // stone gate + dark arch + portcullis
        {
            FillRect(b, M - 30, M - 26, M + 30, M + 24, IcoLight);              // gate block
            Merlons(b, M - 30, M + 30, M + 24, 10, IcoLight);
            FillRect(b, M - 16, M - 26, M + 16, M + 14, IcoShade);              // arch void
            Disc(b, M, M + 14, 16, IcoShade);
            for (int i = -2; i <= 2; i++) VBar(b, M + i * 8, M - 26, M + 12, 4, IcoLight); // portcullis bars
            HBar(b, M - 16, M + 16, M - 6, 4, IcoLight); HBar(b, M - 16, M + 16, M + 6, 4, IcoLight);
        }

        static void Encounter(Color32[] b) // crossed swords, gold guards
        {
            Stroke(b, M - 26, M - 26, M + 26, M + 26, 7, IcoLight);
            Stroke(b, M + 26, M - 26, M - 26, M + 26, 7, IcoLight);
            Stroke(b, M - 30, M - 18, M - 18, M - 30, 6, Acc);                  // guard
            Stroke(b, M + 18, M - 30, M + 30, M - 18, 6, Acc);                  // guard
            Disc(b, M - 28, M - 28, 5, Acc); Disc(b, M + 28, M - 28, 5, Acc);   // pommels
        }

        static void Camp(Color32[] b) // tent + crossed apex poles
        {
            TriUp(b, M, M - 26, M + 26, 34, IcoLight);
            VBar(b, M, M - 26, M + 30, 4, IcoShade);                            // seam
            FillRect(b, M - 10, M - 26, M + 10, M - 18, IcoShade);              // entrance
            Stroke(b, M - 10, M + 22, M + 10, M + 34, 4, IcoLight);
            Stroke(b, M + 10, M + 22, M - 10, M + 34, 4, IcoLight);
        }

        static void Port(Color32[] b) // anchor
        {
            VBar(b, M, M - 26, M + 22, 6, IcoLight);                            // shank
            Disc(b, M, M + 22, 8, IcoLight); Disc(b, M, M + 22, 3, IcoShade);   // ring
            HBar(b, M - 14, M + 14, M + 12, 6, IcoLight);                       // stock
            Stroke(b, M, M - 20, M - 22, M - 4, 6, IcoLight);                   // left arm
            Stroke(b, M, M - 20, M + 22, M - 4, 6, IcoLight);                   // right arm
            Stroke(b, M - 22, M - 4, M - 16, M + 3, 6, IcoLight);               // left fluke
            Stroke(b, M + 22, M - 4, M + 16, M + 3, 6, IcoLight);               // right fluke
        }

        static void Unknown(Color32[] b) // bold accent "?"
        {
            Stroke(b, M - 14, M + 16, M + 2, M + 26, 8, Acc);
            Stroke(b, M + 2, M + 26, M + 14, M + 14, 8, Acc);
            Stroke(b, M + 14, M + 14, M - 2, M + 2, 8, Acc);
            VBar(b, M - 2, M - 12, M + 2, 8, Acc);                              // stem
            Disc(b, M - 2, M - 26, 6, Acc);                                     // dot
        }
    }
}
