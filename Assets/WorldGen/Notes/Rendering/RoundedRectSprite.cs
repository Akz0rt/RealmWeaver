using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>Один общий 9-slice спрайт со скруглёнными углами: рамка карточки и её рабочая
    /// область растягивают его, не искажая углы. Генерируется в рантайме и кэшируется на класс —
    /// той же дорогой, что NotesToolbar.GetBackdropSprite делает свой круг, чтобы в проекте не
    /// заводилось файлов-картинок.</summary>
    public static class RoundedRectSprite
    {
        static Sprite cached;

        public static Sprite Get()
        {
            if (cached != null) return cached;

            const int size = 32;
            const float radius = 8f;   // = CardChrome.CornerRadiusPx при 1:1 (спрайт 100 ppu, канвас 100)
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "NotesRoundedRect";

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius));
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius));
                    float a = 1f;
                    if (dx > 0f && dy > 0f)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        a = Mathf.Clamp01(radius - d + 0.5f);   // мягкий край шириной в пиксель
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();

            cached = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return cached;
        }
    }
}
