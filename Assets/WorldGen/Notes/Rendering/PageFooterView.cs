using UnityEngine;
using UnityEngine.UI;
using WorldGen.Notes.Data;
using WorldGen.Rendering.Theme;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The two things a page can say about itself that it does not say in prose: «Итоги» — who is in this
    /// session — and «Упомянута в» — where this page is spoken of.
    ///
    /// READ-ONLY, AND THAT IS THE POINT. There is no add, no reorder, no delete, no drag and no text field
    /// here. Both lists are computed on every refresh (NotesDocOps.CollectReferences / FindBacklinks) and
    /// stored nowhere, so they cannot drift from the prose that produced them. Removing an entry means
    /// removing the mention, and the UI must not suggest otherwise — a delete button here would be a promise
    /// that a summary can disagree with its page.
    ///
    /// ABSENT, NOT EMPTY. When a list has nothing in it its heading is not drawn at all, and when BOTH are
    /// empty this object deactivates itself entirely — a layout group ignores an inactive child, so a fresh
    /// session page shows a blank surface rather than two labelled empty boxes. That is what keeps the DM's
    /// ruling «страница сессии начинается пустой» literally true.
    ///
    /// A CHILD OF Content, not a third fixed strip: it belongs after the last block, and it should scroll
    /// away with the prose it summarises rather than hold screen space on a page nobody has scrolled to the
    /// bottom of.
    ///
    /// EVERY HEIGHT IS ARITHMETIC, never measured. The column's first layout pass happens before it has a
    /// width (DocumentPageView.SettleHeights early-returns then), so a footer whose height came from
    /// Text.preferredHeight or a ContentSizeFitter would report zero on that pass and appear only after some
    /// later relayout — the ContentSizeFitter-inside-a-VerticalLayoutGroup trap this project keeps meeting.
    ///
    /// LEGACY uGUI, like PageToolbar: only the page's body text went to TextMeshPro in Р2.
    /// </summary>
    public class PageFooterView : MonoBehaviour
    {
        public const string ObjectName = "PageFooter";

        const float GapAboveFooter = 28f;
        const float HeadingHeight = 24f;
        const float SubtitleHeight = 18f;
        const float RowHeight = 22f;
        const float GapBetweenLists = 16f;
        const float BottomPadding = 8f;

        DocumentPageView page;
        RectTransform self;
        LayoutElement element;

        /// <summary>Builds the footer into the scrolling column, replacing any previous one.
        ///
        /// REBUILT RATHER THAN REPAIRED, exactly as PageToolbar.Attach is: its rows are Buttons whose
        /// onClick is a runtime listener list that a domain reload does not restore, so re-finding the object
        /// would give back rows that look right and do nothing.</summary>
        public static PageFooterView Attach(RectTransform content, DocumentPageView page)
        {
            if (content == null || page == null) return null;

            var existing = content.Find(ObjectName);
            if (existing != null) DestroyImmediate(existing.gameObject);

            var go = new GameObject(ObjectName, typeof(RectTransform));
            go.transform.SetParent(content, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var view = go.AddComponent<PageFooterView>();
            view.page = page;
            view.self = rect;
            view.element = go.AddComponent<LayoutElement>();
            view.Refresh();
            return view;
        }

        /// <summary>Draws both lists from scratch. Cheap — a session page references a handful of things, and
        /// rebuilding is what makes "never stored" true rather than a claim.</summary>
        public void Refresh()
        {
            if (page == null || self == null) return;

            // Back to the bottom of the column. Rebuild destroys every row and makes new ones, which are
            // parented to Content and therefore land AFTER this object — so the footer has to reclaim the
            // end each time rather than being put there once.
            self.SetAsLastSibling();

            for (int i = self.childCount - 1; i >= 0; i--)
            {
                var child = self.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            var references = page.PageReferences();
            var backlinks = page.PageBacklinks();

            // Nothing to say. Deactivated rather than merely emptied: a zero-height child still takes the
            // layout group's spacing, which would leave a visible gap under the last row for no reason.
            if (references.Count == 0 && backlinks.Count == 0)
            {
                element.preferredHeight = 0f;
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);

            var font = page.BodyFont;
            float y = GapAboveFooter;

            if (references.Count > 0)
            {
                y = Heading(font, "Итоги", y);
                y = Subtitle(font, "В сессии участвуют:", y);
                foreach (var reference in references)
                {
                    // Captured into locals: the row's click runs long after this loop has moved on, and a
                    // closure over the iteration variable would send every row to the last object.
                    string kind = reference.Kind, id = reference.Id;
                    y = Row(font, reference.CurrentName, page.KindLabelFor(kind, id),
                            () => page.RouteLink(kind, id), y);
                }
            }

            if (backlinks.Count > 0)
            {
                if (references.Count > 0) y += GapBetweenLists;
                y = Heading(font, "Упомянута в", y);
                foreach (var backlink in backlinks)
                {
                    string pageId = backlink.SourcePageId;
                    y = Row(font, backlink.SourcePageName, backlink.SectionTitle,
                            () => page.RouteLink(NotesLinkOps.KindPage, pageId), y);
                }
            }

            element.preferredHeight = y + BottomPadding;
        }

        float Heading(Font font, string text, float y)
        {
            var label = Label(self, font, text, 14, ThemeRole.Txt, HeadingHeight, y, 0f);
            label.fontStyle = FontStyle.Bold;
            return y + HeadingHeight;
        }

        float Subtitle(Font font, string text, float y)
        {
            Label(self, font, text, 12, ThemeRole.Mut, SubtitleHeight, y, 0f);
            return y + SubtitleHeight;
        }

        /// <summary>One object: its current name, then its kind (or, for a backlink, the section it is named
        /// in) in the muted colour. A Button rather than a label, because a name in a summary is a way back to
        /// the thing it names — the same convention a navigator row and a link in the prose follow, reached
        /// through the same DocumentPageView.RouteLink so all three cannot disagree about where a name
        /// goes.</summary>
        float Row(Font font, string name, string aside, System.Action onClick, float y)
        {
            var rowGO = new GameObject("Row", typeof(RectTransform));
            rowGO.transform.SetParent(self, false);
            Stretch((RectTransform)rowGO.transform, RowHeight, y, 0f);

            // Invisible but still raycasts — otherwise the row is only clickable where its letters are.
            // Tagged at alpha 0 rather than given a bare transparent colour, which is the idiom
            // NavigatorView's own rows use and keeps every runtime graphic on this page themed.
            var image = rowGO.AddComponent<Image>();
            ThemeService.Tag(image, ThemeRole.Panel, 0f);

            var button = rowGO.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick?.Invoke());

            var rowRect = (RectTransform)rowGO.transform;
            var nameLabel = Label(rowRect, font, name ?? "", 13, ThemeRole.Accent, RowHeight, 0f, 12f);

            if (!string.IsNullOrEmpty(aside))
            {
                // Measured, not guessed: the aside starts where the name actually ends, so a short name does
                // not leave a column of air and a long one is not written over. preferredWidth is a pure
                // font measurement — it does not need a layout pass to have happened, which is what lets this
                // work on the very first Rebuild, before the column has a width at all.
                float offset = 12f + nameLabel.preferredWidth + 8f;
                Label(rowRect, font, "· " + aside, 12, ThemeRole.Mut, RowHeight, 0f, offset);
            }

            return y + RowHeight;
        }

        static Text Label(RectTransform parent, Font font, string text, int size, ThemeRole role,
                          float height, float y, float left)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, height, y, left);

            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            ThemeService.Tag(label, role);
            return label;
        }

        /// <summary>Full width, fixed height, hung from the top by `y`. Hand-stacked rather than given to a
        /// nested VerticalLayoutGroup so that every height here stays the arithmetic this class's doc
        /// promises.</summary>
        static void Stretch(RectTransform rect, float height, float y, float left)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(0f, -y);
        }
    }
}
