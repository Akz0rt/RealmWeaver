using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Every operation on a document page, as pure functions over the flat block list. Deliberately free of
    /// any UnityEngine reference so the whole layer runs in Tools/notes-harness without an Editor — the same
    /// arrangement InteriorOps / BrushOps / SettlementFence use.
    ///
    /// Nesting lives in DocBlock.Depth, not in a tree of children. Every structural operation is expressed
    /// through SubtreeLength, so "a block moves with its children" is implemented in exactly one place.
    ///
    /// Invariants the layer maintains (Validate reports violations, Normalize repairs what it can):
    ///   I1  Section => Depth 0; every other kind => Depth >= 1.
    ///   I2  Depth[n] &lt;= Depth[n-1] + 1, and a non-empty page starts with a Section.
    ///   I3  a non-empty LinkedPageId always resolves to a page that exists.
    ///   I4  a block never links to the page that contains it.
    ///   I5  at most one group carries IsReference.
    ///   I6  block ids are unique across the whole document.
    ///   I7  only Item takes children; Detail only on Item; ImageBytes/DisplayHeight only on Image;
    ///       LinkedPageId only on Item (optional) or BoardRef (required).
    ///   I8  a BoardRef's target page has Kind == Board.
    /// </summary>
    public static class NotesDocOps
    {
        public const string ReferenceGroupTitle = "Справочник";

        /// <summary>The Lazy-DM prep sheet, in order. Hints are placeholder text shown while a section or a
        /// row is empty — they are rendered, never stored, so they can be reworded without touching saves.</summary>
        static readonly (string Title, string Hint)[] Template =
        {
            ("Персонажи игроков", "Что каждый персонаж хочет и за какую его зацепку можно дёрнуть."),
            ("Сильное начало",    "Первая сцена целиком, готовая к чтению вслух. Единственный кусок, который пишется прозой."),
            ("Возможные сцены",   "5–10 однострочников. Не сюжет и не порядок — просто что может случиться."),
            ("Секреты и подсказки", "10 фактов, которые игроки могут узнать. Не привязывай к месту — выдавай там, где они окажутся."),
            ("Яркие локации",     "3 детали на место: что видно, что слышно, чем пахнет. Планировка уже есть на карте."),
            ("Важные NPC",        "Имя, одна строка внешности и главное — чего он хочет."),
            ("Монстры",           "Кто и сколько. Статблок — ссылкой или вставленным текстом."),
            ("Награды",           "Что можно унести: монеты, предметы, улики, сведения."),
        };

        static readonly Dictionary<string, string> hints = BuildHints();

        static Dictionary<string, string> BuildHints()
        {
            var d = new Dictionary<string, string>();
            foreach (var t in Template) d[t.Title] = t.Hint;
            return d;
        }

        /// <summary>Section title -> placeholder text. Keyed by title so a renamed section simply loses its
        /// hint instead of showing the wrong one.</summary>
        public static IReadOnlyDictionary<string, string> SectionHints => hints;

        // ── Creation ───────────────────────────────────────────────────────────

        public static DocBlock NewBlock(BlockKind kind, int depth, string text = "")
            => new DocBlock { Kind = kind, Depth = depth, Text = text ?? "" };

        public static NotesPage CreateSessionSheet(string name)
        {
            var page = new NotesPage { Name = name, Kind = PageKind.Document };
            foreach (var t in Template)
            {
                page.Blocks.Add(NewBlock(BlockKind.Section, 0, t.Title));
                // «Сильное начало» is the one section that is prose rather than a list, so it starts with an
                // empty Prose row: the DM should meet a paragraph there, not a bullet.
                if (t.Title == "Сильное начало")
                    page.Blocks.Add(NewBlock(BlockKind.Prose, 1));
            }
            return page;
        }

        // ── Lookup ─────────────────────────────────────────────────────────────

        public static NotesPage FindPage(NotesDocument doc, string pageId)
        {
            if (doc == null || string.IsNullOrEmpty(pageId)) return null;
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    if (p.Id == pageId) return p;
            return null;
        }

        public static DocBlock FindBlock(NotesDocument doc, string blockId, out NotesPage owner)
        {
            owner = null;
            if (doc == null || string.IsNullOrEmpty(blockId)) return null;
            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    foreach (var b in p.Blocks)
                        if (b.Id == blockId) { owner = p; return b; }
            return null;
        }

        static int IndexOf(IReadOnlyList<DocBlock> blocks, string blockId)
        {
            if (blocks == null || string.IsNullOrEmpty(blockId)) return -1;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].Id == blockId) return i;
            return -1;
        }

        /// <summary>1 plus the run of immediately-following blocks deeper than blocks[index]. The single
        /// place "with its children" is defined.</summary>
        public static int SubtreeLength(IReadOnlyList<DocBlock> blocks, int index)
        {
            if (blocks == null || index < 0 || index >= blocks.Count) return 0;
            int d = blocks[index].Depth, len = 1;
            while (index + len < blocks.Count && blocks[index + len].Depth > d) len++;
            return len;
        }

        /// <summary>The block a row at <paramref name="depth"/> would hang under, searching back from
        /// <paramref name="fromIndex"/> (exclusive). Null when that depth is top level.</summary>
        static DocBlock ParentFor(IReadOnlyList<DocBlock> blocks, int fromIndex, int depth)
        {
            for (int k = fromIndex - 1; k >= 0; k--)
                if (blocks[k].Depth == depth - 1) return blocks[k];
                else if (blocks[k].Depth < depth - 1) return null;
            return null;
        }

        static bool TakesChildren(DocBlock b) => b != null && (b.Kind == BlockKind.Item || b.Kind == BlockKind.Section);

        // ── Structure ──────────────────────────────────────────────────────────

        public static void Insert(List<DocBlock> blocks, int index, DocBlock block)
        {
            if (blocks == null || block == null) return;
            if (index < 0) index = 0;
            if (index > blocks.Count) index = blocks.Count;
            blocks.Insert(index, block);
        }

        public static void RemoveWithChildren(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return;
            blocks.RemoveRange(i, SubtreeLength(blocks, i));
        }

        public static bool MoveWithSubtree(List<DocBlock> blocks, string blockId, int dir)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0 || (dir != 1 && dir != -1)) return false;

            int d = blocks[i].Depth;
            int len = SubtreeLength(blocks, i);

            if (dir == 1)
            {
                int j = i + len;                                   // the next SIBLING, past our own subtree
                if (j >= blocks.Count || blocks[j].Depth != d) return false;
                int jlen = SubtreeLength(blocks, j);
                // Post-removal: the sibling's subtree slides down to start at i, so landing at i + jlen puts
                // us immediately after all of it.
                MoveRange(blocks, i, len, i + jlen);
                return true;
            }

            int k = i - 1;
            while (k >= 0 && blocks[k].Depth > d) k--;             // skip the previous sibling's children
            if (k < 0 || blocks[k].Depth < d) return false;        // hit our parent, or the top: no sibling
            MoveRange(blocks, i, len, k);                          // k is below i, so removal does not shift it
            return true;
        }

        public static bool MoveSubtreeTo(List<DocBlock> blocks, string blockId, int targetIndex, int targetDepth)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return false;
            int len = SubtreeLength(blocks, i);

            // A block cannot be dropped into its own subtree, nor onto where it already starts.
            if (targetIndex >= i && targetIndex < i + len) return false;
            if (targetIndex < 0 || targetIndex > blocks.Count) return false;

            var moved = blocks[i];
            if (moved.Kind == BlockKind.Section) targetDepth = 0;
            else if (targetDepth < 1) targetDepth = 1;

            // Shift the whole subtree so it keeps its internal shape, move it, then let ClampDepths pull down
            // anything the destination cannot legally hold. Clamping AFTER the move rather than guessing the
            // destination's neighbour beforehand means "legal depth" is decided in exactly one place, and the
            // drop preview (which asks ClampDepths the same question) can never disagree with the result.
            int shift = targetDepth - moved.Depth;
            if (shift != 0)
                for (int k = i; k < i + len; k++) blocks[k].Depth += shift;

            MoveRange(blocks, i, len, targetIndex > i ? targetIndex - len : targetIndex);
            ClampDepths(blocks);
            return true;
        }

        /// <summary>Moves the run [start, start+len) so that it begins at <paramref name="destination"/>,
        /// which is a POST-REMOVAL index — i.e. an index into the list as it looks once the run has been
        /// lifted out. Every caller converts to that convention itself; having this method "helpfully" adjust
        /// a pre-removal index was the one bug this suite caught, because callers that had already adjusted
        /// then got corrected twice and the move collapsed to a no-op.</summary>
        static void MoveRange(List<DocBlock> blocks, int start, int len, int destination)
        {
            var slice = blocks.GetRange(start, len);
            blocks.RemoveRange(start, len);
            if (destination < 0) destination = 0;
            if (destination > blocks.Count) destination = blocks.Count;
            blocks.InsertRange(destination, slice);
        }

        public static bool Indent(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i <= 0) return false;
            var b = blocks[i];
            if (b.Kind == BlockKind.Section) return false;          // I1

            int newDepth = b.Depth + 1;

            // The parent lookup is the ONLY guard needed here, and it enforces I2 as well as I7. A mutation
            // check proved an explicit `newDepth > blocks[i-1].Depth + 1` test could never fire: that
            // condition means the preceding block is shallower than this one, in which case ParentFor already
            // walks off the top and returns null. Keeping it would read as a safety net that nothing reaches.
            // I2 itself is pinned by Validate, not here.
            var parent = ParentFor(blocks, i, newDepth);
            if (!TakesChildren(parent)) return false;                 // no parent at all, or one that takes none

            int len = SubtreeLength(blocks, i);
            for (int k = i; k < i + len; k++) blocks[k].Depth++;
            return true;
        }

        public static bool Outdent(List<DocBlock> blocks, string blockId)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return false;
            var b = blocks[i];
            if (b.Kind == BlockKind.Section) return false;           // already at 0
            if (b.Depth <= 1) return false;                          // depth 0 is Section territory (I1)

            int len = SubtreeLength(blocks, i);
            for (int k = i; k < i + len; k++) blocks[k].Depth--;
            ClampDepths(blocks);
            return true;
        }

        public static List<int> VisibleIndices(IReadOnlyList<DocBlock> blocks)
        {
            var visible = new List<int>();
            if (blocks == null) return visible;

            int hideBelow = int.MaxValue;   // anything deeper than this is inside a collapsed block
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Depth > hideBelow) continue;
                hideBelow = blocks[i].Collapsed ? blocks[i].Depth : int.MaxValue;
                visible.Add(i);
            }
            return visible;
        }

        // ── Text editing ───────────────────────────────────────────────────────

        /// <summary>Splits a text row at the caret. The new half is inserted AFTER the original's whole
        /// subtree, so children stay with the block they were written under rather than silently
        /// re-parenting to the new half.</summary>
        public static DocBlock SplitAt(List<DocBlock> blocks, string blockId, int caretOffset)
        {
            int i = IndexOf(blocks, blockId);
            if (i < 0) return null;
            var b = blocks[i];
            if (b.Kind == BlockKind.Image || b.Kind == BlockKind.BoardRef) return null;   // no text to split

            string text = b.Text ?? "";
            if (caretOffset < 0) caretOffset = 0;
            if (caretOffset > text.Length) caretOffset = text.Length;

            var created = NewBlock(b.Kind, b.Depth, text.Substring(caretOffset));
            b.Text = text.Substring(0, caretOffset);
            Insert(blocks, i + SubtreeLength(blocks, i), created);
            return created;
        }

        public static bool MergeWithPrevious(List<DocBlock> blocks, string blockId, out int caretOffset)
        {
            caretOffset = 0;
            int i = IndexOf(blocks, blockId);
            if (i <= 0) return false;

            var b = blocks[i];
            var prev = blocks[i - 1];
            if (b.Kind == BlockKind.Image || b.Kind == BlockKind.BoardRef) return false;
            if (prev.Kind == BlockKind.Section) return false;        // never eat a section heading
            if (prev.Kind == BlockKind.Image || prev.Kind == BlockKind.BoardRef) return false;
            if (SubtreeLength(blocks, i) > 1) return false;          // merging a parent would orphan children

            caretOffset = (prev.Text ?? "").Length;
            prev.Text = (prev.Text ?? "") + (b.Text ?? "");
            blocks.RemoveAt(i);
            return true;
        }

        // ── Integrity ──────────────────────────────────────────────────────────

        public static List<string> Validate(NotesDocument doc)
        {
            var problems = new List<string>();
            if (doc == null) return problems;

            int referenceGroups = 0;
            var seenIds = new HashSet<string>();

            foreach (var g in doc.Groups)
            {
                if (g.IsReference) referenceGroups++;
                foreach (var p in g.Pages)
                {
                    if (p.Kind == PageKind.Board && p.Blocks != null && p.Blocks.Count > 0)
                        problems.Add($"page «{p.Name}» is a Board but carries {p.Blocks.Count} blocks");
                    if (p.Blocks == null) continue;

                    for (int i = 0; i < p.Blocks.Count; i++)
                    {
                        var b = p.Blocks[i];

                        if (!seenIds.Add(b.Id))
                            problems.Add($"duplicate block id {b.Id} (I6)");

                        if (b.Kind == BlockKind.Section && b.Depth != 0)
                            problems.Add($"section «{b.Text}» has Depth {b.Depth}, want 0 (I1)");
                        if (b.Kind != BlockKind.Section && b.Depth < 1)
                            problems.Add($"{b.Kind} block «{b.Text}» has Depth {b.Depth}, want >= 1 (I1)");

                        if (i == 0 && b.Kind != BlockKind.Section)
                            problems.Add($"page «{p.Name}» starts with {b.Kind}, want Section (I2)");
                        if (i > 0 && b.Depth > p.Blocks[i - 1].Depth + 1)
                            problems.Add($"block «{b.Text}» jumps from Depth {p.Blocks[i - 1].Depth} to {b.Depth} (I2)");
                        if (i > 0 && b.Depth > p.Blocks[i - 1].Depth && !TakesChildren(p.Blocks[i - 1]))
                            problems.Add($"block «{b.Text}» is a child of a {p.Blocks[i - 1].Kind} block (I7)");

                        if (!string.IsNullOrEmpty(b.Detail) && b.Kind != BlockKind.Item)
                            problems.Add($"{b.Kind} block «{b.Text}» carries Detail (I7)");
                        if (b.ImageBytes != null && b.Kind != BlockKind.Image)
                            problems.Add($"{b.Kind} block «{b.Text}» carries ImageBytes (I7)");

                        if (!string.IsNullOrEmpty(b.LinkedPageId))
                        {
                            if (b.Kind != BlockKind.Item && b.Kind != BlockKind.BoardRef)
                                problems.Add($"{b.Kind} block «{b.Text}» carries LinkedPageId (I7)");
                            if (b.LinkedPageId == p.Id)
                                problems.Add($"block «{b.Text}» links to its own page (I4)");
                            var target = FindPage(doc, b.LinkedPageId);
                            if (target == null)
                                problems.Add($"block «{b.Text}» links to missing page {b.LinkedPageId} (I3)");
                            else if (b.Kind == BlockKind.BoardRef && target.Kind != PageKind.Board)
                                problems.Add($"BoardRef «{b.Text}» targets a {target.Kind} page (I8)");
                        }
                        else if (b.Kind == BlockKind.BoardRef)
                            problems.Add($"BoardRef «{b.Text}» has no target (I7)");
                    }
                }
            }

            if (referenceGroups > 1)
                problems.Add($"{referenceGroups} groups carry IsReference, want at most 1 (I5)");

            return problems;
        }

        /// <summary>Idempotent, UNGATED repair — never keyed on a format version, following the convention
        /// ProjectSerializer.Load already documents ("a version guard is a thing the NEXT format bump forgets
        /// to widen"). Only ever touches a field it finds wrong, so re-running it is a no-op.</summary>
        public static void Normalize(NotesDocument doc)
        {
            if (doc == null) return;

            bool referenceSeen = false;
            foreach (var g in doc.Groups)
            {
                if (g.IsReference)
                {
                    if (referenceSeen) g.IsReference = false;      // I5
                    else referenceSeen = true;
                }

                foreach (var p in g.Pages)
                {
                    if (p.Blocks == null) p.Blocks = new List<DocBlock>();
                    if (p.Blocks.Count > 0 && p.Kind == PageKind.Board) p.Kind = PageKind.Document;

                    foreach (var b in p.Blocks)
                    {
                        if (b.Text == null) b.Text = "";
                        if (b.Kind == BlockKind.Section) b.Depth = 0;
                        else if (b.Depth < 1) b.Depth = 1;

                        if (b.Kind != BlockKind.Item && !string.IsNullOrEmpty(b.Detail)) b.Detail = null;
                        if (b.Kind != BlockKind.Image) { b.ImageBytes = null; b.DisplayHeight = 0f; }

                        if (!string.IsNullOrEmpty(b.LinkedPageId))
                        {
                            var target = FindPage(doc, b.LinkedPageId);
                            bool bad = b.Kind != BlockKind.Item && b.Kind != BlockKind.BoardRef
                                       || b.LinkedPageId == p.Id
                                       || target == null
                                       || b.Kind == BlockKind.BoardRef && target.Kind != PageKind.Board;
                            if (bad)
                            {
                                // A BoardRef with no reachable board is meaningless as a card, so it degrades
                                // to a plain row carrying what it used to point at — nothing vanishes silently.
                                if (b.Kind == BlockKind.BoardRef)
                                {
                                    b.Kind = BlockKind.Item;
                                    if (string.IsNullOrEmpty(b.Text) && target != null) b.Text = target.Name;
                                }
                                b.LinkedPageId = null;
                            }
                        }
                        else if (b.Kind == BlockKind.BoardRef)
                            b.Kind = BlockKind.Item;
                    }

                    ClampDepths(p.Blocks);
                }
            }
        }

        /// <summary>Walks a block list once and pulls any depth that violates I2 or I7 back to the deepest
        /// legal value. Shared by Outdent, MoveSubtreeTo and Normalize so "legal depth" has one definition.</summary>
        static void ClampDepths(List<DocBlock> blocks)
        {
            if (blocks == null) return;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Kind == BlockKind.Section) { b.Depth = 0; continue; }
                if (b.Depth < 1) b.Depth = 1;
                if (i == 0) continue;

                var prev = blocks[i - 1];
                int max = TakesChildren(prev) ? prev.Depth + 1 : prev.Depth;
                if (max < 1) max = 1;
                if (b.Depth > max) b.Depth = max;
            }
        }
    }
}
