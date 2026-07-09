using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    public enum DecorationType { Mountain, Hill, Pine, AutumnTree, Mesa }

    /// <summary>Контекст-категория варианта (детерминирована по биому/температуре клетки).
    /// Bare/Snowy/Forested — для гор и холмов; Snowy/Plain — для хвои; Plain — для остальных.</summary>
    public enum DecorationStyleCategory { Bare, Snowy, Forested, Plain }

    /// <summary>Один спрайт декорации: что и где рисовать. worldPos в координатах карты (XZ).</summary>
    public struct DecorationInstance
    {
        public Vector2 worldPos;              // x = worldX, y = worldZ (карта XZ)
        public DecorationType type;
        public DecorationStyleCategory style;
        public int artVariant;                // индекс картинки внутри (type, style)
        public float scale;                   // мировой размер (высота спрайта в мировых единицах)
        public Color32 tint;
        public float sortZ;                   // = worldPos.y; back-to-front по возрастанию
    }
}
