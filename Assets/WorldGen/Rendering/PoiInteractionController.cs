using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Handles all mouse interaction with POI markers:
    /// - Click on marker → select (highlight + show panel).
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
                tracking = true;
                isDragging = false;
                trackedPoiId = hit.PoiId;
                pressScreenPos = mousePos;
                InputConsumedThisFrame = true;
            }
            else
            {
                poiManager.DeselectAll();
            }
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
    }
}
