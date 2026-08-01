using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Handles all mouse interaction with POI markers:
    /// - Click on marker → select (highlight + show panel).
    /// - Double-click on marker → open that place's page in the workspace (Task 10c Step 2a).
    /// - Click on empty map → deselect.
    /// - Drag marker → reposition; commits WorldPosition + OwnerCellId on mouse-up.
    ///
    /// Uses distance-based hit detection (no physics layers needed).
    /// Sets InputConsumedThisFrame = true when claiming input so CellSelectionController and
    /// BrushToolController skip. DefaultExecutionOrder(-100) guarantees this runs its Update()
    /// before those two on the very same press frame - otherwise, with unspecified script order,
    /// they could still see InputConsumedThisFrame == false on the first frame of a POI press
    /// and briefly select a cell / paint under the cursor before this claims the flag.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PoiInteractionController : MonoBehaviour
    {
        [Header("Dependencies")]
        public PoiManager poiManager;
        public WorldMapRenderer mapRenderer;
        public Camera raycastCamera;
        [Tooltip("Info popup shown on a single click of a POI.")]
        public PoiInfoPopup infoPopup;
        [Tooltip("Screen controller — a double-click opens the point's page in the workspace (Task 10c).")]
        public MapScreenController mapScreenController;

        [Header("Interaction settings")]
        [Tooltip("World-unit radius around a POI center that counts as a hit.")]
        public float selectRadius = 12f;
        [Tooltip("Screen pixels moved before a press becomes a drag instead of a click.")]
        public float dragThresholdPixels = 5f;

        public bool InputConsumedThisFrame { get; private set; }

        bool tracking;
        bool isDragging;
        string trackedPoiId;
        Vector2 pressScreenPos;
        readonly PoiClickTracker clickTracker = new PoiClickTracker();

        void Awake()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
        }

        void LateUpdate()
        {
            InputConsumedThisFrame = false; // reset after all Updates have run
        }

        void Update()
        {
            if (poiManager == null || raycastCamera == null) return;
            if (Mouse.current == null) return;

            // Esc снимает взвод размещения без создания точки.
            if (poiManager.PlacementArmed && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                poiManager.DisarmPlacement();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                OnPress();
            else if (Mouse.current.leftButton.isPressed && tracking)
                OnHeld();
            else if (Mouse.current.leftButton.wasReleasedThisFrame && tracking)
                OnRelease();
        }

        void OnPress()
        {
            // Ignore clicks that land on UI (edit fields, buttons, etc.) — otherwise clicking
            // into the name/description InputField reads as an empty-map click and deselects.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var mousePos = Mouse.current.position.ReadValue();
            var worldXZ = ProjectToMapPlane(mousePos);
            var hit = FindNearestPoi(worldXZ);

            if (hit != null)
            {
                // Выбор существующей точки в приоритете над размещением (даже если взвод активен).
                tracking = true;
                isDragging = false;
                trackedPoiId = hit.PoiId;
                pressScreenPos = mousePos;
                InputConsumedThisFrame = true;
                return;
            }

            if (poiManager.PlacementArmed)
            {
                // Клик по пустой клетке карты — создаём точку здесь и снимаем взвод.
                if (GetCellIdAt(mousePos) >= 0)
                {
                    poiManager.AddAt(worldXZ);
                    poiManager.DisarmPlacement();
                    InputConsumedThisFrame = true;
                }
                return; // взвод активен, но мимо карты — остаёмся взведёнными, не сбрасываем выбор
            }

            poiManager.DeselectAll();
            if (infoPopup != null) infoPopup.Hide();
        }

        void OnHeld()
        {
            InputConsumedThisFrame = true;

            var mousePos = Mouse.current.position.ReadValue();
            if (!isDragging)
            {
                float dist = Vector2.Distance(mousePos, pressScreenPos);
                if (dist < dragThresholdPixels) return;
                isDragging = true;
            }

            var worldXZ = ProjectToMapPlane(mousePos);
            var view = poiManager.GetMarkerView(trackedPoiId);
            if (view != null) view.SetVisualPosition(worldXZ);
        }

        void OnRelease()
        {
            InputConsumedThisFrame = true;
            var mousePos = Mouse.current.position.ReadValue();

            if (!isDragging)
            {
                poiManager.SelectPoi(trackedPoiId);
                bool isDouble = clickTracker.RegisterClick(trackedPoiId, Time.unscaledTime);
                var poi = poiManager.GetPoiById(trackedPoiId);
                if (poi != null)
                {
                    // Single click → select + info popup; double click → the PLACE itself, i.e. its editor
                    // (Task 10c Step 2a, retargeted by Task 10e).
                    //
                    // The single-click half is the spec's hard rule and is why this is a DOUBLE-click at all:
                    // «клик выделяет ... случайный клик не должен выбрасывать пользователя с карты». Before
                    // Task 10c a double-click took over the whole window with the POI editor, which was
                    // exactly that — being thrown out of the map. It now opens a tab, and in the OTHER pane
                    // when a split exists, so the map the user just clicked stays on screen either way.
                    //
                    // Task 10c opened the POI's PAGE here, because a page was what put the place in the
                    // navigator's «Мир». The DM ruled otherwise after using it — «двойное нажатие и выбор в
                    // навигаторе должен выдавать то же самое меню, именно оно соответствует точке интереса» —
                    // so this now reaches the SAME destination the info popup's «Редактировать» does
                    // (PoiInfoPopup.cs:307 → MapScreenController.OpenPoiEditor); OpenPoiFromMap differs from
                    // it only by opening beside the map rather than in front of it.
                    if (isDouble && mapScreenController != null) mapScreenController.OpenPoiFromMap(poi);
                    else if (infoPopup != null) infoPopup.Show(poi);
                }
            }
            else
            {
                var worldXZ = ProjectToMapPlane(mousePos);
                int newCellId = GetCellIdAt(mousePos);
                poiManager.MovePoiTo(trackedPoiId, worldXZ, newCellId);
            }

            tracking = false;
            isDragging = false;
            trackedPoiId = null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        System.Numerics.Vector2 ProjectToMapPlane(Vector2 screenPos)
        {
            var ray = raycastCamera.ScreenPointToRay(screenPos);
            float yTarget = poiManager.poiYOffset;
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return default;
            float t = (yTarget - ray.origin.y) / ray.direction.y;
            var world = ray.origin + ray.direction * t;
            if (mapRenderer != null)
            {
                var local = mapRenderer.transform.InverseTransformPoint(world);
                return new System.Numerics.Vector2(local.x, local.z);
            }
            return new System.Numerics.Vector2(world.x, world.z);
        }

        PoiMarkerView FindNearestPoi(System.Numerics.Vector2 xzPos)
        {
            PoiMarkerView best = null;
            float bestDist = selectRadius;
            foreach (var poi in poiManager.GetAllPois())
            {
                var delta = poi.WorldPosition - xzPos;
                float d = (float)System.Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = poiManager.GetMarkerView(poi.Id);
                }
            }
            return best;
        }

        int GetCellIdAt(Vector2 screenPos)
        {
            if (mapRenderer == null) return -1;
            var ray = raycastCamera.ScreenPointToRay(screenPos);
            var cell = mapRenderer.GetCellUnderRay(ray);
            return cell?.Id ?? -1;
        }

        // ── Self-tests (project convention: [ContextMenu] + Debug.Log PASS/FAIL) ──────

        [ContextMenu("Self-Test: Poi Click Tracker")]
        public void SelfTestPoiClickTracker()
        {
            var t = new PoiClickTracker { DoubleClickSeconds = 0.3f };
            bool a = t.RegisterClick("p1", 0.0f);   // first → false
            bool b = t.RegisterClick("p1", 0.2f);   // same, in window → true (double)
            bool c = t.RegisterClick("p1", 0.4f);   // after reset, first of new pair → false
            bool d = t.RegisterClick("p2", 0.5f);   // different id → false
            bool e = t.RegisterClick("p2", 1.5f);   // same id but out of window → false

            bool ok = !a && b && !c && !d && !e;
            Debug.Log(ok ? "Self-Test Poi Click Tracker: PASS"
                         : $"Self-Test Poi Click Tracker: FAIL (a={a},b={b},c={c},d={d},e={e})");
        }

        [ContextMenu("Self-Test: Poi Region Label")]
        public void SelfTestPoiRegionLabel()
        {
            System.Func<int, int> regionIdByCell = cell => cell == 5 ? 2 : -1;
            System.Func<int, string> regionNameById = rid => rid == 2 ? "Терра Умбра" : null;

            string named  = PoiRegionLabel.Resolve(5,  regionIdByCell, regionNameById); // "Терра Умбра"
            string noCell = PoiRegionLabel.Resolve(-1, regionIdByCell, regionNameById); // ""
            string noReg  = PoiRegionLabel.Resolve(9,  regionIdByCell, regionNameById); // "" (region -1)

            bool ok = named == "Терра Умбра" && noCell == "" && noReg == "";
            Debug.Log(ok ? "Self-Test Poi Region Label: PASS"
                         : $"Self-Test Poi Region Label: FAIL (named='{named}', noCell='{noCell}', noReg='{noReg}')");
        }
    }
}
