using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Real orthographic zoom (scroll wheel + MapToolbarUI buttons) and pan (right-mouse-drag)
    /// for the map's Camera. Session-only state - never persisted, resets to fit-to-map on
    /// WorldMapRenderer.PositionCameraOverMap's one-time initial placement.
    /// </summary>
    public class MapCameraController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        public Camera targetCamera;

        /// <summary>Множитель к половине большей стороны карты для «По размеру» (100%).
        /// 0.5 = карта край-в-край; больше = запас по краям (карта не упирается в тулбар/грани).
        /// Тот же множитель применяет WorldMapRenderer.PositionCameraOverMap при первой установке.</summary>
        public const float FitFactor = 0.7f;

        [Header("Настройки зума")]
        [Tooltip("Минимальный orthographicSize (максимальное приближение), доля от naturalFitSize. Меньше = можно приблизить сильнее.")]
        public float minSizeFraction = 0.08f;
        [Tooltip("Максимальный orthographicSize (максимальное отдаление), доля от naturalFitSize. Больше = можно отдалить сильнее.")]
        public float maxSizeFraction = 3f;
        [Tooltip("Множитель за одно нажатие кнопки +/- в тулбаре.")]
        public float buttonZoomStep = 1.15f;
        [Tooltip("Доля изменения масштаба за один щелчок колеса (0.12 = 12% за щелчок).")]
        public float scrollZoomStep = 0.12f;

        [Header("Настройки пана")]
        [Tooltip("Множитель скорости пана относительно текущего orthographicSize.")]
        public float panSensitivity = 1.0f;
        [Tooltip("Насколько за пределы карты (в тех же мировых единицах) можно панить.")]
        public float panMargin = 50f;

        float naturalFitSize = 0f;
        Vector3 naturalFitPosition;
        bool dragging;
        Vector2 lastMousePos;

        public float NaturalFitSize => naturalFitSize;

        public float CurrentZoomPercent
        {
            get
            {
                if (targetCamera == null || naturalFitSize <= 0f) return 100f;
                return naturalFitSize / targetCamera.orthographicSize * 100f;
            }
        }

        void Awake()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated += ComputeNaturalFit;
        }

        void OnDestroy()
        {
            if (mapRenderer != null) mapRenderer.OnWorldRegenerated -= ComputeNaturalFit;
        }

        /// <summary>Recomputes the fit-to-map size/position from the just-regenerated map. Subscribed to
        /// WorldMapRenderer.OnWorldRegenerated, which fires right after PositionCameraOverMap() runs.
        /// Mirrors WorldMapRenderer.PositionCameraOverMap()'s formula directly instead of reading
        /// targetCamera.transform.position - that guarded method only repositions the camera once per
        /// session (cameraPlacedOnce), so on the second-and-later regenerations the live camera transform
        /// may be wherever the user last panned it, which would otherwise corrupt naturalFitPosition.</summary>
        void ComputeNaturalFit()
        {
            if (mapRenderer == null) return;
            float maxSide = Mathf.Max(mapRenderer.mapWidth, mapRenderer.mapHeight);
            naturalFitSize = maxSide * FitFactor;
            naturalFitPosition = new Vector3(mapRenderer.mapWidth * 0.5f, maxSide * 1.5f, mapRenderer.mapHeight * 0.5f);
        }

        void Update()
        {
            if (mapRenderer == null || targetCamera == null) return;

            HandleScrollZoom();
            HandleRightMouseDragPan();
        }

        void HandleScrollZoom()
        {
            if (Mouse.current == null) return;
            // Скролл над UI (панели, списки, попапы) крутит этот UI, а не зумит карту.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;
            // Sign-based step, independent of the platform's raw scroll magnitude (Windows reports
            // 120/notch, other setups 1/notch) so one notch always changes zoom by scrollZoomStep.
            // Scroll up (positive) = zoom in = smaller orthographicSize (multiplier < 1).
            float factor = scroll > 0f ? (1f - scrollZoomStep) : (1f + scrollZoomStep);
            ZoomBy(factor);
        }

        void ApplyZoomDelta(float sizeDelta)
        {
            if (naturalFitSize <= 0f) return;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize + sizeDelta,
                naturalFitSize * minSizeFraction, naturalFitSize * maxSizeFraction);
        }

        /// <summary>Called by MapToolbarUI's "-"/"+" buttons. Positive multiplier > 1 zooms out, &lt; 1 zooms in.</summary>
        public void ZoomBy(float multiplier)
        {
            if (naturalFitSize <= 0f) return;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * multiplier,
                naturalFitSize * minSizeFraction, naturalFitSize * maxSizeFraction);
        }

        /// <summary>Called by MapToolbarUI's "100%"/"По размеру" buttons.</summary>
        public void ResetZoom()
        {
            if (naturalFitSize <= 0f) return;
            targetCamera.orthographicSize = naturalFitSize;
            targetCamera.transform.position = naturalFitPosition;
        }

        void HandleRightMouseDragPan()
        {
            if (naturalFitSize <= 0f) return;
            if (Mouse.current == null) return;

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                // Начало пана — только если курсор не над UI (ПКМ по панели не должен таскать карту).
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                dragging = true;
                lastMousePos = Mouse.current.position.ReadValue();
                return;
            }
            if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                dragging = false;
                return;
            }
            if (!dragging) return;

            Vector2 currentMousePos = Mouse.current.position.ReadValue();
            Vector2 delta = currentMousePos - lastMousePos;
            lastMousePos = currentMousePos;

            // Camera looks straight down (Euler(90,0,0)) - screen X maps to world X, screen Y maps to world Z (inverted).
            float worldPerPixel = (targetCamera.orthographicSize * 2f / Screen.height) * panSensitivity;
            Vector3 move = new Vector3(-delta.x * worldPerPixel, 0f, -delta.y * worldPerPixel);

            Vector3 newPos = targetCamera.transform.position + move;
            float minX = -panMargin, maxX = mapRenderer.mapWidth + panMargin;
            float minZ = -panMargin, maxZ = mapRenderer.mapHeight + panMargin;
            newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
            newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);
            targetCamera.transform.position = newPos;
        }

        [ContextMenu("Self-Test: Zoom Clamp")]
        public void SelfTestZoomClamp()
        {
            if (targetCamera == null)
            {
                Debug.LogWarning("Self-Test Zoom Clamp: targetCamera is not assigned.");
                return;
            }

            float before = targetCamera.orthographicSize;

            targetCamera.orthographicSize = naturalFitSize * maxSizeFraction * 10f; // way too big
            ApplyZoomDelta(0f); // re-applies the clamp with a zero delta
            ZoomBy(1f); // re-clamps at current (still-too-big) value
            bool clampedHigh = targetCamera.orthographicSize <= naturalFitSize * maxSizeFraction + 0.001f;

            targetCamera.orthographicSize = 0.0001f; // way too small
            ZoomBy(1f);
            bool clampedLow = targetCamera.orthographicSize >= naturalFitSize * minSizeFraction - 0.001f;

            targetCamera.orthographicSize = before;
            Debug.Log(clampedHigh && clampedLow
                ? "Self-Test Zoom Clamp: PASS"
                : $"Self-Test Zoom Clamp: FAIL (clampedHigh={clampedHigh}, clampedLow={clampedLow})");
        }
    }
}
