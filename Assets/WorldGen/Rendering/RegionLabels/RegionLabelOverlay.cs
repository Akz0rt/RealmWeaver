using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Screen-space overlay for region labels. Task 4: one TMP text per label, projected from its
    /// world centroid each frame, alpha driven by camera zoom (visible zoomed-out, fades in when zoomed in).
    /// Task 5 adds EDITING: each label view is now a transparent clickable/draggable container (invisible
    /// Image raycast target + pointer handler) with the TMP as a stretched child. Click selects → inline
    /// rename box + "×" delete; drag moves the label on the y=0 map plane; add-mode drops a new label where
    /// you click. Render/LOD math is unchanged — it is just retargeted from the TMP to its container.</summary>
    public class RegionLabelOverlay : MonoBehaviour
    {
        [Header("Источники")]
        public RegionLabelManager manager;
        public MapCameraController cameraController;
        public TMP_FontAsset labelFont;

        [Header("LOD (доли от NaturalFitSize)")]
        [Range(0f,1f)]   public float nearFrac = 0.35f;    // ниже -> приближено, всё скрыто
        [Range(0f,1.5f)] public float farFrac = 0.6f;      // биомы полностью видны от этого
        [Range(0.5f,3f)] public float macroLoFrac = 1.3f;  // отсюда биомы гаснут, материк/моря появляются
        [Range(0.5f,3f)] public float macroHiFrac = 1.8f;  // выше -> только материк/моря
        public float baseFontSize = 34f;
        public float labelYOffsetWorld = 0.5f;         // приподнять точку привязки над картой

        /// <summary>When true, the next map click (not over UI) drops a new label there. Task 6 wires a button.</summary>
        public bool addMode;

        /// <summary>Task 4 (edit-mode gate): when false (default) labels are display-only — their click
        /// Image.raycastTarget is off so they never intercept the cursor (fixes scroll-zoom being blocked
        /// while hovering a label), and all click/drag/add/deselect entry points no-op. Toggled via
        /// SetEditMode, the public API MapLayersPanel (Task 5) calls from its edit-mode button.</summary>
        bool editMode = false;

        bool visible = true;
        RectTransform canvasRect;
        Font builtinFont;

        // Per-label view: a clickable/draggable container holding the TMP text as a stretched child.
        readonly Dictionary<string, LabelView> views = new Dictionary<string, LabelView>();

        Material labelMat;   // shared outline + underlay (soft-shadow) material for all label TMPs
        Color labelColor = new Color(0.97f, 0.96f, 0.92f, 1f);   // text color (near-white); DM-changeable via SetLabelColor
        readonly List<(LabelView lv, Vector2 pos, float a)> cullBuffer = new List<(LabelView, Vector2, float)>();
        readonly List<Rect> placedRects = new List<Rect>();

        // Active inline-edit UI (rename field + "×" delete), tracking the selected label. Null when none.
        RectTransform editRoot;
        InputField editField;
        string editId;

        void Awake()
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();          // ScreenSpaceOverlay canvas + EventSystem; store canvasRect
            if (manager != null)
            {
                manager.OnLabelsChanged += Rebuild;
                manager.OnSelectionChanged += HandleSelectionChanged;
            }
            Rebuild();
        }

        void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnLabelsChanged -= Rebuild;
                manager.OnSelectionChanged -= HandleSelectionChanged;
            }
            if (labelMat != null) Destroy(labelMat);   // free the shared outline+underlay material instance
        }

        public void SetVisible(bool on) { visible = on; if (canvasRect != null) canvasRect.gameObject.SetActive(on); }

        /// <summary>Sets the shared text color for all region labels (DM color picker in the layers panel).
        /// Alpha is driven per-frame by the LOD, so only RGB is applied here.</summary>
        public void SetLabelColor(Color c)
        {
            labelColor = new Color(c.r, c.g, c.b, 1f);
            foreach (var lv in views.Values)
                if (lv != null && lv.Tmp != null)
                {
                    float a = lv.Tmp.color.a;                         // keep the current LOD-driven alpha
                    lv.Tmp.color = new Color(labelColor.r, labelColor.g, labelColor.b, a);
                }
        }

        /// <summary>Public API for MapLayersPanel (Task 5). Flips every existing label's click raycastTarget
        /// on/off; leaving edit mode also cancels a pending add-mode and tears down any open rename box
        /// (via manager.DeselectAll() -> OnSelectionChanged -> EnsureEditUI).</summary>
        public void SetEditMode(bool on)
        {
            if (editMode == on) return;
            editMode = on;
            ApplyEditModeToViews();
            if (!on)
            {
                addMode = false;
                if (manager != null) manager.DeselectAll();
            }
        }

        void ApplyEditModeToViews()
        {
            foreach (var kv in views)
            {
                var lv = kv.Value;
                if (lv != null && lv.ClickTarget != null) lv.ClickTarget.raycastTarget = editMode;
            }
        }

        public void ToggleAddMode() { if (!editMode) return; addMode = !addMode; }

        void BuildCanvas()
        {
            var canvasGO = new GameObject("RegionLabelCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;   // below ALL app chrome (notes/legend=0, panels 40-100) so labels never cover UI
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasRect = canvasGO.GetComponent<RectTransform>();   // Canvas auto-adds a RectTransform
            EnsureEventSystemExists();
        }

        static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (auto-created)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── View lifecycle ────────────────────────────────────────────────────────

        /// <summary>Reconciles the label views to the manager's current list. Unlike a naive
        /// destroy-all-and-rebuild, this KEEPS surviving containers alive — destroying a container mid-drag
        /// would break the EventSystem's drag (its pointerDrag points at that GameObject), and MoveLabel fires
        /// OnLabelsChanged every drag frame. It also refreshes text for renames without recreating anything.
        /// Finally it reconciles the inline-edit UI to the surviving selection (SeedFromCells / LoadLabels /
        /// ClearAll / DeleteLabel clear selection but fire ONLY OnLabelsChanged — see EnsureEditUI).</summary>
        void Rebuild()
        {
            if (manager == null) { DestroyEditUI(); return; }

            var all = manager.GetAll();
            var live = new HashSet<string>();
            foreach (var d in all)
            {
                live.Add(d.Id);
                if (views.TryGetValue(d.Id, out var lv) && lv != null && lv.Container != null)
                {
                    if (lv.Tmp != null) lv.Tmp.text = d.Text;   // reflect renames without recreating the view
                }
                else
                {
                    views[d.Id] = CreateLabelView(d);
                }
            }

            // Drop views whose label was deleted.
            List<string> stale = null;
            foreach (var kv in views)
                if (!live.Contains(kv.Key)) (stale ??= new List<string>()).Add(kv.Key);
            if (stale != null)
                foreach (var id in stale)
                {
                    var lv = views[id];
                    if (lv != null && lv.Container != null) Destroy(lv.Container.gameObject);
                    views.Remove(id);
                }

            EnsureEditUI();
        }

        LabelView CreateLabelView(RegionLabelData d)
        {
            var go = new GameObject($"RegionLabel_{d.Id}");
            go.transform.SetParent(canvasRect, false);

            // Transparent, raycast-enabled click/drag target. Alpha 0 still receives raycasts
            // (GraphicRaycaster ignores color alpha unless alphaHitTestMinimumThreshold is set), and its
            // presence makes label clicks register as EventSystem.IsPointerOverGameObject() so the
            // brush/camera tools correctly skip them.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = editMode;   // Task 4: display-only by default (non-blocking); ApplyEditModeToViews flips this live
            var container = go.GetComponent<RectTransform>();
            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.sizeDelta = new Vector2(220f, 34f);

            var handler = go.AddComponent<RegionLabelPointerHandler>();
            handler.LabelId = d.Id;
            handler.Overlay = this;

            // TMP text as a stretched child (one Graphic per GameObject: Image here, TMP there).
            var tmpGO = new GameObject("Text");
            tmpGO.transform.SetParent(container, false);
            var tmp = tmpGO.AddComponent<TextMeshProUGUI>();
            if (labelFont != null) tmp.font = labelFont;
            var lm = EnsureLabelMaterial();
            if (lm != null) tmp.fontSharedMaterial = lm;   // shared outline+underlay material (no per-label instances)
            tmp.text = d.Text;
            tmp.fontSize = baseFontSize;
            tmp.fontStyle = FontStyles.Normal;         // upright (Forum reads far better than faux-italic on a busy map)
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 4f;                 // letter-spacing (tighter than before for legibility)
            tmp.color = labelColor;                    // shared label text color (DM-changeable)
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;                 // clicks handled by the container's Image
            // Outline + soft-shadow (underlay) live on the shared material (EnsureLabelMaterial), not per-tmp.
            var trt = tmp.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return new LabelView { Container = container, Tmp = tmp, ClickTarget = hit, Priority = d.Priority };
        }

        // ── Per-frame projection / LOD (Task 4 math, retargeted to the container) ────

        void LateUpdate()
        {
            if (!visible || manager == null || cameraController == null) return;
            var cam = cameraController.targetCamera;
            float refSize = cameraController.NaturalFitSize;
            if (cam == null || refSize <= 0f) return;

            HandleMapClick();       // add-mode drop, or click-away-to-dismiss (runs before the projection loop)

            float r = cam.orthographicSize / refSize;
            float biomeA = BiomeAlpha(r);
            float macroA = MacroAlpha(r);

            // Project each label; buffer the on-screen, non-faded ones for priority overlap culling.
            cullBuffer.Clear();
            foreach (var d in manager.GetAll())
            {
                if (!views.TryGetValue(d.Id, out var lv) || lv == null || lv.Container == null || lv.Tmp == null) continue;
                float a = d.Kind == RegionLabelData.LabelKind.Biome ? biomeA : macroA;
                Vector3 world = new Vector3(d.WorldPosition.X, labelYOffsetWorld, d.WorldPosition.Y);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool onScreen = sp.z > 0f && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
                if (!onScreen || a <= 0.01f) { Park(lv); continue; }
                cullBuffer.Add((lv, new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f), a));
            }

            // Overlap cull: higher Priority wins (continents/seas > bigger biome zones > smaller); overlapping
            // lower-priority labels are HIDDEN, not moved — every shown label stays pinned to its anchor.
            cullBuffer.Sort((x, y) => y.lv.Priority.CompareTo(x.lv.Priority));
            placedRects.Clear();
            foreach (var e in cullBuffer)
            {
                var rect = new Rect(e.pos.x - 110f, e.pos.y - 17f, 220f, 34f);
                bool blocked = false;
                for (int i = 0; i < placedRects.Count; i++) if (placedRects[i].Overlaps(rect)) { blocked = true; break; }
                if (blocked) { Park(e.lv); continue; }
                placedRects.Add(rect);
                SetAlpha(e.lv, e.a);
                e.lv.Container.anchoredPosition = e.pos;
            }

            UpdateEditUIPosition();   // keep the inline-edit UI riding the selected label
        }

        static void SetAlpha(LabelView lv, float a) { var c = lv.Tmp.color; c.a = a; lv.Tmp.color = c; }
        static void Park(LabelView lv) { SetAlpha(lv, 0f); lv.Container.anchoredPosition = new Vector2(-9999f, -9999f); }

        // Biome labels: visible in the MID band (fade in from nearFrac, fade out into the macro band).
        float BiomeAlpha(float r)
        {
            float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(nearFrac, farFrac, r));
            float down = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(macroLoFrac, macroHiFrac, r));
            return up * (1f - down);
        }
        // Continents + seas: visible when zoomed OUT past the mid band.
        float MacroAlpha(float r) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(macroLoFrac, macroHiFrac, r));

        // Shared TMP material: crisp dark outline + soft dark underlay (drop shadow) so text lifts off terrain.
        Material EnsureLabelMaterial()
        {
            if (labelMat == null && labelFont != null)
            {
                labelMat = new Material(labelFont.material);
                labelMat.SetFloat("_OutlineWidth", 0.3f);
                labelMat.SetColor("_OutlineColor", new Color32(6, 9, 14, 255));
                labelMat.EnableKeyword("UNDERLAY_ON");
                labelMat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.7f));
                labelMat.SetFloat("_UnderlayOffsetX", 1f);
                labelMat.SetFloat("_UnderlayOffsetY", -1f);
                labelMat.SetFloat("_UnderlaySoftness", 0.35f);
                labelMat.SetFloat("_UnderlayDilate", 0.1f);
            }
            return labelMat;
        }

        // ── Pointer callbacks (driven by RegionLabelPointerHandler) ─────────────────

        public void HandleLabelClicked(string id)
        {
            if (!editMode) return;
            if (manager != null) manager.SelectLabel(id);
        }

        public void HandleLabelDragBegin(string id)
        {
            if (!editMode) return;
            if (manager == null) return;
            var sel = manager.GetSelected();
            if (sel == null || sel.Id != id) manager.SelectLabel(id);
        }

        public void HandleLabelDrag(string id, PointerEventData eventData)
        {
            if (!editMode) return;
            if (manager == null) return;
            if (TryUnprojectMouseToGround(out var w))
                manager.MoveLabel(id, new System.Numerics.Vector2(w.x, w.z));
        }

        // ── Map-click handling (add-mode drop / click-away dismiss) ──────────────────

        /// <summary>Single map-click dispatcher. A click on a label's transparent Image counts as
        /// IsPointerOverGameObject() (and is handled by that label's pointer handler), so it never reaches
        /// here — only genuine empty-map clicks do. In add-mode an empty click drops a new label; otherwise
        /// an empty click dismisses an open rename box (click-away-to-finish). The rename box no longer
        /// auto-closes on blur/Enter, so this is its explicit dismiss path.</summary>
        void HandleMapClick()
        {
            if (!editMode) return;
            if (manager == null) return;
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (addMode)
            {
                if (TryUnprojectMouseToGround(out var w))
                {
                    manager.AddLabel(new System.Numerics.Vector2(w.x, w.z), null);   // auto-selects → rename box opens
                    addMode = false;
                }
                return;
            }

            // Not in add-mode: an empty-map click finishes editing by deselecting the current label.
            if (manager.GetSelected() != null) manager.DeselectAll();
        }

        /// <summary>Unproject the current mouse position onto the map plane (y = 0). Same ray math as the brush.</summary>
        bool TryUnprojectMouseToGround(out Vector3 world)
        {
            world = Vector3.zero;
            var cam = cameraController != null ? cameraController.targetCamera : null;
            if (cam == null || Mouse.current == null) return false;
            Vector2 mp = Mouse.current.position.ReadValue();
            Ray r = cam.ScreenPointToRay(mp);
            if (Mathf.Abs(r.direction.y) < 1e-6f) return false;   // ray parallel to plane
            float t = -r.origin.y / r.direction.y;
            if (t < 0f) return false;                              // plane is behind the camera
            world = r.origin + r.direction * t;
            return true;
        }

        // ── Inline edit UI (rename + delete) ──────────────────────────────────────────

        void HandleSelectionChanged(RegionLabelData sel) => EnsureEditUI();

        /// <summary>Reconciles the edit UI to the manager's CURRENT selection. Idempotent: if the box is already
        /// open for the selected label it does nothing (so a benign OnLabelsChanged during a drag, or a
        /// click-after-drag re-select, does not tear down an in-progress edit). This is the single guard that
        /// keeps a reseed/reload/clear/delete — which clear selection but fire only OnLabelsChanged — from
        /// leaving a stale rename box pointing at a deleted label.</summary>
        void EnsureEditUI()
        {
            var sel = manager != null ? manager.GetSelected() : null;
            if (sel == null) { DestroyEditUI(); return; }
            if (editId == sel.Id && editRoot != null) return;   // already open for this label
            DestroyEditUI();
            OpenEditUI(sel);
        }

        void OpenEditUI(RegionLabelData d)
        {
            var go = new GameObject("RegionLabelEdit");
            go.transform.SetParent(canvasRect, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);      // themed backing so the box reads over the map
            editRoot = go.GetComponent<RectTransform>();
            editRoot.anchorMin = new Vector2(0.5f, 0.5f);
            editRoot.anchorMax = new Vector2(0.5f, 0.5f);
            editRoot.pivot = new Vector2(0.5f, 0.5f);
            editRoot.sizeDelta = new Vector2(220f, 30f);
            if (views.TryGetValue(d.Id, out var lv) && lv != null && lv.Container != null)
                editRoot.anchoredPosition = lv.Container.anchoredPosition;   // start over the label

            editField = BuildRenameField(editRoot);
            var fieldRT = editField.GetComponent<RectTransform>();
            fieldRT.anchorMin = new Vector2(0f, 0f);
            fieldRT.anchorMax = new Vector2(1f, 1f);
            fieldRT.offsetMin = new Vector2(4f, 3f);
            fieldRT.offsetMax = new Vector2(-30f, -3f);        // leave room on the right for "×"
            editField.text = d.Text;
            editField.onEndEdit.AddListener(OnRenameCommitted);

            // "×" delete button.
            var xGO = new GameObject("Delete");
            xGO.transform.SetParent(editRoot, false);
            var xImg = xGO.AddComponent<Image>();
            ThemeService.Tag(xImg, ThemeRole.Elev);
            var xBtn = xGO.AddComponent<Button>();
            xBtn.targetGraphic = xImg;
            // Capture the id in a closure — NOT editId, which the field's blur (fired on this button's
            // pointer-DOWN) could otherwise have nulled before this onClick runs on pointer-UP.
            string deleteId = d.Id;
            xBtn.onClick.AddListener(() => { if (manager != null) manager.DeleteLabel(deleteId); });
            var xRT = xGO.GetComponent<RectTransform>();
            xRT.anchorMin = new Vector2(1f, 0.5f);
            xRT.anchorMax = new Vector2(1f, 0.5f);
            xRT.pivot = new Vector2(1f, 0.5f);
            xRT.anchoredPosition = new Vector2(-4f, 0f);
            xRT.sizeDelta = new Vector2(22f, 22f);
            var xtGO = new GameObject("X");
            xtGO.transform.SetParent(xGO.transform, false);
            var xText = xtGO.AddComponent<Text>();
            xText.text = "✕";
            xText.font = builtinFont;
            xText.fontSize = 14;
            ThemeService.Tag(xText, ThemeRole.Danger);
            xText.alignment = TextAnchor.MiddleCenter;
            var xtRT = xtGO.GetComponent<RectTransform>();
            xtRT.anchorMin = Vector2.zero;
            xtRT.anchorMax = Vector2.one;
            xtRT.sizeDelta = Vector2.zero;

            editId = d.Id;
            editRoot.SetAsLastSibling();     // draw above the label containers
            editField.Select();
            editField.ActivateInputField();  // focus for immediate typing
        }

        void OnRenameCommitted(string value)
        {
            if (manager == null || editId == null) return;           // editId==null guards a stray onEndEdit after teardown
            if (!string.IsNullOrWhiteSpace(value)) manager.RenameLabel(editId, value);
            // Commit ONLY — deliberately no DeselectAll / teardown here. The legacy InputField blurs (and so
            // fires this onEndEdit) on the "×" button's pointer-DOWN; tearing the box down here would destroy
            // the "×" before its pointer-UP onClick, so DeleteLabel would never fire. The box is dismissed
            // instead by an empty-map click (HandleMapClick), by selecting another label, or by delete.
        }

        void UpdateEditUIPosition()
        {
            if (editRoot == null || editId == null) return;
            if (views.TryGetValue(editId, out var lv) && lv != null && lv.Container != null)
                editRoot.anchoredPosition = lv.Container.anchoredPosition;   // track the label (incl. drag / park)
        }

        void DestroyEditUI()
        {
            if (editRoot != null) Destroy(editRoot.gameObject);
            editRoot = null;
            editField = null;
            editId = null;
        }

        /// <summary>Single-line rename field, mirroring PoiEditPanel.BuildInputField (Image bg as targetGraphic,
        /// legacy Text child + Placeholder, builtin font). Legacy UnityEngine.UI.InputField is the shipped,
        /// low-risk path in this project; a legacy box over TMP labels is cosmetically fine.</summary>
        InputField BuildRenameField(Transform parent)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.lineType = InputField.LineType.SingleLine;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = builtinFont;
            text.fontSize = 14;
            ThemeService.Tag(text, ThemeRole.Txt);
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phText = phGO.AddComponent<Text>();
            phText.font = builtinFont;
            phText.fontSize = 14;
            ThemeService.Tag(phText, ThemeRole.Mut);
            phText.fontStyle = FontStyle.Italic;
            phText.text = "Название региона";
            var phRect = phGO.GetComponent<RectTransform>();
            phRect.anchorMin = new Vector2(0.02f, 0f);
            phRect.anchorMax = new Vector2(1f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = phText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.flexibleWidth = 1f;
            return field;
        }

        /// <summary>Holder for one label's runtime view: the clickable/draggable container + its TMP child.</summary>
        class LabelView
        {
            public RectTransform Container;
            public TextMeshProUGUI Tmp;
            /// <summary>The container's transparent click Image. Task 4: its raycastTarget is toggled by
            /// SetEditMode/ApplyEditModeToViews so labels are non-blocking in display mode (edit mode off).</summary>
            public Image ClickTarget;
            public float Priority;   // mirrors RegionLabelData.Priority for overlap-cull ordering
        }
    }

    /// <summary>Rides the EventSystem + GraphicRaycaster built by RegionLabelOverlay. One per label container;
    /// forwards click/drag to the overlay, which drives RegionLabelManager CRUD.</summary>
    public class RegionLabelPointerHandler : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler
    {
        public string LabelId;
        public RegionLabelOverlay Overlay;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Overlay != null) Overlay.HandleLabelClicked(LabelId);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Overlay != null) Overlay.HandleLabelDragBegin(LabelId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Overlay != null) Overlay.HandleLabelDrag(LabelId, eventData);
        }
    }
}
