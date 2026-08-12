using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Generation.Mountains;
using WorldGen.Rendering.MapRaster;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Слой нарисованных гор: хранит мазки и показывает то, что из них выросло.
    ///
    /// Устройство простое и повторяет нарисованные реки: в памяти лежат ТОЛЬКО мазки, вся геометрия
    /// производная и считается заново. Отменить мазок — значит выкинуть его из списка и пересчитать;
    /// откатывать нечего, и мазок, сливший два массива, при отмене честно разливает их обратно.
    ///
    /// Считается всё в фоне, потому что счёт стоит десятки миллисекунд (а на исполинском мазке —
    /// сотни), и в кадр он не помещается. Отсюда две тонкости, обе намеренные:
    ///
    /// • В РЕДАКТОРЕ, когда сцена не запущена, Update не тикает — и результат фонового счёта некому
    ///   было бы забрать. Поэтому вне игры считаем прямо в вызове: пункт меню обязан работать, иначе
    ///   на чекпоинте ДМ жмёт кнопку и не видит ничего.
    /// • Пока идёт счёт, показывается лента следа кисти. Она появляется мгновенно и говорит «мазок
    ///   принят», а горы подменяют её, когда досчитаются. Так же сделано у рек и рельефа.
    ///
    /// Кисти на этом слое ещё нет — она придёт задачей позже; сейчас мазок ставится пунктом
    /// контекстного меню компонента.
    /// </summary>
    public class MountainLayer : MonoBehaviour
    {
        [Header("Источник")]
        [Tooltip("Если не назначено — ищется на этом же объекте.")]
        public WorldMapRenderer mapRenderer;

        [Header("Размер (§13 спеки)")]
        [Tooltip("R — радиус горы в мировых единицах карты. Задаёт и шаг слоёв 2R, и предельную полуширину массы.")]
        public float mountainRadius = 10f;
        [Tooltip("T — целевая длина звена, в долях R.")]
        public float linkLengthFactor = 1.6f;
        [Tooltip("Разброс длин звеньев: 0.06 — шесть процентов.")]
        [Range(0f, 0.3f)] public float lengthJitter = 0.06f;
        [Tooltip("a — угол взгляда на землю: во столько раз вертикаль «дороже» при нарезке и во столько же сплющена подошва.")]
        public float anisotropy = 1.6f;

        [Header("Форма")]
        [Tooltip("t — талия на позвонке: доля полной полуширины на стыке звеньев.")]
        [Range(0.1f, 1f)] public float waist = 0.55f;
        [Tooltip("h — множитель высоты: H = h·w, где w — полуширина в середине звена.")]
        public float heightFactor = 2.2f;
        [Tooltip("k — растяжение подошвы вдоль оси. Больше — выше перевалы, плотнее массив.")]
        public float stretch = 1.4f;
        [Tooltip("Сколько ярусов различаем: ярус 0 — гряда по краю массива.")]
        [Range(1, 4)] public int tiers = 3;
        [Tooltip("Высота горы у края массива как доля высоты в сердцевине.")]
        [Range(0.2f, 1f)] public float edgeHeight = 0.55f;

        [Header("Слой")]
        [Tooltip("Высота слоя над плоскостью карты. Берег 0.3, границы регионов 0.4, превью реки 0.45.")]
        public float layerY = 0.6f;
        [Tooltip("Толщина линии гребня в мировых единицах.")]
        public float crestWidth = 0.35f;
        [Tooltip("Размах тональной шкалы в мировых единицах. 0 — по фактической глубине массива, как в прототипе.")]
        public float toneSpan = 0f;
        [Tooltip("Прозрачность блика на гребне, 0…255.")]
        [Range(0, 255)] public int crestAlpha = 90;
        [Tooltip("Показывать ли слой.")]
        public bool visible = true;
        [Tooltip("Горы только по суше (решение ДМ). Снять — можно рисовать и по воде.")]
        public bool onlyOnLand = true;

        [Header("Цвет")]
        [Tooltip("Брать концы тональной шкалы из палитры карты (слоты MtnL и MtnS). Снять — красить своими цветами ниже.")]
        public bool useMapPalette = true;
        public Color farColor = new Color(0.55f, 0.59f, 0.64f, 1f);
        public Color nearColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        public Color inkColor = new Color(0.94f, 0.93f, 0.89f, 0.35f);
        [Tooltip("Цвет ленты следа кисти, которая видна, пока считаются горы.")]
        public Color previewColor = new Color(0.95f, 0.45f, 0.25f, 0.30f);

        const string ContainerName = "СлойГор";

        readonly List<MountainStroke> strokes = new List<MountainStroke>();
        int nextStrokeId = 1;

        Transform container;
        Material material;
        MeshFilter body;
        MeshFilter preview;

        Task<List<MountainShape>> pending;
        int generation;
        int pendingGeneration;
        float rebuildAt = -1f;

        public IReadOnlyList<MountainStroke> Strokes => strokes;

        // ── мазки ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Кладёт мазок и пересчитывает слой. Точки — в Site-координатах карты.</summary>
        public MountainStroke AddStroke(List<Vec2> points, float radius, bool erase)
        {
            if (points == null || points.Count == 0 || radius <= 0f) return null;
            var stroke = new MountainStroke { Id = nextStrokeId++, Radius = radius, Erase = erase };
            stroke.Points.AddRange(points);
            strokes.Add(stroke);
            Rebuild();
            return stroke;
        }

        /// <summary>Убирает мазок (отмена). Номера не переиспользуются: по самому старому мазку
        /// пятна берётся зерно разброса, и повторно выданный номер сдвинул бы уже нарисованное.</summary>
        public void RemoveStroke(MountainStroke stroke)
        {
            if (stroke == null || !strokes.Remove(stroke)) return;
            // Отложенно: «Отменить всё» снимает мазки по одному, и на каждый считать заново — это
            // десяток лишних полных пересчётов подряд ради последнего.
            RebuildSoon(0.05f);
        }

        public void ClearStrokes()
        {
            if (strokes.Count == 0) return;
            strokes.Clear();
            Rebuild();
        }

        // ── показ ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Лента следа кисти: показывает, что мазок принят, пока горы считаются.</summary>
        public void ShowPreview(List<Vec2> points, float radius)
        {
            EnsureContainer();
            if (preview == null || points == null || points.Count < 2 || radius <= 0f) return;
            MountainMeshBuilder.BuildRibbon(preview.sharedMesh, points, radius * 2f, layerY - 0.05f,
                                            previewColor);
        }

        public void ClearPreview()
        {
            if (preview != null && preview.sharedMesh != null) preview.sharedMesh.Clear();
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (container != null) container.gameObject.SetActive(value);
        }

        /// <summary>
        /// Пересчёт слоя. В игре уходит в фон, в редакторе считается на месте (там некому забрать
        /// результат). Устаревшие ответы отбрасываются по номеру поколения: ДМ ведёт мазок, счёт
        /// запускается на каждое движение, и прийти они могут не по порядку.
        /// </summary>
        public void Rebuild()
        {
            EnsureContainer();
            generation++;

            var snapshot = Snapshot();
            var settings = BuildSettings();

            if (!Application.isPlaying)
            {
                pending = null;
                Apply(Compute(snapshot, settings));
                return;
            }

            pendingGeneration = generation;
            pending = Task.Run(() => Compute(snapshot, settings));
        }

        /// <summary>Просит пересчёт «попозже». Нужен ползункам: ДМ ведёт ползунок, значение меняется
        /// на каждый пиксель, а полный пересчёт стоит десятки миллисекунд — считать столько раз
        /// незачем. Каждый новый вызов отодвигает срок, поэтому считается один раз, когда ползунок
        /// замер.</summary>
        public void RebuildSoon(float delay = 0.2f)
        {
            // Вне игры Update не тикает — отложенный пересчёт там просто не наступил бы никогда.
            if (!Application.isPlaying) { Rebuild(); return; }
            rebuildAt = Time.realtimeSinceStartup + Mathf.Max(0f, delay);
        }

        void Update()
        {
            if (rebuildAt > 0f && Time.realtimeSinceStartup >= rebuildAt)
            {
                rebuildAt = -1f;
                Rebuild();
            }

            if (pending == null || !pending.IsCompleted) return;

            var finished = pending;
            pending = null;

            if (finished.IsFaulted)
            {
                // Без этого исключение в чистом слое выглядит как «слой ничего не рисует» — молча и
                // навсегда.
                Debug.LogError($"[Горы] Счёт слоя сорвался: {finished.Exception?.GetBaseException()}");
                return;
            }
            if (finished.IsCanceled || pendingGeneration != generation) return;

            Apply(finished.Result);
        }

        /// <summary>Считается ЦЕЛИКОМ без UnityEngine — за этим и держится чистый слой.</summary>
        static List<MountainShape> Compute(List<MountainStroke> snapshot, MountainSettings settings)
        {
            var shapes = new List<MountainShape>();
            foreach (var blob in BlobBuilder.Group(snapshot))
                shapes.AddRange(MountainGeometry.Build(blob, settings));

            // Тон раздан внутри пятна, а порядок маляра — общий: гора южного массива обязана
            // закрывать гору северного, даже если они из разных пятен.
            MountainGeometry.SortForPainting(shapes);
            return shapes;
        }

        void Apply(List<MountainShape> shapes)
        {
            EnsureContainer();
            if (body == null) return;

            MountainMeshBuilder.Build(body.sharedMesh, shapes, Style());
            ClearPreview();
        }

        MountainPaintStyle Style()
        {
            if (useMapPalette)
                return MountainPaintStyle.FromPalette(Theme(), crestWidth, layerY, (byte)crestAlpha);

            return new MountainPaintStyle
            {
                Far = farColor,
                Near = nearColor,
                Ink = inkColor,
                CrestWidth = crestWidth,
                LayerY = layerY,
            };
        }

        /// <summary>Настройки для одного пересчёта. Признак суши берётся СНИМКОМ здесь, на главном
        /// потоке: считать будут в фоне, а спрашивать живую карту оттуда нельзя.</summary>
        MountainSettings BuildSettings() => new MountainSettings
        {
            IsLand = onlyOnLand ? Renderer()?.BuildLandProbe() : null,
            Radius = Mathf.Max(0.01f, mountainRadius),
            LinkFactor = linkLengthFactor,
            LengthJitter = lengthJitter,
            Anisotropy = anisotropy,
            Waist = waist,
            HeightFactor = heightFactor,
            Stretch = stretch,
            Tiers = tiers,
            EdgeHeight = edgeHeight,
            ToneSpan = toneSpan,
        };

        List<MountainStroke> Snapshot()
        {
            var copy = new List<MountainStroke>(strokes.Count);
            foreach (var stroke in strokes)
            {
                var clone = new MountainStroke { Id = stroke.Id, Radius = stroke.Radius, Erase = stroke.Erase };
                clone.Points.AddRange(stroke.Points);
                copy.Add(clone);
            }
            return copy;
        }

        // ── хозяйство ───────────────────────────────────────────────────────────────────────────

        void EnsureContainer()
        {
            if (container != null && body != null && preview != null) return;

            if (material == null)
            {
                var shader = Shader.Find("WorldGen/MountainPaint");
                if (shader == null)
                {
                    Debug.LogError("[Горы] Шейдер 'WorldGen/MountainPaint' не найден — проверь, что он в Project Settings → Graphics → Always Included Shaders.");
                    return;
                }
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            if (container == null)
            {
                var renderer = Renderer();
                Transform parent = renderer != null ? renderer.transform : transform;

                // Пересборка кода в редакторе обнуляет поля компонента, но объекты сцены переживает.
                // Не поискав старый слой, мы завели бы второй поверх первого — и «Убрать всё»
                // чистило бы только новый, а старый так и остался бы висеть на карте.
                var stale = parent.Find(ContainerName);
                if (stale != null) KillContainer(stale);

                var go = new GameObject(ContainerName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                go.SetActive(visible);
                container = go.transform;
            }

            body = body != null ? body : Part("Горы");
            preview = preview != null ? preview : Part("СледМазка");
        }

        MeshFilter Part(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(container, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return filter;
        }

        MapPaletteTheme Theme()
        {
            var renderer = Renderer();
            return renderer != null ? renderer.paletteTheme : MapPaletteTheme.ColdTwilight;
        }

        WorldMapRenderer Renderer()
        {
            if (mapRenderer == null) mapRenderer = GetComponent<WorldMapRenderer>();
            return mapRenderer;
        }

        Vector2 MapCenter()
        {
            var renderer = Renderer();
            return renderer == null
                ? new Vector2(250f, 250f)
                : new Vector2(renderer.mapWidth * 0.5f, renderer.mapHeight * 0.5f);
        }

        void OnValidate()
        {
            if (container != null) container.gameObject.SetActive(visible);
        }

        void OnDestroy()
        {
            if (container != null) KillContainer(container);
            if (material != null) Kill(material);
        }

        /// <summary>Сносит слой вместе с мешами: уничтожение объекта их НЕ забирает, а слой
        /// пересобирается за каждый мазок — иначе в памяти оседают мёртвые меши.</summary>
        static void KillContainer(Transform target)
        {
            foreach (var filter in target.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) Kill(filter.sharedMesh);
            Kill(target.gameObject);
        }

        static void Kill(Object target)
        {
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }

        // ── пробные мазки (кисти ещё нет) ───────────────────────────────────────────────────────

        [ContextMenu("Мазок: гряда по дуге")]
        public void TestRidge()
        {
            Vector2 c = MapCenter();
            float length = mountainRadius * 14f;
            var points = new List<Vec2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                points.Add(new Vec2(c.x - length * 0.5f + length * t,
                                    c.y + Mathf.Sin(t * Mathf.PI * 1.2f) * mountainRadius * 3f));
            }
            AddStroke(points, mountainRadius * 1.4f, false);
        }

        [ContextMenu("Мазок: широкий массив")]
        public void TestMassif()
        {
            Vector2 c = MapCenter();
            float length = mountainRadius * 8f;
            var points = new List<Vec2>
            {
                new Vec2(c.x - length * 0.5f, c.y - mountainRadius * 4f),
                new Vec2(c.x + length * 0.5f, c.y - mountainRadius * 4f),
            };
            AddStroke(points, mountainRadius * 3f, false);
        }

        [ContextMenu("Мазок: ластик поперёк")]
        public void TestEraser()
        {
            Vector2 c = MapCenter();
            var points = new List<Vec2>
            {
                new Vec2(c.x, c.y - mountainRadius * 6f),
                new Vec2(c.x, c.y + mountainRadius * 6f),
            };
            AddStroke(points, mountainRadius * 0.8f, true);
        }

        [ContextMenu("Убрать всё")]
        public void TestClear() => ClearStrokes();
    }
}
