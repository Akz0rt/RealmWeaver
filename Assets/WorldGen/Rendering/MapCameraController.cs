using UnityEngine;
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

        [Header("Настройки зума")]
        [Tooltip("Минимальный orthographicSize (максимальное приближение), доля от naturalFitSize.")]
        public float minSizeFraction = 0.15f;
        [Tooltip("Множитель за одно нажатие кнопки +/- в тулбаре.")]
        public float buttonZoomStep = 1.15f;
        [Tooltip("Чувствительность зума колесом мыши.")]
        public float scrollZoomSensitivity = 0.001f;

        [Header("Настройки пана")]
        [Tooltip("Множитель скорости пана относительно текущего orthographicSize.")]
        public float panSensitivity = 1.0f;
        [Tooltip("Насколько за пределы карты (в тех же мировых единицах) можно панить.")]
        public float panMargin = 50f;

        float naturalFitSize = -1f;
        Vector3 naturalFitPosition;
        bool dragging;
        Vector2 lastMousePos;

        public float NaturalFitSize
        {
            get
            {
                EnsureNaturalFitComputed();
                return naturalFitSize;
            }
        }

        public float CurrentZoomPercent
        {
            get
            {
                EnsureNaturalFitComputed();
                if (targetCamera == null || naturalFitSize <= 0f) return 100f;
                return naturalFitSize / targetCamera.orthographicSize * 100f;
            }
        }

        void EnsureNaturalFitComputed()
        {
            if (naturalFitSize > 0f || mapRenderer == null) return;
            naturalFitSize = Mathf.Max(mapRenderer.mapWidth, mapRenderer.mapHeight) * 0.5f;
            if (targetCamera != null) naturalFitPosition = targetCamera.transform.position;
        }

        void Update()
        {
            if (targetCamera == null) return;
            EnsureNaturalFitComputed();

            HandleScrollZoom();
            HandleRightMouseDragPan();
        }

        void HandleScrollZoom()
        {
            if (Mouse.current == null) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;
            ApplyZoomDelta(-scroll * scrollZoomSensitivity * targetCamera.orthographicSize);
        }

        void ApplyZoomDelta(float sizeDelta)
        {
            float minSize = naturalFitSize * minSizeFraction;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize + sizeDelta, minSize, naturalFitSize);
        }

        /// <summary>Called by MapToolbarUI's "-"/"+" buttons. Positive multiplier > 1 zooms out, &lt; 1 zooms in.</summary>
        public void ZoomBy(float multiplier)
        {
            EnsureNaturalFitComputed();
            float minSize = naturalFitSize * minSizeFraction;
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * multiplier, minSize, naturalFitSize);
        }

        /// <summary>Called by MapToolbarUI's "100%"/"По размеру" buttons.</summary>
        public void ResetZoom()
        {
            EnsureNaturalFitComputed();
            targetCamera.orthographicSize = naturalFitSize;
            targetCamera.transform.position = naturalFitPosition;
        }

        void HandleRightMouseDragPan()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
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
            EnsureNaturalFitComputed();
            float before = targetCamera.orthographicSize;

            targetCamera.orthographicSize = naturalFitSize * 10f; // way too big
            ApplyZoomDelta(0f); // triggers clamp via ZoomBy path instead
            ZoomBy(1f); // re-clamps at current (still-too-big) value
            bool clampedHigh = targetCamera.orthographicSize <= naturalFitSize + 0.001f;

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
