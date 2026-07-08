using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Кистевое выделение области карты для последующего применения override (climate/biome/water/...):
    /// - Зажать ЛКМ и водить - "закрашивает" область в радиусе кисти (как кисть рельефа) в выделение.
    /// - Новый мазок БЕЗ Shift - начинает выделение заново.
    /// - Новый мазок С Shift - добавляет область к уже выделенным (аккумуляция нескольких областей
    ///   последовательно, потом применяешь override ко всему набору разом).
    /// Радиус выделения - СВОЙ (selectionRadius, отдельный слайдер в панели), не от кисти рельефа.
    /// Под курсором рисуется кольцо-превью области, которая выделится.
    ///
    /// Выбранные клетки подсвечиваются отдельным полупрозрачным overlay-mesh (не трогает основную
    /// карту) - см. RebuildOverlay. Хит-тест - через TryGetSiteHitPoint (работает и в GPU-режиме).
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
        [Tooltip("If assigned, cell selection is suppressed when POI interaction controller has claimed the input.")]
        public PoiInteractionController poiController;
        [Tooltip("Радиус кисти выделения (мировые единицы) - НЕЗАВИСИМ от кисти рельефа, свой слайдер в панели.")]
        public float selectionRadius = 42f;
        [Tooltip("Высота кольца-курсора выделения над картой (Y).")]
        public float cursorHeight = 2f;
        public float cursorLineWidth = 1.6f;

        public float SelectionRadius { get => selectionRadius; set => selectionRadius = Mathf.Max(1f, value); }

        [Header("Внешний вид подсветки")]
        public Color selectionColor = new Color(1f, 1f, 0.2f, 0.45f);
        [Tooltip("Высота overlay-меша над поверхностью карты (Y), чтобы избежать z-fighting.")]
        public float overlayYOffset = 0.3f;

        readonly HashSet<VoronoiCell> selectedCells = new HashSet<VoronoiCell>();

        MeshFilter overlayMeshFilter;
        MeshRenderer overlayMeshRenderer;
        LineRenderer cursorRing;
        const int CircleSegments = 48;

        /// <summary>Срабатывает при любом изменении набора выбранных клеток - UI-панель override должна подписаться на это, чтобы знать текущий выбор.</summary>
        public event System.Action<IReadOnlyCollection<VoronoiCell>> OnSelectionChanged;

        bool isDragging;

        void Awake()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
            BuildOverlayObject();
            BuildCursor();
        }

        void Update()
        {
            if (poiController != null && poiController.InputConsumedThisFrame) return;
            if (mapRenderer == null || raycastCamera == null) return;
            if (Mouse.current == null) return; // нет подключённой мыши (например, в тестовой среде) - ничего не делаем

            bool shiftHeld = Keyboard.current != null &&
                              (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // Клик по UI не должен выделять клетки карты "сквозь" интерфейс.
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                isDragging = true;
                // Новый мазок без Shift - выделяем заново; с Shift - добавляем к прошлым областям.
                if (!shiftHeld) selectedCells.Clear();
                AddAreaUnderCursor();
                NotifyChanged();
            }
            else if (Mouse.current.leftButton.isPressed && isDragging)
            {
                // Тащим ЛКМ - продолжаем закрашивать область по пути (аккумулируем в текущий мазок).
                if (AddAreaUnderCursor()) NotifyChanged();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            // Кольцо-курсор: показываем область, которая выделится под курсором.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) HideCursor();
            else UpdateCursor();
        }

        /// <summary>Добавляет в выделение все клетки в радиусе выделения под курсором. true - если что-то добавилось.</summary>
        bool AddAreaUnderCursor()
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            if (!mapRenderer.TryGetSiteHitPoint(ray, out Vector2 site)) return false;

            bool any = false;
            foreach (var cell in BrushOps.CellsInRadius(mapRenderer.Cells, site.x, site.y, selectionRadius, square: false))
                if (selectedCells.Add(cell)) any = true;
            return any;
        }

        // ── Кольцо-курсор области выделения ──────────────────────────────────────
        void BuildCursor()
        {
            var go = new GameObject("SelectionCursor");
            go.transform.SetParent(mapRenderer != null ? mapRenderer.transform : transform, false);
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
            var c = new Color(selectionColor.r, selectionColor.g, selectionColor.b, 1f);
            cursorRing.startColor = c;
            cursorRing.endColor = c;
            cursorRing.enabled = false;
        }

        void UpdateCursor()
        {
            if (cursorRing == null || mapRenderer == null || raycastCamera == null || Mouse.current == null) return;
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = raycastCamera.ScreenPointToRay(mousePos);
            if (!mapRenderer.TryGetSiteHitPoint(ray, out Vector2 site)) { HideCursor(); return; }

            cursorRing.widthMultiplier = cursorLineWidth;
            cursorRing.positionCount = CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = (i / (float)CircleSegments) * Mathf.PI * 2f;
                cursorRing.SetPosition(i, new Vector3(
                    site.x + Mathf.Cos(a) * selectionRadius,
                    cursorHeight,
                    site.y + Mathf.Sin(a) * selectionRadius));
            }
            cursorRing.enabled = true;
        }

        void HideCursor()
        {
            if (cursorRing != null) cursorRing.enabled = false;
        }

        void OnDisable() => HideCursor();

        void OnDestroy()
        {
            if (overlayMeshFilter != null && overlayMeshFilter.mesh != null) Destroy(overlayMeshFilter.mesh);
            if (overlayMeshRenderer != null && overlayMeshRenderer.material != null) Destroy(overlayMeshRenderer.material);
            if (cursorRing != null && cursorRing.material != null) Destroy(cursorRing.material);
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

            // Уничтожаем предыдущий overlay-mesh перед заменой - иначе течём нативными Mesh
            // (RebuildOverlay вызывается на каждый кадр протяжки выделения).
            if (overlayMeshFilter.mesh != null) Destroy(overlayMeshFilter.mesh);
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
