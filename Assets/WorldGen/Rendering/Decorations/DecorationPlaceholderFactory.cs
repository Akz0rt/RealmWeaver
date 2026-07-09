using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Процедурно рисует простые side-view спрайты-плейсхолдеры в квадратный тайл
    /// (RGBA, прозрачный фон, пивот низ-центр). Зеркало PoiPlaceholderFactory. Заменяемо:
    /// подложить готовый арт вместо этих рисовалок (см. спеку, шов замены).</summary>
    public static class DecorationPlaceholderFactory
    {
        // Рисует один тайл size×size. Тон приходит из per-instance tint в шейдере, поэтому
        // здесь рисуем в оттенках серого (luminance) + альфа; форма/затенение — важное.
        public static Color32[] DrawTile(DecorationType type, DecorationStyleCategory style, int size, int variant)
        {
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            switch (type)
            {
                case DecorationType.Mountain: DrawMountain(px, size, style, variant); break;
                case DecorationType.Hill: DrawHill(px, size, style); break;
                case DecorationType.Pine: DrawPine(px, size, style); break;
                case DecorationType.AutumnTree: DrawBlobTree(px, size); break;
                case DecorationType.Mesa: DrawMesa(px, size); break;
            }
            return px;
        }

        static void Set(Color32[] px, int size, int x, int y, byte lum, byte a)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            px[y * size + x] = new Color32(lum, lum, lum, a);
        }

        // Тёмный контур + двухтоновый силуэт: левая грань светлее (свет слева).
        static void DrawMountain(Color32[] px, int size, DecorationStyleCategory style, int variant)
        {
            int baseY = size - 2;
            int peakX = size / 2 + (variant % 3 - 1) * size / 10; // лёгкий сдвиг пика по варианту
            int peakY = 2;
            float halfW = size * 0.42f;
            for (int y = peakY; y <= baseY; y++)
            {
                float t = (y - peakY) / (float)(baseY - peakY);
                int spread = Mathf.RoundToInt(halfW * t);
                for (int x = peakX - spread; x <= peakX + spread; x++)
                {
                    bool lit = x < peakX;                 // левая грань — освещённая
                    byte lum = (byte)(lit ? 210 : 120);
                    Set(px, size, x, y, lum, 255);
                }
                // контур
                Set(px, size, peakX - spread, y, 20, 255);
                Set(px, size, peakX + spread, y, 20, 255);
            }
            if (style == DecorationStyleCategory.Snowy)
                for (int y = peakY; y < peakY + size / 4; y++)
                {
                    float t = (y - peakY) / (float)(baseY - peakY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = peakX - spread; x <= peakX + spread; x++) Set(px, size, x, y, 245, 255);
                }
            if (style == DecorationStyleCategory.Forested) // тёмная «лесная» юбка снизу
                for (int y = baseY - size / 5; y <= baseY; y++)
                {
                    float t = (y - peakY) / (float)(baseY - peakY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = peakX - spread; x <= peakX + spread; x++) Set(px, size, x, y, 70, 255);
                }
        }

        static void DrawHill(Color32[] px, int size, DecorationStyleCategory style)
        {
            int cx = size / 2, baseY = size - 2;
            float r = size * 0.42f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - cx) / r, dy = (y - baseY) / r;
                if (dy <= 0 && dx * dx + dy * dy <= 1f)
                    Set(px, size, x, y, (byte)(x < cx ? 190 : 130), 255);
            }
        }

        static void DrawPine(Color32[] px, int size, DecorationStyleCategory style)
        {
            int cx = size / 2, baseY = size - 2;
            // трунк
            for (int y = baseY - size / 6; y <= baseY; y++) { Set(px, size, cx, y, 90, 255); Set(px, size, cx - 1, y, 90, 255); }
            // 3 яруса треугольников
            for (int tier = 0; tier < 3; tier++)
            {
                int topY = 2 + tier * (size / 4);
                int botY = topY + size / 3;
                float halfW = size * (0.36f - tier * 0.06f);
                for (int y = topY; y <= botY; y++)
                {
                    float t = (y - topY) / (float)(botY - topY);
                    int spread = Mathf.RoundToInt(halfW * t);
                    for (int x = cx - spread; x <= cx + spread; x++) Set(px, size, x, y, (byte)(x < cx ? 150 : 90), 255);
                }
            }
            if (style == DecorationStyleCategory.Snowy)
                for (int x = cx - 2; x <= cx + 2; x++) Set(px, size, x, 3, 245, 255);
        }

        static void DrawBlobTree(Color32[] px, int size)
        {
            int cx = size / 2, cy = size / 2 - 1, baseY = size - 2;
            for (int y = baseY - size / 6; y <= baseY; y++) { Set(px, size, cx, y, 90, 255); Set(px, size, cx - 1, y, 90, 255); }
            float r = size * 0.34f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r * r) Set(px, size, x, y, (byte)(x < cx ? 190 : 130), 255);
            }
        }

        static void DrawMesa(Color32[] px, int size)
        {
            int baseY = size - 2, topY = size / 2;
            for (int y = topY; y <= baseY; y++)
            {
                float t = (y - topY) / (float)(baseY - topY);
                int half = Mathf.RoundToInt(size * (0.22f + 0.14f * t));
                for (int x = size / 2 - half; x <= size / 2 + half; x++) Set(px, size, x, y, (byte)(x < size / 2 ? 175 : 120), 255);
            }
        }
    }
}
