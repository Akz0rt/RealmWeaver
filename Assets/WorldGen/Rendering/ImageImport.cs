using UnityEngine;

namespace WorldGen.Rendering
{
    /// <summary>Loads an image file and re-encodes it as a size-bounded PNG for Room.Preview. Bounding the
    /// longest side keeps a 60-building town's .dndproj from ballooning (spec §9).</summary>
    public static class ImageImport
    {
        public static byte[] LoadAndShrink(string path, int maxSide)
        {
            byte[] raw;
            try { raw = System.IO.File.ReadAllBytes(path); }
            catch { return null; }

            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(raw)) { Object.Destroy(tex); return null; }

            int w = tex.width, h = tex.height;
            int longSide = w > h ? w : h;
            if (longSide <= maxSide)
            {
                var asPng = tex.EncodeToPNG();
                Object.Destroy(tex);
                return asPng;
            }

            float scale = (float)maxSide / longSide;
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            var rt = RenderTexture.GetTemporary(nw, nh);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var png = small.EncodeToPNG();
            Object.Destroy(tex);
            Object.Destroy(small);
            return png;
        }
    }
}
