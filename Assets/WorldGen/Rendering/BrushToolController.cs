using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Что редактирует кисть. Elevation/Temperature/Moisture — относительное изменение;
    /// Biome — прямая замена биома выбранным из палитры.</summary>
    public enum BrushTool { Elevation, Temperature, Moisture, Biome, Water, Region }

    /// <summary>Режим для Elevation/Temperature/Moisture. Для Biome не применяется.</summary>
    public enum BrushMode { Raise, Lower, Smooth }

    /// <summary>Форма отпечатка кисти.</summary>
    public enum BrushShape { Circle, Square }

    /// <summary>
    /// Radius-кисть: пока зажата ЛКМ, применяет изменение ко ВСЕМ клеткам в круге/квадрате
    /// заданного радиуса вокруг точки под курсором (а не к одной клетке, как раньше).
    /// Режимы:
    ///   • Поднять/Опустить — прибавляет ±(brushStep · Сила) к elevation/temperature/moisture каждой клетки.
    ///   • Сгладить — каждую клетку сдвигает на долю Силы к среднему между ней и её соседями.
    ///   • Биом — красит клетку канонической температурой/влажностью выбранной ячейки 5×5-матрицы,
    ///     из которых биом выводится классификацией (Сила не участвует).
    /// При заходе на новую точку изменение применяется сразу (мгновенный отклик при движении);
    /// пока курсор стоит на месте — доливается каждые repeatInterval секунд (не зависит от FPS).
    /// Один проход (нажатие→отпускание ЛКМ) — одна Undo-операция, откатывающая все затронутые
    /// клетки разом (Ctrl+Z). "Отменить всё" (кнопка панели) откатывает все мазки сессии сразу.
    ///
    /// Работает НЕЗАВИСИМО от CellSelectionController — включай только один режим одновременно
    /// (см. brushModeActive), чтобы не было конфликта интерпретации кликов мыши.
    /// </summary>
    public class BrushToolController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        [Tooltip("Камера, с которой кастуется луч кисти. Если не назначено — используется Camera.main.")]
        public Camera raycastCamera;
        [Tooltip("If assigned, brush painting is suppressed when POI interaction controller has claimed the input (e.g. dragging a POI marker).")]
        public PoiInteractionController poiController;

        [Header("Настройки инструмента")]
        public BrushTool activeTool = BrushTool.Elevation;
        public BrushMode mode = BrushMode.Raise;
        public BrushShape shape = BrushShape.Circle;
        [Tooltip("Радиус кисти в мировых единицах карты (то же пространство, что VoronoiCell.Site).")]
        public float brushRadius = 42f;
        [Tooltip("Базовая величина изменения за одно применение к клетке (до умножения на Силу).")]
        public float brushStep = 0.02f;
        [Range(0f, 1f)]
        [Tooltip("Сила кисти 0..1 — множитель дельты для Поднять/Опустить и доля усреднения для Сгладить. Для биома не используется.")]
        public float strength = 0.6f;
        [Tooltip("Выбранная клетка матрицы (темп-уровень, влажн-уровень) для режима 'Биом'. null — красить нечем.")]
        public (int t, int m)? selectedClimateCell = null;
        [Tooltip("Id региона для режима 'Регион' (устанавливается панелью регионов). -1 = стереть (снять клетку с региона).")]
        public int selectedRegionId = -1;
        [Tooltip("Интервал в секундах между повторными применениями, пока ЛКМ зажата на месте. Не зависит от FPS.")]
        public float repeatInterval = 0.05f;
        [Tooltip("Регион-кисть: если мазок покрыл >= этого % клеток озера, всё озеро отходит региону кисти.")]
        public int regionLakeClaimPercent = 30;

        [Header("Включение режима")]
        [Tooltip("Если выключено — компонент не реагирует на ввод. Включается при переключении в режим кисти.")]
        public bool brushModeActive = false;

        [Header("Превью области кисти")]
        [Tooltip("Толщина контура-превью в мировых единицах карты.")]
        public float cursorLineWidth = 1.6f;
        [Tooltip("Высота контура над поверхностью карты (локальные единицы), чтобы он рисовался поверх меша.")]
        public float cursorHeight = 2f;

        bool isPainting;
        Vector2 lastPaintSite;   // точка последнего применения в Site-координатах — для мгновенного отклика при движении
        bool hasLastPaintSite;
        float repeatTimer;
        readonly HashSet<int> steppedThisStroke = new HashSet<int>();
        readonly HashSet<VoronoiCell> biomeStrokeCells = new HashSet<VoronoiCell>();
        bool regionStrokeTouched;
        readonly HashSet<int> regionStrokeLakeCells = new HashSet<int>();   // lake cells a Region stamp passed over (for the 30% rule)
        readonly HashSet<int> waterStrokeCells = new HashSet<int>();        // cells a Water stamp touched (unify their lakes on release)

        LineRenderer cursorRing;
        const int CircleSegments = 48;

        void Update()
        {
            if (mapRenderer == null) { HideCursor(); return; }
            if (cursorRing == null) BuildCursor();

            if (!brushModeActive) { HideCursor(); return; }
            if (poiController != null && poiController.InputConsumedThisFrame) { HideCursor(); return; }
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) { HideCursor(); return; }
            if (Mouse.current == null) { HideCursor(); return; }

            // Курсор над UI (панель кисти, тулбар, меню, любой raycast-target) — не рисуем по карте
            // "сквозь" интерфейс и не показываем контур области под панелью.
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            HandleUndo();
            HandlePainting(pointerOverUI);

            if (pointerOverUI && !isPainting) HideCursor();
            else UpdateCursor();
        }

        void HandleUndo()
        {
            if (Keyboard.current == null) return;

            bool ctrlHeld = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (ctrlHeld && Keyboard.current.zKey.wasPressedThisFrame)
            {
                bool didUndo = mapRenderer.UndoLastBrushStroke();
                if (didUndo)
                    Debug.Log($"BrushToolController: отменён последний мазок. Осталось в истории: {mapRenderer.BrushUndoStackCount}.");
            }
        }

        void HandlePainting(bool pointerOverUI)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (pointerOverUI) return; // клик по UI не должен начинать мазок по карте
                isPainting = true;
                hasLastPaintSite = false;
                mapRenderer.BeginBrushStroke();
                steppedThisStroke.Clear();
                biomeStrokeCells.Clear();
                regionStrokeTouched = false;
                regionStrokeLakeCells.Clear();
                waterStrokeCells.Clear();
                PaintAtCursor();
            }
            else if (Mouse.current.leftButton.isPressed && isPainting)
            {
                PaintAtCursor();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && isPainting)
            {
                isPainting = false;
                if (biomeStrokeCells.Count > 0)
                {
                    mapRenderer.FinalizeBiomeStroke(biomeStrokeCells);
                    biomeStrokeCells.Clear();
                }
                if (regionStrokeTouched)
                {
                    // 30% rule: a lake the region stroke covered enough of joins the painted region wholesale.
                    mapRenderer.ClaimLakesByCoverage(regionStrokeLakeCells, selectedRegionId, regionLakeClaimPercent);
                    mapRenderer.RebuildBorders();
                    regionStrokeTouched = false;
                }
                if (waterStrokeCells.Count > 0)
                {
                    // Water edits changed lake topology: re-unify touched lakes to their majority land region.
                    mapRenderer.UnifyTouchedLakes(waterStrokeCells);
                    mapRenderer.RebuildBorders();
                }
                regionStrokeLakeCells.Clear();
                waterStrokeCells.Clear();
                mapRenderer.EndBrushStroke();
            }
        }

        void PaintAtCursor()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            if (!mapRenderer.TryGetSiteHitPoint(ray, out Vector2 site)) return;

            // В режиме биома без выбранного биома красить нечем — no-op (не ошибка).
            if (activeTool == BrushTool.Biome && selectedClimateCell == null) return;

            // Перешли на заметно новую точку — применяем сразу (мгновенный отклик), сбрасываем таймер.
            const float moveEpsilonSqr = 0.0001f;
            if (!hasLastPaintSite || (site - lastPaintSite).sqrMagnitude > moveEpsilonSqr)
            {
                hasLastPaintSite = true;
                lastPaintSite = site;
                repeatTimer = 0f;
                ApplyStamp(site);
                return;
            }

            // Курсор стоит на месте — доливаем через фиксированные интервалы реального времени.
            float interval = Mathf.Max(repeatInterval, 0.0001f);
            repeatTimer += Time.deltaTime;
            while (repeatTimer >= interval)
            {
                repeatTimer -= interval;
                ApplyStamp(site);
            }
        }

        void ApplyStamp(Vector2 site)
        {
            var affected = BrushOps.CellsInRadius(
                mapRenderer.Cells, site.x, site.y, brushRadius, shape == BrushShape.Square);
            if (affected.Count == 0) return;

            if (activeTool == BrushTool.Biome)
            {
                var (t, m) = selectedClimateCell.Value;
                foreach (var cell in affected)
                {
                    mapRenderer.BrushSetClimateLevelsPreview(cell, t, m);
                    biomeStrokeCells.Add(cell);
                }
                mapRenderer.RebakeAffectedCells(affected);
                return;
            }

            if (activeTool == BrushTool.Water)
            {
                // Океан = Raise, Суша = Lower (сегмент режима переименован в EditorBrushPanel).
                bool makeLand = mode == BrushMode.Lower;
                foreach (var cell in affected)
                    mapRenderer.BrushSetWater(cell, makeLand);
                mapRenderer.RebakeAffectedCells(affected);
                foreach (var cell in affected) waterStrokeCells.Add(cell.Id);
                return;
            }

            if (activeTool == BrushTool.Region)
            {
                foreach (var cell in affected)
                {
                    mapRenderer.BrushSetRegion(cell, selectedRegionId);  // skips water internally
                    if (cell.EffectiveIsLake) regionStrokeLakeCells.Add(cell.Id);
                }
                mapRenderer.RebakeAffectedCells(affected);   // updates GPU regionId slot-B (Regions-mode fill)
                regionStrokeTouched = true;                   // flag → RebuildBorders on release
                return;
            }

            if (mode == BrushMode.Smooth)
            {
                ApplySmooth(affected);
                mapRenderer.RebakeAffectedCells(affected);
                return;
            }

            int dir = mode == BrushMode.Raise ? +1 : -1;
            if (activeTool == BrushTool.Elevation)
            {
                float signedDelta = dir * brushStep * strength;
                if (signedDelta != 0f)
                    foreach (var cell in affected) mapRenderer.BrushAdjustElevation(cell, signedDelta);
            }
            else // Temperature / Moisture — one level step per cell per stroke
            {
                foreach (var cell in affected)
                {
                    if (!steppedThisStroke.Add(cell.Id)) continue; // already stepped this stroke
                    if (activeTool == BrushTool.Temperature) mapRenderer.BrushStepTemperature(cell, dir);
                    else if (activeTool == BrushTool.Moisture) mapRenderer.BrushStepMoisture(cell, dir);
                }
            }
            mapRenderer.RebakeAffectedCells(affected);
        }

        void ApplySmooth(List<VoronoiCell> affected)
        {
            if (activeTool == BrushTool.Temperature || activeTool == BrushTool.Moisture)
            {
                foreach (var cell in affected)
                {
                    if (!steppedThisStroke.Add(cell.Id)) continue;
                    bool temp = activeTool == BrushTool.Temperature;
                    int cur = BiomeMatrix.Level5(temp ? cell.EffectiveTemperature : cell.EffectiveMoisture);
                    int target = BrushOps.SmoothedLevel(cur, NeighborLevels(cell, temp), strength);
                    if (temp) mapRenderer.SetTemperatureLevel(cell, target);
                    else      mapRenderer.SetMoistureLevel(cell, target);
                }
                return;
            }

            // Elevation: continuous smoothing (unchanged logic).
            var deltas = new List<(VoronoiCell cell, float delta)>(affected.Count);
            foreach (var cell in affected)
            {
                float current = cell.EffectiveElevation;
                float target = BrushOps.SmoothedValue(current, ElevationNeighborValues(cell), strength);
                deltas.Add((cell, target - current));
            }
            foreach (var (cell, delta) in deltas)
                if (delta != 0f) mapRenderer.BrushAdjustElevation(cell, delta);
        }

        IEnumerable<int> NeighborLevels(VoronoiCell cell, bool temp)
        {
            foreach (int nid in cell.NeighborIds)
            {
                var n = mapRenderer.GetCellById(nid);
                if (n != null) yield return BiomeMatrix.Level5(temp ? n.EffectiveTemperature : n.EffectiveMoisture);
            }
        }

        IEnumerable<float> ElevationNeighborValues(VoronoiCell cell)
        {
            foreach (int nid in cell.NeighborIds)
            {
                var n = mapRenderer.GetCellById(nid);
                if (n != null) yield return n.EffectiveElevation;
            }
        }

        // ── Превью области кисти (контур под курсором, как в графредакторах) ──────

        void BuildCursor()
        {
            var go = new GameObject("BrushCursor");
            go.transform.SetParent(mapRenderer.transform, false); // локальные координаты = пространство Site
            cursorRing = go.AddComponent<LineRenderer>();
            cursorRing.useWorldSpace = false;
            cursorRing.loop = true;
            cursorRing.widthMultiplier = cursorLineWidth;
            cursorRing.numCornerVertices = 0;
            cursorRing.numCapVertices = 0;
            cursorRing.material = new Material(Shader.Find("Sprites/Default"));
            cursorRing.textureMode = LineTextureMode.Stretch;
            cursorRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cursorRing.receiveShadows = false;
            cursorRing.enabled = false;
        }

        void UpdateCursor()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            if (!mapRenderer.TryGetSiteHitPoint(ray, out Vector2 site))
            {
                HideCursor();
                return;
            }

            Color c = ThemeService.Get(ThemeRole.Accent);
            cursorRing.startColor = c;
            cursorRing.endColor = c;
            cursorRing.widthMultiplier = cursorLineWidth;

            if (shape == BrushShape.Square)
            {
                cursorRing.positionCount = 4;
                cursorRing.SetPosition(0, new Vector3(site.x - brushRadius, cursorHeight, site.y - brushRadius));
                cursorRing.SetPosition(1, new Vector3(site.x + brushRadius, cursorHeight, site.y - brushRadius));
                cursorRing.SetPosition(2, new Vector3(site.x + brushRadius, cursorHeight, site.y + brushRadius));
                cursorRing.SetPosition(3, new Vector3(site.x - brushRadius, cursorHeight, site.y + brushRadius));
            }
            else
            {
                cursorRing.positionCount = CircleSegments;
                for (int i = 0; i < CircleSegments; i++)
                {
                    float a = (i / (float)CircleSegments) * Mathf.PI * 2f;
                    cursorRing.SetPosition(i, new Vector3(
                        site.x + Mathf.Cos(a) * brushRadius,
                        cursorHeight,
                        site.y + Mathf.Sin(a) * brushRadius));
                }
            }

            cursorRing.enabled = true;
        }

        void HideCursor()
        {
            if (cursorRing != null) cursorRing.enabled = false;
        }

        void OnDisable() => HideCursor();

        // ── Self-tests (this project's convention: [ContextMenu] + Debug.Log PASS/FAIL) ──────────

        [ContextMenu("Self-Test: Brush Radius Query")]
        public void SelfTestRadiusQuery()
        {
            var cells = new List<VoronoiCell>
            {
                new VoronoiCell(0, new System.Numerics.Vector2(0f, 0f)),  // центр
                new VoronoiCell(1, new System.Numerics.Vector2(3f, 0f)),  // внутри круга и квадрата r=5
                new VoronoiCell(2, new System.Numerics.Vector2(4f, 4f)),  // dist≈5.66 — вне круга r=5, внутри квадрата
                new VoronoiCell(3, new System.Numerics.Vector2(10f, 0f)), // вне обоих
            };

            var circle = BrushOps.CellsInRadius(cells, 0f, 0f, 5f, square: false);
            var square = BrushOps.CellsInRadius(cells, 0f, 0f, 5f, square: true);

            bool circleOk = circle.Count == 2 && circle.Exists(c => c.Id == 0) && circle.Exists(c => c.Id == 1);
            bool squareOk = square.Count == 3 && square.Exists(c => c.Id == 2) && !square.Exists(c => c.Id == 3);

            Debug.Log(circleOk && squareOk
                ? "Self-Test Brush Radius Query: PASS"
                : $"Self-Test Brush Radius Query: FAIL (circle={circle.Count}, square={square.Count})");
        }

        [ContextMenu("Self-Test: Smooth Averaging")]
        public void SelfTestSmoothAveraging()
        {
            // current=1, соседи {0,0} → среднее(1,0,0)=1/3.
            float full = BrushOps.SmoothedValue(1f, new[] { 0f, 0f }, 1f);      // = 1/3
            float half = BrushOps.SmoothedValue(1f, new[] { 0f, 0f }, 0.5f);    // = 1 + (1/3-1)*0.5 = 2/3
            float none = BrushOps.SmoothedValue(1f, new[] { 0f, 0f }, 0f);      // = 1
            float noNeighbors = BrushOps.SmoothedValue(0.7f, new float[0], 1f); // = 0.7 (среднее = само значение)

            bool ok = Mathf.Abs(full - (1f / 3f)) < 1e-5f
                      && Mathf.Abs(half - (2f / 3f)) < 1e-5f
                      && Mathf.Abs(none - 1f) < 1e-5f
                      && Mathf.Abs(noNeighbors - 0.7f) < 1e-5f;

            Debug.Log(ok
                ? "Self-Test Smooth Averaging: PASS"
                : $"Self-Test Smooth Averaging: FAIL (full={full}, half={half}, none={none}, noNeighbors={noNeighbors})");
        }

        [ContextMenu("Self-Test: Level Smooth")]
        public void SelfTestLevelSmooth()
        {
            bool ok = true;
            ok &= BrushOps.SmoothedLevel(4, new[] { 0, 0 }, 1f) == 0;   // full pull to average (0)
            ok &= BrushOps.SmoothedLevel(4, new[] { 2, 2 }, 1f) == 2;   // to neighbour level
            ok &= BrushOps.SmoothedLevel(4, new[] { 0, 0 }, 0f) == 4;   // strength 0 → unchanged
            ok &= BrushOps.SmoothedLevel(2, new int[0], 1f) == 2;       // no neighbours → unchanged
            Debug.Log(ok ? "Self-Test Level Smooth: PASS" : "Self-Test Level Smooth: FAIL");
        }
    }
}
