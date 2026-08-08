using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Ctrl+F on the open page: a query, every match lit up, «3 из 12», and Enter to walk them.
    ///
    /// THE COMPONENT LIVES ON THE PAGE ROOT AND THE BOX IS ITS CHILD, which is not an arrangement detail: a
    /// MonoBehaviour on a deactivated object gets no Update, so a bar that WAS the hidden object could never
    /// notice the Ctrl+F meant to open it. The root is active for exactly as long as the page is on screen,
    /// which is exactly when this key should work.
    ///
    /// WHAT IT SEARCHES IS PageSearch'S ANSWER, not a second scan — display text, so a query matches the
    /// names the DM reads rather than the ids under them, and every offset is already the one a highlight
    /// needs. Hits inside COLLAPSED sections count, and jumping to one opens what hides it
    /// (NotesDocOps.ExpandTo); a search that reports twelve matches and cannot show four of them is worse
    /// than one that found eight.
    ///
    /// WHILE THE FIELD HAS THE CARET, THE PAGE'S OWN KEYBOARD STANDS DOWN — `SearchOwnsKeys`, which
    /// DocumentPageView ORs with the palette's flag rather than sharing one switch between them. Two
    /// unrelated things suspending the same keyboard through one bool would mean whichever released it last
    /// spoke for both.
    ///
    /// LEGACY uGUI, like every other panel on this page: only body text went to TextMeshPro.
    /// </summary>
    public class PageSearchBar : MonoBehaviour
    {
        public const string ObjectName = "PageSearchBox";

        const float BarHeight = 34f;
        const float BarWidth = 400f;
        /// <summary>Clear of the «+ Раздел» strip the page reserves above the prose, plus a little air.</summary>
        const float TopOffset = 50f;
        const float ButtonSize = 26f;

        DocumentPageView page;
        Font font;

        GameObject boxGO;
        InputField field;
        Text counter;

        readonly List<PageSearch.PageHit> hits = new List<PageSearch.PageHit>();
        int current = -1;

        public bool IsOpen => boxGO != null && boxGO.activeSelf;

        /// <summary>Replaced rather than repaired, for the reason PageToolbar.Attach gives: a domain reload
        /// nulls every field here while the objects they point at survive, and a Button's onClick list is not
        /// restored by finding the Button again.</summary>
        public static PageSearchBar Attach(DocumentPageView page, Font font)
        {
            if (page == null || page.Root == null) return null;

            var existing = page.Root.GetComponent<PageSearchBar>();
            if (existing != null) DestroyImmediate(existing);
            var staleBox = page.Root.Find(ObjectName);
            if (staleBox != null) DestroyImmediate(staleBox.gameObject);

            var bar = page.Root.gameObject.AddComponent<PageSearchBar>();
            bar.page = page;
            bar.font = font;
            bar.Build();
            return bar;
        }

        // ── opening, closing, and who owns the keys ──────────────────────────────

        public void Open()
        {
            if (boxGO == null) return;
            boxGO.SetActive(true);
            if (field != null)
            {
                field.Select();
                field.ActivateInputField();
            }
            // Re-run rather than trusting what was left: the page may have been edited since the bar was
            // last closed, and showing a stale «3 из 12» over text that no longer says that is worse than
            // showing nothing.
            RunSearch(field != null ? field.text : "");
        }

        public void Close()
        {
            if (boxGO != null) boxGO.SetActive(false);
            hits.Clear();
            current = -1;
            if (page != null) page.SearchOwnsKeys = false;
            ClearHighlights();
        }

        /// <summary>The page went off screen — a tab switch, another surface, a reload. The bar goes with it,
        /// or it would come back holding results for a page nobody is looking at.</summary>
        void OnDisable() => Close();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || page == null) return;

            // ONLY THE PANE THE CARET IS IN (two panes arc, Task 5), AND THAT GOES FOR EVERY KEY BELOW, not
            // only the opening chord. This whole method polls the hardware per VIEW, and since that arc's
            // Task 4 there is one view per pane — so an ungated Ctrl+F opened BOTH search bars, each calling
            // ActivateInputField, with Unity's undefined Update order deciding which one ended up holding
            // the caret. IsKeyboardTarget asks the one router that also answers for undo and «@» (see
            // DocumentPageView.KeyboardTargetProbe), so all three land in the same pane.
            //
            // THE FIRST ROUND OF THIS FIX GATED ONLY Ctrl+F, on the argument that "an already-open bar
            // belongs to whoever CLICKS in it". That argument does not cover a KEY, where there is no click
            // to belong to — and two bars open at once is still reachable without one: Ctrl+F in pane 0
            // (its field takes the caret), click into pane 1's text, Ctrl+F there. From then on one F3 or
            // Enter stepped BOTH bars and one Esc closed both, so the other pane silently jumped to its own
            // next match and repainted its highlights. That is exactly the "two owners" this task exists to
            // remove, so the same gate is on all four keys.
            //
            // THE BAR IN THE OTHER PANE IS NOT STRANDED BY THIS: its own box is built under `page.Root`, so
            // clicking into its field makes that view the caret's view and hands every key back at once.
            bool ours = page.IsKeyboardTarget;

            if ((keyboard.ctrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                && keyboard.fKey.wasPressedThisFrame && ours)
            { Open(); return; }

            if (!IsOpen) { page.SearchOwnsKeys = false; return; }

            // NOT gated: it is a report about THIS bar's own field, not a key. DocKeyboardController reads it
            // through KeyboardSuspended to stand down while a search field holds the caret, and that has to
            // stay true of whichever bar actually holds it.
            page.SearchOwnsKeys = field != null && field.isFocused;

            if (clearSelectionWhenFocused && field != null && field.isFocused)
            {
                field.MoveTextEnd(false);
                clearSelectionWhenFocused = false;
            }

            if (!ours) return;

            if (keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                || keyboard.f3Key.wasPressedThisFrame)
                Step(!shift);
        }

        // ── searching ────────────────────────────────────────────────────────────

        /// <summary>Re-runs the query against the page as it is now. Called on every keystroke in the field,
        /// and again after any rebuild — see DocumentPageView.Rebuild's call to Reapply.</summary>
        void RunSearch(string query)
        {
            hits.Clear();
            current = -1;

            // КАРТОЧКА ПЕРЕДАЁТСЯ ЗДЕСЬ, и без этого аргумента правило её не увидит: трёхаргументная форма
            // Find подставляет card = null. Первый заход починил чистое правило и забыл вызывающую
            // сторону — поиск по «Кто» так и не находил ничего.
            if (page != null && page.Page != null)
                hits.AddRange(PageSearch.Find(page.Page.Blocks, page.Page.Character, query, page.ResolveNameFor));

            // Straight to the first match, so typing a name and looking up is enough — the DM should not have
            // to press anything to see where the first one is.
            if (hits.Count > 0) current = 0;

            ApplyHighlights();
            UpdateCounter();
            if (current >= 0) Reveal(hits[current]);
        }

        void Step(bool forward)
        {
            if (hits.Count == 0) return;
            current = PageSearch.Step(hits.Count, current, forward);
            ApplyHighlights();
            UpdateCounter();
            Reveal(hits[current]);
            // Enter deactivates a single-line uGUI field, so the caret has to be given back or the second
            // Enter would reach the page instead of this bar.
            if (field != null && !field.isFocused)
            {
                field.ActivateInputField();
                // AND THE SELECTION UNDONE, a frame later. The legacy InputField selects everything on focus
                // and offers no way to turn that off (onFocusSelectAll is TMP's, not this one's), so a DM who
                // pressed Enter twice and then typed a letter would have it replace the query they were
                // stepping through. ActivateInputField only takes effect on the next update, which is why
                // this cannot simply be done here — see Update.
                clearSelectionWhenFocused = true;
            }
        }

        /// <summary>Set by Step; consumed by Update once the field has actually taken focus.</summary>
        bool clearSelectionWhenFocused;

        /// <summary>Puts the hit on screen: opens whatever hides it first, and only then scrolls, because a
        /// row inside a collapsed section has no rect to scroll to yet.</summary>
        void Reveal(PageSearch.PageHit hit)
        {
            if (hit == null || page == null || page.Page == null) return;

            // ПОПАДАНИЕ В КАРТОЧКЕ ЛЕЖИТ НЕ В СТРОКЕ, а в шапке персонажа, и строки у него нет вовсе
            // (BlockId пуст). Разворачивать нечего, прокручивать к строке нечего — шапка стоит первой в
            // той же колонке, поэтому «показать её» значит промотать колонку в начало.
            //
            // Подсвечивается при этом ПОЛЕ ЦЕЛИКОМ, а не слово в нём: у поля карточки нет отдельного слоя
            // показа, в котором строка прозы красит буквы разметкой. Почему так и что стоило бы иначе —
            // в доке CharacterHeaderView.SetSearchHighlights, там же и цена решения.
            if (hit.Field != PageSearch.CardField.None)
            {
                page.RevealTop();
                return;
            }

            if (NotesDocOps.ExpandTo(page.Page.Blocks, hit.BlockId))
            {
                // Rebuild re-applies the highlights on its way out, so this does not re-paint them here.
                page.Rebuild();
            }
            page.RevealRow(hit.BlockId);
        }

        /// <summary>Hands each row the hits that fall inside it. Rebuilt from scratch every time rather than
        /// tracked incrementally — the list is the length of what one page matched, and a per-row cache that
        /// drifted from `hits` would light up text that no longer says what it says.</summary>
        void ApplyHighlights()
        {
            if (page == null) return;

            var currentHit = current >= 0 && current < hits.Count ? hits[current] : null;

            // Шапка персонажа — такой же получатель подсветки, как строка, и получает ВЕСЬ список: она
            // сама отбирает из него те попадания, у которых названо её поле (см. её SetSearchHighlights).
            page.SetCardSearchHighlights(hits, currentHit);

            foreach (var row in page.Rows)
            {
                if (row == null) continue;
                List<PageSearch.PageHit> mine = null;
                foreach (var hit in hits)
                    if (hit.BlockId == row.BlockId) (mine ?? (mine = new List<PageSearch.PageHit>())).Add(hit);
                row.SetSearchHighlights(mine, currentHit);
            }
        }

        void ClearHighlights()
        {
            if (page == null) return;
            // Шапка гасится вместе со строками. Пропустить её значило бы оставить поле подсвеченным после
            // закрытия строки поиска — подложка сама не гаснет, у неё нет своего повода.
            page.SetCardSearchHighlights(null, null);
            foreach (var row in page.Rows)
                if (row != null) row.SetSearchHighlights(null, null);
        }

        /// <summary>Called by DocumentPageView after every Rebuild — the rows the highlights were painted on
        /// have just been destroyed and remade, and the block list may have changed under the query too.</summary>
        public void Reapply()
        {
            if (!IsOpen) return;

            // The hit the DM was standing on, by identity of PLACE rather than by index: a rebuild can change
            // how many matches there are, and index 3 of the new list may be somewhere else entirely.
            var was = current >= 0 && current < hits.Count ? hits[current] : null;

            hits.Clear();
            if (page != null && page.Page != null)
                hits.AddRange(PageSearch.Find(page.Page.Blocks, page.Page.Character,
                                              field != null ? field.text : "", page.ResolveNameFor));

            current = hits.Count > 0 ? 0 : -1;
            if (was != null)
                for (int i = 0; i < hits.Count; i++)
                    // ПОЛЕ ВХОДИТ В ОПОЗНАНИЕ МЕСТА наравне с блоком и смещением: у попаданий в карточке
                    // BlockId пуст у ВСЕХ, поэтому без него «Кто, смещение 0» и «Чего хочет, смещение 0»
                    // считались бы одним и тем же местом, и перестройка перебрасывала бы ДМ между полями.
                    if (hits[i].BlockId == was.BlockId && hits[i].DisplayStart == was.DisplayStart
                        && hits[i].Field == was.Field)
                    { current = i; break; }

            ApplyHighlights();
            UpdateCounter();
        }

        void UpdateCounter()
        {
            if (counter == null) return;
            counter.text = hits.Count == 0
                ? (field != null && !string.IsNullOrWhiteSpace(field.text) ? "нет" : "")
                : $"{current + 1} из {hits.Count}";
        }

        // ── the box ──────────────────────────────────────────────────────────────

        void Build()
        {
            var root = page.Root;

            boxGO = new GameObject(ObjectName, typeof(RectTransform));
            boxGO.transform.SetParent(root, false);
            var boxRect = (RectTransform)boxGO.transform;
            // Top-right, floating over the prose rather than pushing it down: a bar that changed the page's
            // height would reflow every line the DM is trying to look at.
            boxRect.anchorMin = new Vector2(1f, 1f);
            boxRect.anchorMax = new Vector2(1f, 1f);
            boxRect.pivot = new Vector2(1f, 1f);
            boxRect.anchoredPosition = new Vector2(-18f, -TopOffset);
            boxRect.sizeDelta = new Vector2(BarWidth, BarHeight);
            ThemeService.Tag(boxGO.AddComponent<Image>(), ThemeRole.Elev);

            float x = -6f;
            var close = BuildButton(boxRect, "×", x, Close);
            x -= ButtonSize + 4f;
            BuildButton(boxRect, "↓", x, () => Step(true));
            x -= ButtonSize + 2f;
            BuildButton(boxRect, "↑", x, () => Step(false));
            x -= ButtonSize + 6f;

            counter = BuildLabel(boxRect, "", 12, ThemeRole.Mut, TextAnchor.MiddleRight);
            var counterRect = (RectTransform)counter.transform;
            counterRect.anchorMin = new Vector2(1f, 0f);
            counterRect.anchorMax = new Vector2(1f, 1f);
            counterRect.pivot = new Vector2(1f, 0.5f);
            counterRect.anchoredPosition = new Vector2(x, 0f);
            counterRect.sizeDelta = new Vector2(74f, 0f);
            x -= 74f + 6f;

            BuildField(boxRect, BarWidth + x - 8f);

            boxGO.SetActive(false);
        }

        void BuildField(RectTransform parent, float width)
        {
            var go = new GameObject("Query", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(8f, 0f);
            rect.sizeDelta = new Vector2(Mathf.Max(80f, width), BarHeight - 10f);

            var image = go.AddComponent<Image>();
            ThemeService.Tag(image, ThemeRole.Panel2);

            field = go.AddComponent<InputField>();
            field.targetGraphic = image;
            field.lineType = InputField.LineType.SingleLine;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = font;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            ThemeService.Tag(text, ThemeRole.Txt);
            StretchInside((RectTransform)textGO.transform);
            field.textComponent = text;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(go.transform, false);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "Найти на странице";
            placeholder.font = font;
            placeholder.fontSize = 13;
            placeholder.alignment = TextAnchor.MiddleLeft;
            ThemeService.Tag(placeholder, ThemeRole.Mut);
            StretchInside((RectTransform)placeholderGO.transform);
            field.placeholder = placeholder;

            field.onValueChanged.AddListener(RunSearch);
        }

        static void StretchInside(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);
        }

        Button BuildButton(RectTransform parent, string label, float rightOffset, System.Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(rightOffset, 0f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            var image = go.AddComponent<Image>();
            ThemeService.Tag(image, ThemeRole.Panel2);

            var text = BuildLabel((RectTransform)go.transform, label, 14, ThemeRole.Txt, TextAnchor.MiddleCenter);
            StretchInside((RectTransform)text.transform);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
            return button;
        }

        Text BuildLabel(RectTransform parent, string value, int size, ThemeRole role, TextAnchor anchor)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.raycastTarget = false;
            ThemeService.Tag(text, role);
            return text;
        }
    }
}
