using System.Collections;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.MapRaster;

namespace WorldGen.Rendering.GpuMap
{
    /// <summary>RFloat текстура: значение пикселя = (float)cellId ближайшей клетки к центру пикселя.
    /// Печётся ОДИН РАЗ при генерации/загрузке - при правке кистью не меняется (сайты Вороного
    /// неподвижны). Point-фильтр: cellId - дискретная метка, интерполировать нельзя.</summary>
    public static class CellIdTexture
    {
        public static Texture2D Build(NearestCellLookup lookup, int texW, int texH, float mapW, float mapH)
        {
            var tex = NewTex(texW, texH);
            Fill(tex, lookup, texW, texH, mapW, mapH, 0, texH);
            tex.Apply(false);
            return tex;
        }

        public static IEnumerator BuildStepped(NearestCellLookup lookup, int texW, int texH, float mapW, float mapH,
            System.Action<Texture2D> onDone, System.Action<float> onProgress)
        {
            var tex = NewTex(texW, texH);
            const int chunkRows = 64;
            for (int y0 = 0; y0 < texH; y0 += chunkRows)
            {
                int rows = Mathf.Min(chunkRows, texH - y0);
                Fill(tex, lookup, texW, texH, mapW, mapH, y0, rows);
                onProgress?.Invoke((y0 + rows) / (float)texH);
                yield return null;
            }
            tex.Apply(false);
            onDone?.Invoke(tex);
        }

        static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RFloat, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        static void Fill(Texture2D tex, NearestCellLookup lookup, int texW, int texH, float mapW, float mapH, int y0, int rows)
        {
            var px = new Color[texW * rows];
            for (int y = 0; y < rows; y++)
            {
                float wz = (y0 + y + 0.5f) / texH * mapH;
                for (int x = 0; x < texW; x++)
                {
                    float wx = (x + 0.5f) / texW * mapW;
                    var cell = lookup.FindNearest(new System.Numerics.Vector2(wx, wz));
                    px[y * texW + x] = new Color(cell != null ? cell.Id : -1f, 0, 0, 0);
                }
            }
            tex.SetPixels(0, y0, texW, rows, px);
        }
    }
}
