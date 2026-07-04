using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Splits the screen between the map area (left) and the notes area (right) using
    /// RectTransform anchors, so both regions rescale proportionally on window resize and
    /// never overlap. The split is user-draggable (via a DraggableDivider straddling
    /// notesAreaRoot's left edge, extending into the map's side so it never competes for
    /// raycasts with the sidebar) and persisted across sessions in PlayerPrefs.
    /// </summary>
    public class NotesLayoutController : MonoBehaviour
    {
        const string PrefsKey = "NotesLayout.SplitFraction";
        const float DefaultSplitFraction = 2f / 3f;
        public const float MinSplitFraction = 0.3f;
        public const float MaxSplitFraction = 0.85f;
        const float DividerWidth = 8f;

        /// <summary>Single source of truth for the map/notes screen split fraction.
        /// MapLegendUI and PoiEditPanel read this directly instead of each declaring their own
        /// copy, which is what let them drift out of sync before this class existed. Lazily
        /// initialized from PlayerPrefs (falling back to DefaultSplitFraction) the first time
        /// any code touches this property — a static property's initializer resolves on first
        /// access regardless of which GameObject's Awake() runs first, the same ordering
        /// guarantee a plain const used to provide (see
        /// docs/superpowers/specs/2026-07-03-map-notes-split-single-source-design.md), while
        /// still allowing later mutation (which a const could never do).</summary>
        public static float SplitFraction { get; private set; } = PlayerPrefs.GetFloat(PrefsKey, DefaultSplitFraction);

        /// <summary>Fires whenever SplitFraction changes (including live during a drag) so
        /// panels anchored to the split (MapLegendUI, PoiEditPanel) can update instead of only
        /// reading the value once at their own Awake().</summary>
        public static event System.Action<float> OnSplitFractionChanged;

        public static void SetSplitFraction(float value)
        {
            value = Mathf.Clamp(value, MinSplitFraction, MaxSplitFraction);
            if (Mathf.Approximately(value, SplitFraction)) return;
            SplitFraction = value;
            OnSplitFractionChanged?.Invoke(value);
        }

        /// <summary>Writes the current SplitFraction to PlayerPrefs. Called on drag-end and on
        /// double-click reset, NOT on every intermediate drag frame — SetSplitFraction alone
        /// already applies the value live via OnSplitFractionChanged.</summary>
        public static void SaveSplitFraction() => PlayerPrefs.SetFloat(PrefsKey, SplitFraction);

        [Tooltip("Root RectTransform containing the notes editor UI. Anchored to the right third.")]
        public RectTransform notesAreaRoot;
        [Tooltip("Camera rendering the 3D map (WorldMapRenderer.targetCamera). Its viewport rect is clamped to the map area so the map doesn't render underneath the notes UI.")]
        public Camera mapCamera;

        void Awake()
        {
            OnSplitFractionChanged += _ => Apply();
        }

        [ContextMenu("Apply Split")]
        public void Apply()
        {
            if (notesAreaRoot != null)
            {
                notesAreaRoot.anchorMin = new Vector2(SplitFraction, 0f);
                notesAreaRoot.anchorMax = new Vector2(1f, 1f);
                notesAreaRoot.offsetMin = Vector2.zero;
                notesAreaRoot.offsetMax = Vector2.zero;
            }

            if (mapCamera != null)
                mapCamera.rect = new Rect(0f, 0f, SplitFraction, 1f);
        }

        /// <summary>Builds the draggable divider. Called once by NotesRootBuilder right after
        /// notesAreaRoot is assigned — deliberately NOT called from Apply() itself, since
        /// Apply() re-runs on every SplitFraction change (including every drag frame via
        /// OnSplitFractionChanged), which would otherwise spawn a new divider GameObject every
        /// single frame while dragging.</summary>
        public void BuildDivider()
        {
            if (notesAreaRoot == null) return;

            // Anchored at notesAreaRoot's own left edge, pivot=(1,0.5) makes the bar extend
            // LEFTWARD — into the map's 3D-camera-viewport area, which has no UI raycast
            // targets at all — rather than straddling into notesAreaRoot itself, where the
            // sidebar (created later, as notesAreaRoot's first child) would win any raycast
            // in the overlapping region.
            var divider = DraggableDivider.Create(notesAreaRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.5f), DividerWidth);
            divider.OnDragDeltaX += dx => SetSplitFraction(SplitFraction + dx / Screen.width);
            divider.OnDragEnd += SaveSplitFraction;

            var doubleClick = divider.gameObject.AddComponent<DoubleClickHandler>();
            doubleClick.OnDoubleClick = () =>
            {
                SetSplitFraction(DefaultSplitFraction);
                SaveSplitFraction();
            };
        }

        // ── Self-tests ─────────────────────────────────────────────────────────

        [ContextMenu("Self-Test: Notes Layout — Split Fraction Clamp")]
        public void SelfTestSplitFractionClamp()
        {
            float original = SplitFraction;
            bool eventFired = false;
            System.Action<float> handler = _ => eventFired = true;
            OnSplitFractionChanged += handler;

            SetSplitFraction(0.1f);
            bool clampedLow = Mathf.Approximately(SplitFraction, MinSplitFraction);

            SetSplitFraction(0.99f);
            bool clampedHigh = Mathf.Approximately(SplitFraction, MaxSplitFraction);

            OnSplitFractionChanged -= handler;
            SetSplitFraction(original);

            bool ok = clampedLow && clampedHigh && eventFired;
            Debug.Log(ok
                ? "Self-Test Notes Layout — Split Fraction Clamp: PASS"
                : $"Self-Test Notes Layout — Split Fraction Clamp: FAIL (clampedLow={clampedLow}, clampedHigh={clampedHigh}, eventFired={eventFired})");
        }
    }
}
