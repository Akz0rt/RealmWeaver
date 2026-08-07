using UnityEngine;
using WorldGen.Notes.Rendering;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Выбор портрета с уменьшением до 512 по большой стороне — портрет рисуется в списке
    /// недавних листов, полноразмерная фотография раздувала бы файл впустую.</summary>
    public static class PortraitImport
    {
        const int MaxSide = 512;

        public static byte[] PickAndDownscale()
        {
            var raw = ImagePicker.OpenFileDialog();
            if (raw == null) return null;
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(raw)) return null;
            int side = Mathf.Max(tex.width, tex.height);
            if (side <= MaxSide) return raw;

            float scale = MaxSide / (float)side;
            int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(w, h, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return small.EncodeToPNG();
        }
    }
}
