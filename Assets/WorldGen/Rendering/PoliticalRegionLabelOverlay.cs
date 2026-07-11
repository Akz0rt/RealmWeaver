using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Screen-space overlay for POLITICAL region-name labels (RegionData.Name), shown ONLY in
    /// MapDisplayMode.Region. Self-created by WorldMapRenderer.EnsurePoliticalLabelOverlay() - no scene
    /// wiring required (see that method's doc comment). Mirrors the world-anchor screen-space projection
    /// idiom used by the biome-zone label overlay (WorldGen.Rendering.RegionLabels.RegionLabelOverlay:
    /// world centroid -> Camera.WorldToScreenPoint every frame), but stripped of ALL its CRUD/edit-mode/
    /// zoom-LOD machinery - political region names are already editable via RegionsPanel's per-row name
    /// field (not by clicking a label on the map), and there is exactly one label per region, shown at a
    /// fixed size whenever Регионы mode is active (no fade-by-zoom).
    ///
    /// NOTE on naming: WorldGen.Rendering.RegionLabels.RegionLabelOverlay/RegionLabelManager name BIOME
    /// ZONES (continents/seas/biome-family patches) despite the "Region" in their name - a pre-existing
    /// naming collision in this codebase (see biome-matrix-branch-state memory). THIS class is for the
    /// actual user-owned political regions (WorldGen.Generation.RegionData via RegionManager) - the two
    /// label sets are mutually exclusive by display mode (see RegionLabelOverlay.LateUpdate's mode gate).</summary>
    public class PoliticalRegionLabelOverlay : MonoBehaviour
    {
        const float LabelYOffsetWorld = 0.5f;   // lift the world anchor slightly above the map plane
        static readonly Vector2 LabelSize = new Vector2(260f, 36f);

        WorldMapRenderer mapRenderer;
        Font builtinFont;
        RectTransform canvasRect;

        class LabelView { public RectTransform Container; public Vector3 World; }
        readonly Dictionary<int, LabelView> views = new Dictionary<int, LabelView>();

        /// <summary>Called once by WorldMapRenderer right after AddComponent - wires the back-reference and
        /// builds the overlay's own ScreenSpaceOverlay canvas (same idiom as RegionLabelOverlay.BuildCanvas,
        /// minus the EventSystem: this overlay is display-only, nothing here is clickable/draggable).</summary>
        public void Init(WorldMapRenderer renderer)
        {
            mapRenderer = renderer;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
        }

        void BuildCanvas()
        {
            var canvasGO = new GameObject("PoliticalRegionLabelCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;   // same band as the biome-label canvas - below all app chrome
            canvasGO.AddComponent<CanvasScaler>();
            canvasRect = canvasGO.GetComponent<RectTransform>();
        }

        /// <summary>Rebuilds the label set from mapRenderer.regionManager.Regions + RegionCentroids().
        /// Cheap (recreates at most a few dozen Text objects) - call after regions change (generate/add/
        /// delete/rename) and on display-mode switch; WorldMapRenderer's GenerateRegionsOnly/DeleteRegion/
        /// SetDisplayMode and RegionsPanel's add/rename all call this. No-op before Init or before a
        /// world/regionManager exists. A region with no land cells yet (just added, not yet painted) has
        /// no centroid and is skipped - it appears once the DM paints at least one land cell into it.</summary>
        public void Rebuild()
        {
            foreach (var lv in views.Values)
                if (lv?.Container != null) Destroy(lv.Container.gameObject);
            views.Clear();

            if (canvasRect == null || mapRenderer == null || mapRenderer.regionManager == null) return;

            var centroids = mapRenderer.RegionCentroids();
            foreach (var region in mapRenderer.regionManager.Regions)
            {
                if (!centroids.TryGetValue(region.Id, out var c)) continue;
                var world = new Vector3(c.X, LabelYOffsetWorld, c.Y);
                views[region.Id] = CreateLabelView(region.Name, world);
            }
        }

        LabelView CreateLabelView(string text, Vector3 world)
        {
            var go = new GameObject($"PoliticalRegionLabel_{text}");
            go.transform.SetParent(canvasRect, false);
            var txt = go.AddComponent<Text>();
            var container = go.GetComponent<RectTransform>();
            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.sizeDelta = LabelSize;

            txt.text = text;
            txt.font = builtinFont;
            txt.fontSize = 20;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.raycastTarget = false;
            ThemeService.Tag(txt, ThemeRole.Txt);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.04f, 0.05f, 0.06f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            return new LabelView { Container = container, World = world };
        }

        void LateUpdate()
        {
            if (canvasRect == null) return;
            bool show = mapRenderer != null && mapRenderer.displayMode == MapDisplayMode.Region && views.Count > 0;
            if (canvasRect.gameObject.activeSelf != show) canvasRect.gameObject.SetActive(show);
            if (!show) return;

            var cam = mapRenderer.targetCamera;
            if (cam == null) return;

            foreach (var lv in views.Values)
            {
                if (lv?.Container == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(lv.World);
                bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                if (lv.Container.gameObject.activeSelf != onScreen) lv.Container.gameObject.SetActive(onScreen);
                if (!onScreen) continue;
                lv.Container.anchoredPosition = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);
            }
        }
    }
}
