using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public enum BrushTool { Elevation, Temperature, Moisture }

    /// <summary>
    /// Режим "кисти" - применяет ОТНОСИТЕЛЬНОЕ изменение (+delta или -delta) к выбранному
    /// параметру клетки (elevation/temperature/moisture), пока зажата ЛКМ. При заходе на новую
    /// клетку delta применяется сразу (мгновенный отклик при движении кистью); пока курсор стоит
    /// на одной клетке - delta доливается каждые repeatInterval секунд (не зависит от FPS).
    /// Один проход кистью (от нажатия до отпускания ЛКМ) - одна Undo-операция, откатывающая
    /// разом все клетки, затронутые за этот проход (Ctrl+Z).
    ///
    /// Использует новый Input System (UnityEngine.InputSystem), как и CellSelectionController.
    ///
    /// Этот компонент работает НЕЗАВИСИМО от CellSelectionController - рекомендуется включать
    /// только один из двух режимов одновременно (либо выбор клеток для панели override, либо
    /// кисть), чтобы не было конфликта интерпретации кликов мыши. См. brushModeActive ниже.
    /// </summary>
    public class BrushToolController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        [Tooltip("Камера, с которой кастуется луч кисти. Если не назначено - используется Camera.main.")]
        public Camera raycastCamera;
        [Tooltip("If assigned, brush painting is suppressed when POI interaction controller has claimed the input (e.g. dragging a POI marker).")]
        public PoiInteractionController poiController;

        [Header("Настройки инструмента")]
        public BrushTool activeTool = BrushTool.Elevation;
        [Tooltip("Величина изменения за одно применение кисти к клетке (за один кадр наведения).")]
        public float brushStep = 0.02f;
        [Tooltip("true - увеличивает значение (+step), false - уменьшает (-step).")]
        public bool increaseMode = true;
        [Tooltip("Интервал в секундах между повторными применениями кисти, пока ЛКМ зажата на одной клетке без движения. Не зависит от FPS. 0.05 = 20 применений/сек.")]
        public float repeatInterval = 0.05f;

        [Header("Включение режима")]
        [Tooltip("Если выключено - компонент не реагирует на ввод вообще. Включай вручную при переключении в режим кисти (например, через UI-кнопку), чтобы не конфликтовать с CellSelectionController.")]
        public bool brushModeActive = false;

        bool isPainting;
        VoronoiCell lastPaintedCell; // клетка, к которой применяли последней - для мгновенного отклика при переходе на новую клетку
        float repeatTimer;           // накопленное время на текущей клетке для повторного применения по интервалу

        void Update()
        {
            if (!brushModeActive) return;
            if (poiController != null && poiController.InputConsumedThisFrame) return;
            if (mapRenderer == null) return;
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return;
            if (Mouse.current == null) return;

            HandleUndo();
            HandlePainting();
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

        void HandlePainting()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                isPainting = true;
                lastPaintedCell = null;
                mapRenderer.BeginBrushStroke();
                PaintAtCursor();
            }
            else if (Mouse.current.leftButton.isPressed && isPainting)
            {
                PaintAtCursor();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && isPainting)
            {
                isPainting = false;
                mapRenderer.EndBrushStroke();
            }
        }

        void PaintAtCursor()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            var cell = mapRenderer.GetCellUnderRay(ray);
            if (cell == null) return;

            if (cell != lastPaintedCell)
            {
                // Перешли на новую клетку - применяем сразу для мгновенного отклика при движении кистью
                // и сбрасываем таймер повтора.
                lastPaintedCell = cell;
                repeatTimer = 0f;
                ApplyDelta(cell);
                return;
            }

            // Курсор стоит на той же клетке - доливаем delta через фиксированные интервалы времени.
            // Накапливаем реальное время (Time.deltaTime), поэтому скорость изменения не зависит от FPS.
            // while с вычитанием интервала "догоняет" пропущенные применения при просадках кадров.
            float interval = Mathf.Max(repeatInterval, 0.0001f);
            repeatTimer += Time.deltaTime;
            while (repeatTimer >= interval)
            {
                repeatTimer -= interval;
                ApplyDelta(cell);
            }
        }

        void ApplyDelta(VoronoiCell cell)
        {
            float signedDelta = increaseMode ? brushStep : -brushStep;

            switch (activeTool)
            {
                case BrushTool.Elevation:
                    mapRenderer.BrushAdjustElevation(cell, signedDelta);
                    break;
                case BrushTool.Temperature:
                    mapRenderer.BrushAdjustTemperature(cell, signedDelta);
                    break;
                case BrushTool.Moisture:
                    mapRenderer.BrushAdjustMoisture(cell, signedDelta);
                    break;
            }
        }
    }
}
