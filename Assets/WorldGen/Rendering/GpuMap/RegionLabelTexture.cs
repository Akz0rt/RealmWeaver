using UnityEngine;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RG8-текстура сглаженных областей: R = familyLabel, G = bandLabel (индексы 0..254).
    /// Значение 255 = sentinel "нет метки" (клин тройного стыка) → шейдер откатывается к family/band
    /// из attribute-текстуры. Point-фильтр, разрешение = cell-id.</summary>
    public class RegionLabelTexture
    {
        public Texture2D Texture { get; private set; }
        public const byte NoLabel = 255;
        int texW, texH;
        Color32[] pixels;

        public Vector4 Texel => new Vector4(1f / texW, 1f / texH, 0, 0);

        public void Build(int[] familyLabel, int[] bandLabel, int w, int h)
        {
            texW = w; texH = h;
            if (Texture != null) Object.Destroy(Texture);
            Texture = new Texture2D(w, h, TextureFormat.RG16, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Encode(familyLabel[i], bandLabel[i]);
            Apply();
        }

        /// <summary>Патч под-прямоугольника (кисть): пере-кодировать rect из label-буферов.</summary>
        public void PatchRect(int[] familyLabel, int[] bandLabel, int rectX, int rectY, int rectW, int rectH)
        {
            for (int y = rectY; y < rectY + rectH; y++)
                for (int x = rectX; x < rectX + rectW; x++)
                { int i = y * texW + x; pixels[i] = Encode(familyLabel[i], bandLabel[i]); }
            Apply();
        }

        static Color32 Encode(int family, int band)
        {
            byte r = (byte)(family < 0 ? NoLabel : Mathf.Clamp(family, 0, 254));
            byte g = (byte)(band   < 0 ? NoLabel : Mathf.Clamp(band,   0, 254));
            return new Color32(r, g, 0, 255);
        }

        public void Apply() { Texture.SetPixels32(pixels); Texture.Apply(false); }
        public void Destroy() { if (Texture != null) Object.Destroy(Texture); }
    }
}
