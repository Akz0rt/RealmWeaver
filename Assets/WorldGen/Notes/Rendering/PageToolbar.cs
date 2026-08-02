using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The page's own editing toolbar: what a DM who does not know the keyboard can still do.
    ///
    /// IT CARRIES NO RULE OF ITS OWN. Every button asks DocumentPageView, which asks NotesDocOps — so the
    /// grammar of a document lives in one place and this class only decides what is enabled and what a
    /// button is called. That is also why it can be rebuilt from nothing at any time (see Attach).
    ///
    /// LEGACY uGUI, deliberately. Only the page's BODY TEXT went to TextMeshPro in Р2 (the arc's constraint:
    /// panels do not migrate), so this follows NotesToolbar's idiom — themed buttons over the built-in font —
    /// rather than starting a second, half-migrated chrome stack.
    ///
    /// WHAT THE LABELS SAY, AND WHY THEY ARE WORDS. The spec's row is «🖼 Изображение · 🔗 Ссылка · ↶ · ↷»;
    /// the built-in Liberation font has no emoji and no guarantee of the undo arrows, and a tofu box on the
    /// DM's first toolbar is worse than a plain word. «→ Раздел» reads as "turn this row into a section",
    /// which is what the button does — it converts the row the caret was last in, and does nothing when
    /// there was none, so it is disabled then rather than quietly doing nothing.
    ///
    /// LAST focus, not current: clicking any button moves Unity's selection to that button and deactivates
    /// the input field first, so "which row is focused" answers "none" by the time onClick runs. The page
    /// remembers instead (DocumentPageView.LastFocusedBlockId).
    /// </summary>
    public class PageToolbar : MonoBehaviour
    {
        public const string ObjectName = "PageToolbar";
        const float ButtonHeight = 26f;
        const float Spacing = 4f;

        DocumentPageView page;
        Font font;

        Button convertSection, convertItem, convertProse, insertImage, insertLink, undo, redo;

        /// <summary>Filled by Task 9's link picker. Until it is, «Ссылка» has nowhere to go and says so by
        /// being disabled — the honest state, and one the DM can tell apart from a broken button.</summary>
        public System.Action OnInsertLinkRequested;

        /// <summary>Builds the bar into the strip the page reserves for it, replacing any previous one.
        ///
        /// REBUILT RATHER THAN REPAIRED, because everything here is chrome with no state worth keeping and
        /// because a Button's onClick is a runtime listener list that a domain reload does not restore —
        /// the trap DocumentPageView.EnsureWired documents for its own «+ Раздел». Throwing the old bar away
        /// and making a new one cannot half-work the way re-finding one can.</summary>
        public static PageToolbar Attach(RectTransform parent, DocumentPageView page, Font font)
        {
            if (parent == null || page == null) return null;

            var existing = parent.Find(ObjectName);
            if (existing != null) DestroyImmediate(existing.gameObject);

            var barGO = new GameObject(ObjectName, typeof(RectTransform));
            barGO.transform.SetParent(parent, false);
            var barRect = barGO.GetComponent<RectTransform>();
            // Anchored to the strip's RIGHT edge and sized to its content, so «+ Раздел» keeps the left.
            barRect.anchorMin = new Vector2(1f, 0.5f);
            barRect.anchorMax = new Vector2(1f, 0.5f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.anchoredPosition = new Vector2(-10f, 0f);
            barRect.sizeDelta = new Vector2(0f, ButtonHeight);

            var layout = barGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleRight;

            var fitter = barGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bar = barGO.AddComponent<PageToolbar>();
            bar.page = page;
            bar.font = font;

            bar.convertSection = bar.BuildButton(barRect, "→ Раздел", () => page.ConvertFocused(BlockKind.Section));
            bar.convertItem = bar.BuildButton(barRect, "→ Пункт", () => page.ConvertFocused(BlockKind.Item));
            bar.convertProse = bar.BuildButton(barRect, "→ Проза", () => page.ConvertFocused(BlockKind.Prose));
            bar.insertImage = bar.BuildButton(barRect, "Картинка", bar.PickAndInsertImage);
            bar.insertLink = bar.BuildButton(barRect, "Ссылка", () => bar.OnInsertLinkRequested?.Invoke());
            bar.undo = bar.BuildButton(barRect, "Отменить", page.Undo);
            bar.redo = bar.BuildButton(barRect, "Вернуть", page.Redo);

            bar.RefreshEnabled();
            return bar;
        }

        Button BuildButton(RectTransform parent, string label, System.Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            ThemeService.Tag(image, ThemeRole.Elev);

            var text = new GameObject("Label", typeof(RectTransform));
            text.transform.SetParent(go.transform, false);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);
            var labelText = text.AddComponent<Text>();
            labelText.font = font;
            labelText.fontSize = 12;
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.raycastTarget = false;
            ThemeService.Tag(labelText, ThemeRole.Txt);

            // The label's own preferred width plus its padding, so a button is exactly as wide as its word —
            // a row of seven fixed-width buttons would either waste a narrow pane's width or clip a long one.
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = ButtonHeight;
            element.preferredWidth = labelText.preferredWidth + 16f;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
            return button;
        }

        void PickAndInsertImage()
        {
            var bytes = ImagePicker.OpenFileDialog();
            if (bytes == null || bytes.Length == 0) return;   // the DM cancelled the dialog
            page.InsertImageAfterFocused(bytes);
        }

        /// <summary>Polled rather than driven by an event, because what these buttons depend on — whether any
        /// row has the caret — changes with a click on the page, a rebuild, an undo and a keystroke alike,
        /// and no one of those owns the question. Seven boolean comparisons a frame is less machinery than
        /// an event every one of those paths would have to remember to raise.</summary>
        void Update()
        {
            if (page == null) return;
            RefreshEnabled();
        }

        void RefreshEnabled()
        {
            bool hasRow = page != null && !string.IsNullOrEmpty(page.LastFocusedBlockId);
            SetInteractable(convertSection, hasRow);
            SetInteractable(convertItem, hasRow);
            SetInteractable(convertProse, hasRow);
            SetInteractable(insertImage, hasRow);
            SetInteractable(insertLink, hasRow && OnInsertLinkRequested != null);
            SetInteractable(undo, page != null && page.CanUndo);
            SetInteractable(redo, page != null && page.CanRedo);
        }

        static void SetInteractable(Button button, bool value)
        {
            if (button != null && button.interactable != value) button.interactable = value;
        }
    }
}
