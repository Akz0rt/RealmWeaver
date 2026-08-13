using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WorldGen.Generation.Mountains;
using WorldGen.Rendering.MapRaster;
using Vec2 = System.Numerics.Vector2;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Слой гор: показывает то, что выросло из рельефа.
    ///
    /// Устройство простое: слой не хранит НИЧЕГО. Источник гор — рельеф (решение ДМ 2026-08-14):
    /// горами считаются клетки карты, у которых высота дотянула до MountainElevation, а рисунок —
    /// их декорация. Отсюда даром достаются слияние и разрезание массивов (это соседство клеток),
    /// подрезка по воде, отмена (обычная отмена правки клеток) и открытие проекта (клетки и так в
    /// файле).
    ///
    /// Считается всё в фоне, потому что счёт стоит десятки миллисекунд (а на исполинском массиве —
    /// сотни), и в кадр он не помещается. Пока идёт счёт, на карте уже видна правка САМОГО РЕЛЬЕФА:
    /// клетки перепеклись сразу, а рисунок догоняет. Отдельной ленты-превью поэтому нет — она была
    /// нужна, пока горы не меняли ничего, кроме себя.
    ///
    /// В РЕДАКТОРЕ, когда сцена не запущена, Update не тикает — и результат фонового счёта некому
    /// было бы забрать. Поэтому вне игры считаем прямо в вызове.
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
        [Tooltip("Сглаживание контура массива, в радиусах горы. Клетки карты стоят через 15 единиц, а гора — 10, поэтому без сглаживания контур выходит мозаикой. 0 — не сглаживать.")]
        [Range(0f, 1.5f)] public float maskSmoothing = 0.5f;

        [Header("Цвет")]
        [Tooltip("Брать концы тональной шкалы из палитры карты (слоты MtnL и MtnS). Снять — красить своими цветами ниже.")]
        public bool useMapPalette = true;
        public Color farColor = new Color(0.55f, 0.59f, 0.64f, 1f);
        public Color nearColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        public Color inkColor = new Color(0.94f, 0.93f, 0.89f, 0.35f);

        const string ContainerName = "СлойГор";

        /// <summary>Высота, с которой клетка считается ГОРОЙ. То же число, по которому
        /// LandformClassifier отделяет Mountains от Hills, — специально одно, чтобы «группа рельефа»
        /// и «что нарисовано» не разъехались.</summary>
        public const float MountainElevation = 0.5f;

        Transform container;
        Material material;
        MeshFilter body;

        Task<List<MountainShape>> pending;
        int generation;
        int pendingGeneration;
        float rebuildAt = -1f;

        // ── что рисовать: массивы из клеток карты ───────────────────────────────────────────────
        //
        // Слой НИЧЕГО не хранит. Источник гор — рельеф: клетки, у которых высота дотянула до
        // MountainElevation. Отсюда всё остальное даром: слияние и разрезание массивов — это
        // соседство клеток, подрезка по суше — вода просто не горная группа, отмена — обычная
        // отмена правки клеток, а открытие проекта не требует ни загрузки, ни формата, потому что
        // клетки в файле и так лежат.

        /// <summary>
        /// Снимок массивов на главном потоке: список многоугольников клеток по каждому связному
        /// куску. Именно снимок, а не ссылки на клетки, — считать будут в фоне, а клетки правит
        /// кисть.
        /// </summary>
        List<List<IReadOnlyList<Vec2>>> SnapshotMassifs()
        {
            var result = new List<List<IReadOnlyList<Vec2>>>();
            var renderer = Renderer();
            var cells = renderer != null ? renderer.Cells : null;
            if (cells == null || cells.Count == 0) return result;

            var byId = new Dictionary<int, WorldGen.Generation.VoronoiCell>(cells.Count);
            foreach (var cell in cells) byId[cell.Id] = cell;

            var mountainIds = new List<int>();
            foreach (var cell in cells)
            {
                if (cell.EffectiveIsOcean || cell.EffectiveIsLake) continue;
                if (cell.EffectiveElevation < MountainElevation) continue;
                if (cell.Polygon == null || cell.Polygon.Count < 3) continue;
                mountainIds.Add(cell.Id);
            }
            if (mountainIds.Count == 0) return result;

            foreach (var piece in ReliefMassifs.Split(mountainIds, id =>
                         byId.TryGetValue(id, out var c) ? c.NeighborIds : null))
            {
                var polygons = new List<IReadOnlyList<Vec2>>(piece.Count);
                foreach (int id in piece)
                    if (byId.TryGetValue(id, out var cell)) polygons.Add(cell.Polygon);
                if (polygons.Count > 0) result.Add(polygons);
            }
            return result;
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (container != null) container.gameObject.SetActive(value);
        }

        /// <summary>
        /// Пересчёт слоя. В игре уходит в фон, в редакторе считается на месте (там некому забрать
        /// результат). Устаревшие ответы отбрасываются по номеру поколения: правки идут пачками, и
        /// прийти ответы могут не по порядку.
        /// </summary>
        public void Rebuild()
        {
            EnsureContainer();
            generation++;

            var snapshot = SnapshotMassifs();
            var settings = BuildSettings();

            if (!Application.isPlaying)
            {
                pending = null;
                Apply(Compute(snapshot, settings, maskSmoothing));
                return;
            }

            pendingGeneration = generation;
            pending = Task.Run(() => Compute(snapshot, settings, maskSmoothing));
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
        static List<MountainShape> Compute(List<List<IReadOnlyList<Vec2>>> massifs,
                                           MountainSettings settings, float smoothing)
        {
            var shapes = new List<MountainShape>();
            foreach (var polygons in massifs)
            {
                // Шаг сетки берётся по горе: кисти, по чьей ширине его раньше подбирали, у массива
                // из клеток нет вовсе.
                float cell = MountainMask.ChooseCell(settings.Radius, settings.Radius);
                var mask = MountainMask.FromPolygons(polygons, cell);
                if (mask == null) continue;
                mask.Smooth(Mathf.RoundToInt(Mathf.Max(0f, smoothing) * settings.Radius / mask.Cell));
                // Подрезка по суше — ПОСЛЕ сглаживания: иначе сглаживание вернуло бы массу за
                // кромку воды, и хребет снова полез бы в море.
                if (settings.IsLand != null) mask.ClipToLand(settings.IsLand);
                shapes.AddRange(MountainGeometry.BuildFromMask(mask, settings, out _));
            }

            // Тон раздан внутри массива, а порядок маляра — общий: гора южного массива обязана
            // закрывать гору северного, даже если они разные.
            MountainGeometry.SortForPainting(shapes);
            return shapes;
        }

        void Apply(List<MountainShape> shapes)
        {
            EnsureContainer();
            if (body == null) return;

            MountainMeshBuilder.Build(body.sharedMesh, shapes, Style());
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

        // ── хозяйство ───────────────────────────────────────────────────────────────────────────

        void EnsureContainer()
        {
            if (container != null && body != null) return;

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
        /// пересобирается за каждую правку рельефа — иначе в памяти оседают мёртвые меши.</summary>
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

    }
}
