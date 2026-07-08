using UnityEngine;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RFloat поле дистанции до берега: 0 на суше, приближённое (chamfer, 2 прохода)
    /// расстояние в ПОЛНОРАЗМЕРНЫХ пикселях до ближайшей суши на воде, клампнуто maxDist. Нужно
    /// шейдеру для ПЛАВНОЙ глубины воды (мелко у берега → глубоко вдали) и широкого свечения берега.
    ///
    /// Считается в ПОНИЖЕННОМ разрешении (downscale): поле дистанции гладкое и сэмплится билинейно,
    /// так что низкое разрешение визуально неотличимо, но пересчёт (при правке берега кистью) в
    /// downscale² раз дешевле. Значения дистанции остаются в полноразмерных пикселях (шаги chamfer
    /// умножены на downscale), поэтому шейдерные _WaterDepthRange/_GlowWidth не зависят от downscale.
    /// Строится из уже готового массива cell-id (без повторного FindNearest/GetPixels).</summary>
    public static class CoastDistanceTexture
    {
        public static Texture2D Build(int[] cellIds, System.Func<int, bool> isWaterCell,
            int fullW, int fullH, int downscale, float maxDist)
        {
            int f = Mathf.Max(1, downscale);
            int w = Mathf.Max(1, (fullW + f - 1) / f);
            int h = Mathf.Max(1, (fullH + f - 1) / f);

            var d = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int fx = Mathf.Min(x * f + f / 2, fullW - 1);
                    int fy = Mathf.Min(y * f + f / 2, fullH - 1);
                    int cid = cellIds[fy * fullW + fx];
                    bool water = cid < 0 || isWaterCell(cid);
                    d[y * w + x] = water ? maxDist : 0f;   // суша = 0, вода = +∞(=maxDist)
                }

            // Шаги в полноразмерных пикселях: соседний low-res пиксель = f full-res пикселей.
            float D1 = f, D2 = f * 1.41421356f;

            // Проход 1: сверху-слева → снизу-справа.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    float v = d[idx];
                    if (x > 0) v = Mathf.Min(v, d[idx - 1] + D1);
                    if (y > 0) v = Mathf.Min(v, d[idx - w] + D1);
                    if (x > 0 && y > 0) v = Mathf.Min(v, d[idx - w - 1] + D2);
                    if (x < w - 1 && y > 0) v = Mathf.Min(v, d[idx - w + 1] + D2);
                    d[idx] = v;
                }

            // Проход 2: снизу-справа → сверху-слева.
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int idx = y * w + x;
                    float v = d[idx];
                    if (x < w - 1) v = Mathf.Min(v, d[idx + 1] + D1);
                    if (y < h - 1) v = Mathf.Min(v, d[idx + w] + D1);
                    if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[idx + w + 1] + D2);
                    if (x > 0 && y < h - 1) v = Mathf.Min(v, d[idx + w - 1] + D2);
                    d[idx] = Mathf.Min(v, maxDist);
                }

            var tex = new Texture2D(w, h, TextureFormat.RFloat, false)
            {
                filterMode = FilterMode.Bilinear,   // непрерывное поле - билинейно = гладко даже в low-res
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[d.Length];
            for (int i = 0; i < d.Length; i++) px[i] = new Color(d[i], 0, 0, 0);
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }
}
