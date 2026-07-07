using UnityEngine;

namespace WorldGen.Rendering.MapRaster
{
    /// <summary>
    /// Хэш/шум-функции, побитово портированные из design_handoff_realmweaver_map/Terra Umbrarum.dc.html
    /// (JS: hash/vn/fbm) - unchecked int32 math воспроизводит Math.imul/>>> ровно так же, как в JS,
    /// чтобы один seed давал одинаковый результат на любой платформе.
    /// </summary>
    public static class Noise
    {
        public static float Hash(int ix, int iy, int s)
        {
            unchecked
            {
                int h = ix * 374761393 + iy * 668265263 + s * 362437;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        static float SmoothStep(float t) => t * t * (3f - 2f * t);

        public static float ValueNoise(float x, float y, int s)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;

            float a = Hash(x0, y0, s);
            float b = Hash(x0 + 1, y0, s);
            float c = Hash(x0, y0 + 1, s);
            float d = Hash(x0 + 1, y0 + 1, s);

            float u = SmoothStep(fx), v = SmoothStep(fy);
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        }

        public static float Fbm(float x, float y, int s, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, s + i * 97);
                freq *= 2f;
                amp *= 0.5f;
            }
            return sum;
        }
    }
}
