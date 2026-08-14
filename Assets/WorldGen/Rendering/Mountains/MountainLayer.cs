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
    /// Манера рисования — тушь (§13): объём держит линия, тело красится цветом карты под ним.
    ///
    /// ТРИ МЕСТА, ГДЕ СЛОЙ МОГ БЫ ЗАВИСНУТЬ, и что с ними сделано.
    /// 1. Счёт геометрии — в фоне, как и раньше.
    /// 2. Сборка меша была НА ГЛАВНОМ потоке. Теперь массивы вершин собираются там же, в фоне, а
    ///    на главном остаётся одна заливка в Mesh: обращаться к живому движку из фона нельзя, а
    ///    складывать Vector3 в список — можно.
    /// 3. Отмена пересчитывала ВСЕ массивы заново. Теперь посчитанное лежит в кэше по массивам:
    ///    ключ — набор клеток куска, и пересчитывается только тот кусок, чей набор изменился. Отмена
    ///    мазка трогает один-два массива из десятков.
    /// </summary>
    public class MountainLayer : MonoBehaviour
    {
        [Header("Источник")]
        [Tooltip("Если не назначено — ищется на этом же объекте.")]
        public WorldMapRenderer mapRenderer;

        [Header("Размер (§15 спеки)")]
        [Tooltip("R — радиус горы в мировых единицах карты. Задаёт и шаг слоёв 2R, и предельную полуширину массы. В манере туши горы должны быть КРУПНЫМИ: в мелкую не помещается ни жирная линия, ни крошка, и цепь вершин читается бахромой.")]
        public float mountainRadius = 10f;
        [Tooltip("T — целевая длина звена, в долях R.")]
        public float linkLengthFactor = 1.6f;
        [Tooltip("Разброс длин звеньев: 0.06 — шесть процентов.")]
        [Range(0f, 0.3f)] public float lengthJitter = 0.06f;
        [Tooltip("a — угол взгляда на землю: во столько раз вертикаль «дороже» при нарезке и во столько же сплющена подошва.")]
        public float anisotropy = 1.6f;

        [Header("Форма")]
        [Tooltip("t — талия на позвонке: доля полной полуширины на стыке звеньев.")]
        [Range(0.1f, 1f)] public float waist = 0.95f;
        [Tooltip("h — множитель высоты: H = h·w, где w — полуширина в середине звена.")]
        public float heightFactor = 2.2f;
        [Tooltip("k — растяжение подошвы вдоль оси. Больше — выше перевалы, плотнее массив.")]
        public float stretch = 1.9f;
        [Tooltip("Острота вершины: 0 — купол, 0.33 — синусоида, 0.66 — конус, 1 — шпиль.")]
        [Range(0f, 1f)] public float sharp = 0.66f;
        [Tooltip("Зубчатость гребня: выщербины на линии.")]
        [Range(0f, 0.3f)] public float jag = 0.03f;
        [Tooltip("Сколько слоёв массы различаем: слой 0 — гряда по краю массива.")]
        [Range(1, 4)] public int tiers = 3;
        [Tooltip("Высота горы у края массива как доля высоты в сердцевине.")]
        [Range(0.2f, 1f)] public float edgeHeight = 0.55f;

        [Header("Тушь (§13 спеки)")]
        [Tooltip("Жирность линии у вершины, в долях R.")]
        [Range(0f, 0.5f)] public float penWidth = 0.15f;
        [Tooltip("Сход линии на нет книзу. У подошвы толщина нулевая всегда — контур обрывается сам.")]
        [Range(0.3f, 4f)] public float penTaper = 1.1f;
        [Tooltip("Чернота линии, крошки и промоин.")]
        [Range(0f, 1f)] public float penAlpha = 0.61f;
        [Tooltip("Крошка в тени: зернистая штриховка теневой стороны.")]
        [Range(0f, 2f)] public float grit = 0.88f;
        [Tooltip("Насколько быстро крошка редеет вниз по склону.")]
        [Range(0f, 3f)] public float gritFall = 0.4f;
        [Tooltip("Промоины: редкие тонкие штрихи по освещённому склону.")]
        [Range(0f, 1f)] public float gully = 0.32f;
        [Tooltip("Откуда светит, в градусах. Одно направление на всю карту.")]
        [Range(0f, 360f)] public float lightAngle = 160f;
        [Tooltip("Насколько слой массы меняет густоту туши: внутренние слои жирнее линией и плотнее крошкой.")]
        [Range(0f, 0.8f)] public float tierInk = 0.5f;
        [Tooltip("Направление градации по слоям массы: 1 — сердцевина гуще, −1 — наоборот.")]
        [Range(-1f, 1f)] public float tierContrast = 1f;
        [Tooltip("Глубина: дальние горы бледнее. 0 — выключено (выбор ДМ).")]
        [Range(0f, 0.5f)] public float depthTone = 0f;

        [Header("Слой")]
        [Tooltip("Высота слоя над плоскостью карты. Берег 0.3, границы регионов 0.4, превью реки 0.45.")]
        public float layerY = 0.6f;
        [Tooltip("Показывать ли слой.")]
        public bool visible = true;
        [Tooltip("Горы только по суше (решение ДМ). Снять — можно рисовать и по воде.")]
        public bool onlyOnLand = true;
        [Tooltip("Сглаживание контура массива, в радиусах горы. Клетки карты стоят через 15 единиц, а гора — 10, поэтому без сглаживания контур выходит мозаикой. 0 — не сглаживать.")]
        [Range(0f, 1.5f)] public float maskSmoothing = 0.5f;

        [Header("Цвет")]
        [Tooltip("Брать краску туши из палитры карты (теневой горный тон, притемнённый). Снять — красить своим цветом ниже.")]
        public bool useMapPalette = true;
        [Tooltip("Своя краска туши, если палитра не используется.")]
        public Color inkColor = new Color(0.10f, 0.086f, 0.07f, 1f);

        const string ContainerName = "СлойГор";

        /// <summary>Высота, с которой клетка считается ГОРОЙ. То же число, по которому
        /// LandformClassifier отделяет Mountains от Hills, — специально одно, чтобы «группа рельефа»
        /// и «что нарисовано» не разъехались.</summary>
        public const float MountainElevation = 0.5f;

        Transform container;
        Material material;
        MeshFilter body;

        List<MountainShape> lastShapes;      // последний рисунок — чтобы пересобирать меш без счёта
        readonly MountainMeshData meshData = new MountainMeshData();
        Task<Batch> pending;
        int generation;
        int pendingGeneration;
        float rebuildAt = -1f;

        // Кэш посчитанных массивов: ключ — набор клеток куска. Пересчитывается только изменившееся.
        readonly Dictionary<ulong, List<MountainShape>> cache = new Dictionary<ulong, List<MountainShape>>();
        string cacheSignature;

        /// <summary>Итог одного пересчёта: и геометрия, и готовые массивы меша.</summary>
        sealed class Batch
        {
            public List<MountainShape> Shapes;
            public MountainMeshData Data;
            public Dictionary<ulong, List<MountainShape>> Cache;
        }

        /// <summary>Один связный кусок гор: набор многоугольников клеток и ключ этого набора.</summary>
        struct Massif
        {
            public ulong Key;
            public List<IReadOnlyList<Vec2>> Polygons;
        }

        // ── что рисовать: массивы из клеток карты ───────────────────────────────────────────────

        /// <summary>
        /// Снимок массивов на главном потоке: список многоугольников клеток по каждому связному
        /// куску. Именно снимок, а не ссылки на клетки, — считать будут в фоне, а клетки правит
        /// кисть.
        ///
        /// Заодно считается КЛЮЧ куска — свёртка номеров его клеток, не зависящая от порядка. По
        /// нему кусок узнаётся в кэше: отмена мазка меняет набор клеток одного-двух кусков, а
        /// остальные достаются готовыми. Раньше отмена пересчитывала всё и на большой карте
        /// подвисала.
        /// </summary>
        List<Massif> SnapshotMassifs()
        {
            var result = new List<Massif>();
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
                ulong key = 1469598103934665603ul;
                foreach (int id in piece)
                {
                    if (!byId.TryGetValue(id, out var cell)) continue;
                    polygons.Add(cell.Polygon);
                    key ^= Mix((ulong)id);   // XOR — свёртка не зависит от порядка обхода
                }
                if (polygons.Count > 0) result.Add(new Massif { Key = key, Polygons = polygons });
            }
            return result;
        }

        static ulong Mix(ulong v)
        {
            unchecked
            {
                v ^= v >> 33; v *= 0xff51afd7ed558ccdul;
                v ^= v >> 33; v *= 0xc4ceb9fe1a85ec53ul;
                return v ^ (v >> 33);
            }
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
            var style = Style();
            string signature = GeometrySignature(settings);
            if (signature != cacheSignature) { cache.Clear(); cacheSignature = signature; }
            var known = new Dictionary<ulong, List<MountainShape>>(cache);

            if (!Application.isPlaying)
            {
                pending = null;
                Apply(Compute(snapshot, settings, style, maskSmoothing, known, new MountainMeshData()));
                return;
            }

            pendingGeneration = generation;
            // Буфер меша в фоне — СВОЙ, а не общий: пока считается новый ответ, старый ещё может
            // заливаться в меш на главном потоке, и общий список порвался бы посередине.
            var buffer = new MountainMeshData();
            pending = Task.Run(() => Compute(snapshot, settings, style, maskSmoothing, known, buffer));
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

        /// <summary>
        /// Считается ЦЕЛИКОМ без обращений к живому движку — за этим и держится чистый слой, и
        /// поэтому же это можно звать из фона. Vector3 и Color32 здесь только складываются в
        /// списки: они обычные структуры.
        /// </summary>
        static Batch Compute(List<Massif> massifs, MountainSettings settings, MountainPaintStyle style,
                             float smoothing, Dictionary<ulong, List<MountainShape>> known,
                             MountainMeshData buffer)
        {
            var shapes = new List<MountainShape>();
            var fresh = new Dictionary<ulong, List<MountainShape>>(massifs.Count);

            foreach (var massif in massifs)
            {
                if (fresh.ContainsKey(massif.Key)) continue;   // два куска с одним набором клеток — один и тот же

                if (!known.TryGetValue(massif.Key, out var piece))
                {
                    piece = new List<MountainShape>();
                    // Шаг сетки берётся по горе: кисти, по чьей ширине его раньше подбирали, у
                    // массива из клеток нет вовсе.
                    float cell = MountainMask.ChooseCell(settings.Radius, settings.Radius);
                    var mask = MountainMask.FromPolygons(massif.Polygons, cell);
                    if (mask != null)
                    {
                        mask.Smooth(Mathf.RoundToInt(Mathf.Max(0f, smoothing) * settings.Radius / mask.Cell));
                        // Подрезка по суше — ПОСЛЕ сглаживания: иначе сглаживание вернуло бы массу
                        // за кромку воды, и хребет снова полез бы в море.
                        if (settings.IsLand != null) mask.ClipToLand(settings.IsLand);
                        piece.AddRange(MountainGeometry.BuildFromMask(mask, settings, out _));
                    }
                }
                fresh[massif.Key] = piece;
                shapes.AddRange(piece);
            }

            // Порядок маляра — ОБЩИЙ: гора южного массива обязана закрывать гору северного, даже
            // если они разные. Поэтому сортировка идёт после склейки всех кусков, а не внутри.
            MountainGeometry.SortForPainting(shapes);
            MountainMeshBuilder.BuildData(buffer, shapes, style, settings.Profile(), settings.Radius);
            return new Batch { Shapes = shapes, Data = buffer, Cache = fresh };
        }

        void Apply(Batch batch)
        {
            if (batch == null) return;
            lastShapes = batch.Shapes;
            if (batch.Cache != null)
            {
                cache.Clear();
                foreach (var pair in batch.Cache) cache[pair.Key] = pair.Value;
            }

            EnsureContainer();
            if (body == null) return;
            MountainMeshBuilder.Upload(body.sharedMesh, batch.Data);
        }

        /// <summary>
        /// Перекрасить, НЕ пересчитывая геометрию. Числа туши — дело стиля, а полный счёт большого
        /// массива стоит четверть секунды: ползунок черноты иначе дёргался бы вместо того, чтобы
        /// ехать. Сборка массивов меша при этом всё равно нужна, но она в разы дешевле счёта.
        /// </summary>
        public void Repaint()
        {
            if (lastShapes == null || lastShapes.Count == 0) return;
            EnsureContainer();
            if (body == null) return;

            var settings = BuildSettings();
            MountainMeshBuilder.BuildData(meshData, lastShapes, Style(), settings.Profile(), settings.Radius);
            MountainMeshBuilder.Upload(body.sharedMesh, meshData);
        }

        /// <summary>Сколько всего вышло в последнем рисунке — чтобы можно было сказать вслух, во что
        /// он обошёлся, и заметить, что крошка упёрлась в потолок.</summary>
        public string LastCost()
        {
            var data = meshData;
            return $"гор {data.Mountains}, вершин {data.Verts.Count}, треугольников {data.Tris.Count / 3}, "
                 + $"крошки {data.GritMarks}{(data.GritCapped ? " (упёрлась в потолок)" : "")}";
        }

        MountainPaintStyle Style()
        {
            var renderer = Renderer();
            var style = useMapPalette
                ? MountainPaintStyle.FromPalette(Theme())
                : new MountainPaintStyle { Ink = inkColor, FallbackBody = new Color32(120, 124, 128, 255) };

            style.Ground = renderer != null ? renderer.BuildGroundProbe() : null;
            style.PenAlpha = penAlpha;
            style.PenWidth = penWidth * Mathf.Max(0.01f, mountainRadius);
            style.PenTaper = penTaper;
            style.Grit = grit;
            style.GritFall = gritFall;
            style.Gully = gully;
            style.LightAngle = lightAngle;
            style.TierInk = tierInk;
            style.TierCount = tiers;
            style.TierContrast = tierContrast;
            style.DepthTone = depthTone;
            style.MapHeight = renderer != null ? renderer.mapHeight : 1f;
            style.LayerY = layerY;
            return style;
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
            Sharp = sharp,
            Jag = jag,
            Tiers = tiers,
            EdgeHeight = edgeHeight,
        };

        /// <summary>
        /// Отпечаток настроек, от которых зависит ГЕОМЕТРИЯ. Кэш массивов держится на нём: сдвинул
        /// ДМ радиус — весь кэш недействителен, потому что от радиуса зависит и маска, и оси, и
        /// звенья. Числа туши сюда не входят намеренно: они меняют только краску, и пересчитывать
        /// ради них нечего.
        ///
        /// Признак суши в отпечаток не попадает, и это оговорка, а не недосмотр: правка воды идёт
        /// через LandChanged, который роняет снимок и просит пересчёт, а вот КЭШ он не роняет.
        /// Поэтому воду учитываем отдельным номером снимка.
        /// </summary>
        string GeometrySignature(MountainSettings s)
            => $"{s.Radius}|{s.LinkFactor}|{s.LengthJitter}|{s.Anisotropy}|{s.Waist}|{s.HeightFactor}"
             + $"|{s.Stretch}|{s.Sharp}|{s.Jag}|{s.Tiers}|{s.EdgeHeight}|{maskSmoothing}|{onlyOnLand}"
             + $"|{LandStamp()}";

        /// <summary>Номер снимка суши. Меняется вместе с водой, поэтому кэш массивов от неё зависит
        /// честно, без сравнения самих снимков.</summary>
        int landStamp;
        public void LandSnapshotChanged() => landStamp++;
        int LandStamp() => landStamp;

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
            // Перекраска, а не пересчёт: правку геометрических чисел в инспекторе всё равно надо
            // подтвердить кнопкой пересчёта, а цвет туши видно сразу и стоит он копейки.
            Repaint();
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
