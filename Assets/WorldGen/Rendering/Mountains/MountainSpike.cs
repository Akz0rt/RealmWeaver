using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation.Mountains;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// ШИП (задача 1 арки «кисть гор»): проверка того, что нельзя проверить ни харнессом, ни
    /// компилятором — держится ли в Unity алгоритм маляра и как слой гор уживается с остальной
    /// картой. Кисти ещё нет, поэтому ось задаётся отсюда, из контекстного меню.
    ///
    /// Что смотреть глазами:
    ///   1. Порядок. Соседние горы перекрываются. Ближняя (та, что НИЖЕ по экрану) обязана
    ///      закрывать дальнюю. Если порядок скачет или мерцает при движении камеры — вся §10
    ///      требует другого решения, и узнать это надо сейчас, а не через восемь задач.
    ///   2. Что осталось видно. Поставить город, подземелье, границу региона и берег так, чтобы
    ///      горы легли поверх. На прошлой арке заливка гор ПРЯТАЛА значок города, и выглядело это
    ///      как «иконка потерялась». Решаем, что должно быть сверху.
    ///   3. Куда уезжает рисунок. Полупрозрачная лента показывает сам мазок, горы — то, что из него
    ///      выросло. Высота откладывается вверх по экрану, поэтому массив стоит СЕВЕРНЕЕ мазка
    ///      примерно на две ширины горы. От этого зависит, по какому следу поднимать рельеф
    ///      (задача 8) и что делать, когда севернее мазка вода.
    ///   4. Дальний зум. Не превращается ли гряда в грязь.
    ///
    /// Компонент временный: в задаче 5 его место займёт настоящий слой, который берёт геометрию из
    /// пятен, а не из этих кнопок.
    /// </summary>
    public class MountainSpike : MonoBehaviour
    {
        [Header("Источник")]
        [Tooltip("Если не назначено — ищется на этом же объекте.")]
        public WorldMapRenderer mapRenderer;

        [Header("Масштаб (§13 спеки)")]
        [Tooltip("R — радиус горы в мировых единицах карты. Задаёт и шаг слоёв 2R, и предельную полуширину массы.")]
        public float mountainRadius = 10f;
        [Tooltip("T — целевая длина звена, в долях R. По умолчанию 1.6R.")]
        public float linkLengthFactor = 1.6f;
        [Tooltip("t — талия на позвонке: доля полной полуширины на стыке звеньев.")]
        [Range(0.1f, 1f)] public float waist = 0.55f;
        [Tooltip("h — множитель высоты горы: H = h·w, где w — полуширина в середине звена.")]
        public float heightFactor = 2.2f;
        [Tooltip("k — растяжение подошвы вдоль оси. Больше — выше перевалы, плотнее массив.")]
        public float stretch = 1.4f;
        [Tooltip("a — сжатие по вертикали (угол взгляда на землю). Подошва сжимается в a раз.")]
        public float verticalSquash = 1.6f;

        [Header("Слой")]
        [Tooltip("Высота слоя над плоскостью карты. Берег 0.3, границы регионов 0.4, превью реки 0.45.")]
        public float layerY = 0.6f;
        [Tooltip("Толщина линии гребня в мировых единицах.")]
        public float crestWidth = 0.35f;
        [Tooltip("Показывать полупрозрачный след мазка под горами.")]
        public bool showStrokeFootprint = true;

        [Header("Цвета (пока прямые, в задаче 5 поедут из палитры карты)")]
        public Color farColor = new Color(0.30f, 0.42f, 0.46f, 1f);
        public Color nearColor = new Color(0.11f, 0.17f, 0.20f, 1f);
        public Color inkColor = new Color(0.94f, 0.93f, 0.89f, 0.40f);
        public Color footprintColor = new Color(0.95f, 0.45f, 0.25f, 0.35f);

        Transform container;
        Material material;

        [ContextMenu("Шип: три горы (прямая ось)")]
        public void SpikeThree()
        {
            var axis = new List<Vec2>();
            Vector2 c = MapCenter();
            float half = mountainRadius * linkLengthFactor * 1.5f;
            axis.Add(new Vec2(c.x - half, c.y));
            axis.Add(new Vec2(c.x + half, c.y));
            BuildFrom(axis, "ШипТриГоры");
        }

        [ContextMenu("Шип: гряда по дуге")]
        public void SpikeRidge()
        {
            var axis = new List<Vec2>();
            Vector2 c = MapCenter();
            float length = mountainRadius * linkLengthFactor * 9f;
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                float x = c.x - length * 0.5f + length * t;
                float y = c.y + Mathf.Sin(t * Mathf.PI * 1.2f) * mountainRadius * 3f;
                axis.Add(new Vec2(x, y));
            }
            BuildFrom(axis, "ШипГряда");
        }

        [ContextMenu("Шип: убрать")]
        public void SpikeClear()
        {
            if (container == null) return;
            foreach (var mf in container.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
            DestroyImmediate(container.gameObject);
            container = null;
        }

        Vector2 MapCenter()
        {
            var renderer = Renderer();
            return renderer == null
                ? new Vector2(250f, 250f)
                : new Vector2(renderer.mapWidth * 0.5f, renderer.mapHeight * 0.5f);
        }

        WorldMapRenderer Renderer()
        {
            if (mapRenderer == null) mapRenderer = GetComponent<WorldMapRenderer>();
            return mapRenderer;
        }

        void BuildFrom(List<Vec2> axis, string name)
        {
            SpikeClear();

            var renderer = Renderer();
            Transform parent = renderer != null ? renderer.transform : transform;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            container = go.transform;

            if (material == null)
            {
                var shader = Shader.Find("WorldGen/MountainPaint");
                if (shader == null)
                {
                    Debug.LogError("[Горы] Шейдер 'WorldGen/MountainPaint' не найден — проверь, что он в Project Settings → Graphics → Always Included Shaders.");
                    return;
                }
                material = new Material(shader);
            }

            if (showStrokeFootprint)
            {
                var mark = new GameObject("СледМазка");
                mark.transform.SetParent(container, false);
                mark.AddComponent<MeshFilter>().sharedMesh = MountainMeshBuilder.BuildRibbon(
                    axis, mountainRadius * 2f, layerY - 0.05f, footprintColor);
                mark.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            var links = BuildLinks(axis, mountainRadius * linkLengthFactor, mountainRadius);
            var shapes = new List<MountainShape>(links.Count);
            float squash = 1f / Mathf.Max(1f, verticalSquash);
            float sampleStep = Mathf.Max(0.1f, mountainRadius / 15f);
            float minSpan = mountainRadius * 0.1f;

            for (int i = 0; i < links.Count; i++)
            {
                var outline = LinkOutline.Build(links[i], waist, sampleStep);
                if (outline == null) continue;
                // У свободного конца оси соседа нет — растягивать туда некуда, иначе подошва
                // вылезает за нарисованный мазок (§14 «вылет за мазок»).
                float back = i == 0 ? 1f : stretch;
                float forward = i == links.Count - 1 ? 1f : stretch;
                var shape = MoundBuilder.Build(outline, links[i], heightFactor, squash, back, forward, minSpan);
                if (shape != null) shapes.Add(shape);
            }

            var body = new GameObject("Горы");
            body.transform.SetParent(container, false);
            body.AddComponent<MeshFilter>().sharedMesh = MountainMeshBuilder.Build(
                shapes, layerY, farColor, nearColor, inkColor, crestWidth);
            body.AddComponent<MeshRenderer>().sharedMaterial = material;

            Debug.Log($"[Горы] Шип собран: звеньев {links.Count}, гор {shapes.Count}, R={mountainRadius}.");
        }

        /// <summary>Резка оси на звенья — ВРЕМЕННАЯ, для шипа: равные куски и постоянная полуширина.
        /// Настоящая (§8: анизотропная метрика, разброс, замкнутые оси, ярусы) придёт в задаче 4.</summary>
        static List<AxisLink> BuildLinks(List<Vec2> axis, float linkLength, float halfWidth)
        {
            var result = new List<AxisLink>();
            if (axis.Count < 2 || linkLength <= 0f) return result;

            float total = 0f;
            for (int i = 1; i < axis.Count; i++) total += (axis[i] - axis[i - 1]).Length();
            int count = Mathf.Max(1, Mathf.RoundToInt(total / linkLength));
            float step = total / count;

            var current = new List<Vec2> { axis[0] };
            float walked = 0f, taken = 0f;
            int index = 1;
            Vec2 cursor = axis[0];

            while (index < axis.Count)
            {
                Vec2 next = axis[index];
                float segment = (next - cursor).Length();
                if (segment <= 1e-6f) { index++; cursor = next; continue; }

                if (taken + segment < step - 1e-6f)
                {
                    taken += segment;
                    walked += segment;
                    cursor = next;
                    current.Add(cursor);
                    index++;
                    continue;
                }

                float need = step - taken;
                Vec2 cut = cursor + (next - cursor) * (need / segment);
                current.Add(cut);
                result.Add(MakeLink(current, halfWidth, result.Count));
                current = new List<Vec2> { cut };
                cursor = cut;
                taken = 0f;
                walked += need;
            }

            if (current.Count >= 2) result.Add(MakeLink(current, halfWidth, result.Count));
            return result;
        }

        static AxisLink MakeLink(List<Vec2> pts, float halfWidth, int index)
        {
            var link = new AxisLink { Pts = new List<Vec2>(pts), Tier = 0 };
            for (int i = 0; i < pts.Count; i++) link.Ws.Add(halfWidth);

            Vec2 a = pts[0], b = pts[pts.Count - 1];
            link.Mid = (a + b) * 0.5f;
            Vec2 dir = b - a;
            link.Tan = dir.LengthSquared() < 1e-8f ? new Vec2(1f, 0f) : Vec2.Normalize(dir);
            link.MidW = halfWidth;
            // Разброс высоты 0.82…1.18, детерминированный: одна и та же ось даёт одну и ту же гряду.
            uint h = (uint)(index * 2654435761u);
            h ^= h >> 15;
            link.HeightJitter = 0.82f + (h % 1000) / 1000f * 0.36f;
            return link;
        }
    }
}
