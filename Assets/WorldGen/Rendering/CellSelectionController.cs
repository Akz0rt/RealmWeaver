using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Позволяет выбирать клетки карты мышкой для последующего применения climate override:
    /// - Обычный клик (без Shift) - сбрасывает выбор, выбирает только клетку под курсором.
    /// - Клик с зажатым Shift - toggle клетки (добавляет, если не была выбрана; убирает, если была) - мультивыбор.
    /// - Drag без Shift (зажата ЛКМ + движение мыши) - добавляет каждую новую клетку под курсором по пути,
    ///   то есть "рисование" выбора по площади.
    ///
    /// Выбранные клетки подсвечиваются отдельным полупрозрачным overlay-mesh (не трогает vertex
    /// colors основной карты) - см. RebuildOverlay.
    ///
    /// ИСПОЛЬЗУЕТ НОВЫЙ INPUT SYSTEM (UnityEngine.InputSystem), не legacy UnityEngine.Input.
    /// Если в проекте Active Input Handling (Project Settings -> Player -> Other Settings)
    /// стоит "Input Manager (Old)" вместо "Input System Package (New)" или "Both" - этот
    /// скрипт не будет работать, нужно переключить на New или Both.
    /// </summary>
    public class CellSelectionController : MonoBehaviour
    {
        [Header("Источники")]
        public WorldMapRenderer mapRenderer;
        [Tooltip("Камера, с которой кастуется луч выбора. Если не назначено - используется Camera.main.")]
        public Camera raycastCamera;

        [Header("Внешний вид подсветки")]
        public Color selectionColor = new Color(1f, 1f, 0.2f, 0.45f);
        [Tooltip("Высота overlay-меша над поверхностью карты (Y), чтобы избежать z-fighting.")]
        public float overlayYOffset = 0.3f;

        readonly HashSet<VoronoiCell> selectedCells = new HashSet<VoronoiCell>();

        MeshFilter overlayMeshFilter;
        MeshRenderer overlayMeshRenderer;

        /// <summary>Срабатывает при любом изменении набора выбранных клеток - UI-панель override должна подписаться на это, чтобы знать текущий выбор.</summary>
        public event System.Action<IReadOnlyCollection<VoronoiCell>> OnSelectionChanged;

        bool isDragging;

        void Awake()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
            BuildOverlayObject();
        }

        void Update()
        {
            if (mapRenderer == null || raycastCamera == null) return;
            if (Mouse.current == null) return; // нет подключённой мыши (например, в тестовой среде) - ничего не делаем

            bool shiftHeld = Keyboard.current != null &&
                              (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                isDragging = true;
                var cell = RaycastCell();
                if (cell == null) return;

                if (shiftHeld)
                    ToggleCell(cell);
                else
                {
                    selectedCells.Clear();
                    selectedCells.Add(cell);
                    NotifyChanged();
                }
            }
            else if (Mouse.current.leftButton.isPressed && isDragging && !shiftHeld)
            {
                // Drag-рисование - добавляет клетки по пути, не убирает уже выбранные (Shift здесь не участвует,
                // т.к. зажатый Shift во время drag означал бы неоднозначное поведение - drag всегда добавляет).
                var cell = RaycastCell();
                if (cell != null && selectedCells.Add(cell))
                    NotifyChanged();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }
        }

        VoronoiCell RaycastCell()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            return mapRenderer.GetCellUnderRay(ray);
        }

        void ToggleCell(VoronoiCell cell)
        {
            if (selectedCells.Contains(cell))
                selectedCells.Remove(cell);
            else
                selectedCells.Add(cell);

            NotifyChanged();
        }

        /// <summary>Полностью очищает текущий выбор (не трогает climate override - только визуальное выделение).</summary>
        public void ClearSelection()
        {
            if (selectedCells.Count == 0) return;
            selectedCells.Clear();
            NotifyChanged();
        }

        public IReadOnlyCollection<VoronoiCell> GetSelectedCells() => selectedCells;

        void NotifyChanged()
        {
            RebuildOverlay();
            OnSelectionChanged?.Invoke(selectedCells);
        }

        void BuildOverlayObject()
        {
            var overlayGO = new GameObject("SelectionOverlay");

            // ВАЖНО: overlay должен быть child именно mapRenderer.transform (карты), а не transform
            // этого контроллера (CellSelectionController может быть отдельным GameObject где угодно
            // в сцене). ToWorldPos ниже уже вычисляет абсолютные мировые координаты - если overlay
            // оказывается child другого объекта с ненулевым transform, координаты применяются дважды,
            // и подсветка визуально оказывается смещена от реальной карты.
            Transform parentTransform = mapRenderer != null ? mapRenderer.transform : transform;
            overlayGO.transform.SetParent(parentTransform, false);

            overlayMeshFilter = overlayGO.AddComponent<MeshFilter>();
            overlayMeshRenderer = overlayGO.AddComponent<MeshRenderer>();

            // Прозрачный unlit-материал через встроенный шейдер - поддерживает alpha blending без необходимости
            // подключать собственный .shader файл специально для overlay.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = selectionColor;
            overlayMeshRenderer.material = mat;
        }

        /// <summary>Перестраивает overlay-mesh под текущий набор выбранных клеток - та же fan-триангуляция, что и в WorldMapRenderer.BuildMesh, но с единым полупрозрачным цветом.</summary>
        void RebuildOverlay()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            foreach (var cell in selectedCells)
            {
                if (cell.Polygon.Count < 3) continue;

                int baseIndex = vertices.Count;
                vertices.Add(ToWorldPos(cell.Site));

                foreach (var p in cell.Polygon)
                    vertices.Add(ToWorldPos(p));
                vertices.Add(ToWorldPos(cell.Polygon[0])); // дублированная замыкающая вершина, как в основном рендере

                int vertCountInFan = cell.Polygon.Count + 2;
                var fanTris = PolygonTriangulator.TriangulateFan(vertCountInFan);

                for (int i = 0; i < fanTris.Length; i += 3)
                {
                    triangles.Add(baseIndex + fanTris[i]);
                    triangles.Add(baseIndex + fanTris[i + 2]);
                    triangles.Add(baseIndex + fanTris[i + 1]);
                }
            }

            var mesh = new Mesh();
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            overlayMeshFilter.mesh = mesh;
        }

        /// <summary>
        /// Возвращает координаты вершины ЛОКАЛЬНО относительно mapRenderer.transform (не абсолютные
        /// мировые) - overlay-объект является child именно mapRenderer.transform (см. BuildOverlayObject),
        /// поэтому Unity сама применит родительский Transform при рендере. Если бы здесь возвращались
        /// уже-мировые координаты (как было раньше), родительский transform применился бы ВТОРОЙ раз,
        /// визуально смещая подсветку от реальной карты.
        /// </summary>
        Vector3 ToWorldPos(System.Numerics.Vector2 p)
        {
            return new Vector3(p.X, overlayYOffset, p.Y);
        }
    }
}
