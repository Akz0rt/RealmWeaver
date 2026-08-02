using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Ctrl+F on the open page: every place a query occurs, in reading order.
    ///
    /// IT SEARCHES WHAT THE DM SEES, NOT WHAT IS STORED — the same rule QuickOpen's body search already
    /// follows, and for the same reason. A link is stored as `[[poi:3f2a…|Тихий Брод]]` and rendered as
    /// «Тихий Брод», so a search of the source would fail to find «Брод» in a sentence that plainly contains
    /// it, and would instead match on «poi», on a guid, and on brackets nobody typed. Every offset this
    /// returns is therefore a DISPLAY offset, which is also exactly what a highlight has to be expressed in.
    ///
    /// Detail is NOT searched. DocBlockView renders only Text, so a hit inside a Detail string would be a
    /// result the DM cannot be shown, and «3 из 12» would count places they can never reach.
    ///
    /// COLLAPSED SECTIONS ARE SEARCHED. Hiding a section is a reading decision, not a filter — a DM looking
    /// for a name wants it found wherever it is, and the search bar's own job is to open the section it
    /// landed in. That is why this takes the whole block list rather than VisibleIndices.
    ///
    /// Pure: no UnityEngine, so the harness runs it.
    /// </summary>
    public static class PageSearch
    {
        /// <summary>One occurrence. `BlockIndex` is an index into the list that was searched — the caller's
        /// Page.Blocks — so a caller can reach the block without a second lookup, while `BlockId` is what
        /// survives the rebuild that a jump-to-hit causes.</summary>
        public class PageHit
        {
            public string BlockId;
            public int BlockIndex;
            /// <summary>Offset into the block's DISPLAY text, not its stored text.</summary>
            public int DisplayStart;
            public int Length;
        }

        /// <summary>Every occurrence of `query`, in document order.
        ///
        /// NON-OVERLAPPING, advancing past each match: «аа» in «ааа» is one hit, not two, because that is
        /// what «2 из 5» has to mean for a DM stepping through them with Enter. Case-insensitive by ordinal
        /// folding, which covers Cyrillic and does not depend on the machine's locale — a page must not find
        /// a different number of things on a different computer.
        ///
        /// An empty or whitespace-only query finds nothing rather than everything: the bar is open and empty
        /// for as long as it takes to type the first letter, and lighting the whole page up in that moment
        /// would be the opposite of helping.</summary>
        public static List<PageHit> Find(IReadOnlyList<DocBlock> blocks, string query,
                                         NotesLinkOps.NameResolver resolve)
        {
            var hits = new List<PageHit>();
            if (blocks == null || string.IsNullOrEmpty(query) || string.IsNullOrWhiteSpace(query)) return hits;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || string.IsNullOrEmpty(block.Text)) continue;
                if (block.Kind == BlockKind.Image) continue;   // no text to see, whatever it may store
                                                              // (Canvas is NOT skipped — its caption is text)

                string display = NotesLinkOps.BuildDisplay(block.Text, resolve).Text;
                if (string.IsNullOrEmpty(display)) continue;

                int from = 0;
                while (from <= display.Length - query.Length)
                {
                    int at = display.IndexOf(query, from, System.StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;
                    hits.Add(new PageHit
                    {
                        BlockId = block.Id,
                        BlockIndex = i,
                        DisplayStart = at,
                        Length = query.Length,
                    });
                    from = at + query.Length;
                }
            }
            return hits;
        }

        /// <summary>Which hit comes after `current`, wrapping round. Returns -1 for an empty list.
        ///
        /// WRAPPING IS THE POINT, not a nicety: a DM who has stepped to the last match and presses Enter
        /// again means «keep going», and a search that silently stops reads as a search that broke. `forward
        /// = false` walks the other way, for Shift+Enter, and wraps the same.</summary>
        public static int Step(int count, int current, bool forward)
        {
            if (count <= 0) return -1;
            if (current < 0) return forward ? 0 : count - 1;
            int next = forward ? current + 1 : current - 1;
            if (next >= count) return 0;
            if (next < 0) return count - 1;
            return next;
        }
    }
}
