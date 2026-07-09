using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Держит массив инстансов декораций и сабмитит их GPU-инстансингом каждый кадр
    /// (immediate-mode). Плоские квадры в XZ, семплят атлас каталога, тонируются per-instance.
    /// Без коллайдеров → некликабельно. Родитель — mapRenderer.transform (наследует его локальные коорд.).</summary>
    public class DecorationRenderer : MonoBehaviour
    {
        const int BatchMax = 1023;
        public float LayerY = 0.45f;
        public bool Visible = true;

        Mesh quad;
        Material material;
        DecorationCatalog catalog;
        List<DecorationInstance> instances = new();

        // Переиспользуемые буферы батча.
        readonly Matrix4x4[] mtx = new Matrix4x4[BatchMax];
        readonly Vector4[] uvRects = new Vector4[BatchMax];
        readonly Vector4[] tints = new Vector4[BatchMax];
        MaterialPropertyBlock mpb;

        void EnsureResources()
        {
            if (quad == null) quad = BuildQuad();
            if (material == null)
            {
                var sh = Shader.Find("WorldGen/Decorations");
                if (sh == null)
                {
                    Debug.LogError("[Decorations] Shader 'WorldGen/Decorations' not found — add it to Project Settings → Graphics → Always Included Shaders (else it is stripped from the build).");
                    return;
                }
                material = new Material(sh);
                material.enableInstancing = true;
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
        }

        // Квад в плоскости XZ, пивот низ-центр: локально X∈[-0.5,0.5], Z∈[0,1] (высота вверх по +Z «экрана»).
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "DecorationQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f), new Vector3(-0.5f, 0f, 1f),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        // NB: material.mainTexture биндит только проперть с именем "_MainTex" (или [MainTexture]);
        // у Decorations.shader текстура называется "_Atlas" — используем SetTexture по имени явно
        // (см. тот же паттерн в GpuMapRenderer.SetTexture("_LabelTex"/"_CoastTex"/...) для кастомных имён).
        public void Init(DecorationCatalog cat) { catalog = cat; EnsureResources(); if (material != null) material.SetTexture("_Atlas", cat.Atlas); }

        public void SetInstances(List<DecorationInstance> list) { instances = list ?? new List<DecorationInstance>(); }

        void LateUpdate()
        {
            if (!Visible || catalog == null || material == null || instances.Count == 0) return;
            EnsureResources();
            material.SetTexture("_Atlas", catalog.Atlas);

            // Bilinear-bleed guard (Task 4 review): в атласе нет padding между тайлами, а
            // FilterMode.Bilinear на краю UV-rect может подмешать прозрачный бордюр соседнего
            // тайла → тёмная кайма. Инсетим rect на пол-текселя с каждой стороны, чтобы билинейная
            // интерполяция никогда не выходила за пределы своего тайла.
            float tx = 1f / catalog.Atlas.width, ty = 1f / catalog.Atlas.height;

            // Матрица инстанса: масштаб по scale, позиция = локальная (x, LayerY, z) в родителе (mapRenderer.transform).
            int i = 0;
            while (i < instances.Count)
            {
                int n = Mathf.Min(BatchMax, instances.Count - i);
                for (int b = 0; b < n; b++)
                {
                    var d = instances[i + b];
                    var local = new Vector3(d.worldPos.x, LayerY, d.worldPos.y);
                    var world = transform.localToWorldMatrix * Matrix4x4.TRS(local, Quaternion.identity, new Vector3(d.scale, d.scale, d.scale));
                    mtx[b] = world;
                    var raw = catalog.UvRect(d.type, d.style, d.artVariant);
                    uvRects[b] = new Vector4(raw.x + tx * 0.5f, raw.y + ty * 0.5f, raw.z - tx, raw.w - ty);
                    tints[b] = (Color)d.tint;
                }
                mpb.Clear();
                mpb.SetVectorArray("_UVRect", TrimTo(uvRects, n));
                mpb.SetVectorArray("_Tint", TrimTo(tints, n));
                Graphics.DrawMeshInstanced(quad, 0, material, mtx, n, mpb);
                i += n;
            }
        }

        // SetVectorArray требует ровно count элементов (или фиксированный размер); отдаём срез.
        static readonly List<Vector4> tmp = new();
        static List<Vector4> TrimTo(Vector4[] src, int n)
        { tmp.Clear(); for (int k = 0; k < n; k++) tmp.Add(src[k]); return tmp; }

        void OnDestroy() { if (material != null) Destroy(material); if (quad != null) Destroy(quad); }
    }
}
