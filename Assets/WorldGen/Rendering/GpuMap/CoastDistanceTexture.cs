using UnityEngine;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RFloat поле дистанции до берега: 0 на суше, приближённое (chamfer, 2 прохода)
    /// расстояние в пикселях до ближайшей суши на воде, клампнуто maxDist. Нужно шейдеру для
    /// ПЛАВНОЙ глубины воды (мелко у берега → глубоко вдали) и широкого свечения берега.
    /// Строится из уже готовой cell-id текстуры (без повторного FindNearest).</summary>
    public static class CoastDistanceTexture
    {
        public static Texture2D Build(Texture2D cellIdTex, System.Func<int, bool> isWaterCell,
            int texW, int texH, float maxDist)
        {
            var ids = cellIdTex.GetPixels();   // cellIdTex создаётся readable (Apply(false))
            var d = new float[texW * texH];
            for (int i = 0; i < d.Length; i++)
            {
                int cid = Mathf.RoundToInt(ids[i].r);
                bool water = cid < 0 || isWaterCell(cid);
                d[i] = water ? maxDist : 0f;   // суша = 0, вода = +∞(=maxDist)
            }

            const float D1 = 1f, D2 = 1.41421356f;

            // Проход 1: сверху-слева → снизу-справа.
            for (int y = 0; y < texH; y++)
                for (int x = 0; x < texW; x++)
                {
                    int idx = y * texW + x;
                    float v = d[idx];
                    if (x > 0) v = Mathf.Min(v, d[idx - 1] + D1);
                    if (y > 0) v = Mathf.Min(v, d[idx - texW] + D1);
                    if (x > 0 && y > 0) v = Mathf.Min(v, d[idx - texW - 1] + D2);
                    if (x < texW - 1 && y > 0) v = Mathf.Min(v, d[idx - texW + 1] + D2);
                    d[idx] = v;
                }

            // Проход 2: снизу-справа → сверху-слева.
            for (int y = texH - 1; y >= 0; y--)
                for (int x = texW - 1; x >= 0; x--)
                {
                    int idx = y * texW + x;
                    float v = d[idx];
                    if (x < texW - 1) v = Mathf.Min(v, d[idx + 1] + D1);
                    if (y < texH - 1) v = Mathf.Min(v, d[idx + texW] + D1);
                    if (x < texW - 1 && y < texH - 1) v = Mathf.Min(v, d[idx + texW + 1] + D2);
                    if (x > 0 && y < texH - 1) v = Mathf.Min(v, d[idx + texW - 1] + D2);
                    d[idx] = Mathf.Min(v, maxDist);
                }

            var tex = new Texture2D(texW, texH, TextureFormat.RFloat, false)
            {
                filterMode = FilterMode.Bilinear,   // непрерывное поле - билинейная интерполяция даёт гладкость
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
