using System;
using System.Collections.Generic;

namespace WorldGen.Notes.Data
{
    /// <summary>One inline link found inside a block's text. SourceStart/SourceLength cut the WHOLE token,
    /// brackets included, so a caller can replace it without re-scanning.</summary>
    public class LinkSpan
    {
        public string Kind;
        public string Id;
        /// <summary>The name as STORED. A fallback, never the source of truth: while the target exists its
        /// current name is looked up instead (see NotesLinkOps' class doc). Kept so a sentence whose target
        /// was deleted still reads as prose.</summary>
        public string Display;
        public int SourceStart;
        public int SourceLength;
    }

    public class DisplaySpan : LinkSpan
    {
        /// <summary>False when the resolver returned null — the target is gone and Display's stored copy is
        /// being shown instead. The renderer draws these as muted prose with no click target, and the
        /// reference index drops them entirely (R3).</summary>
        public bool Resolved;
        public int DisplayStart;
        public int DisplayLength;
    }

    /// <summary>Source text rendered for reading, plus the two-way index map that makes clicking it usable.
    ///
    /// The map exists for ONE job: the DM clicks a character in the rendered row, and the caret has to land
    /// on the corresponding character of the RAW row that replaces it. Without it, every click into a row
    /// containing a link would miss by exactly the machinery's length.</summary>
    public class DisplayText
    {
        public string Text = "";
        public List<DisplaySpan> Spans = new List<DisplaySpan>();

        /// <summary>Display index → source index. An index strictly inside a rendered NAME collapses to the
        /// token's start: there is no meaningful "middle of a token" for a caret to sit at, and a DM who
        /// clicks a link's third letter means "edit this link", which begins at its beginning. Both
        /// BOUNDARIES stay exact — the index at a link's first character maps to the token's first character,
        /// and the index just past its last maps just past the closing brackets, which is where a click to
        /// the right of a link lands.</summary>
        public int ToSource(int displayIndex)
        {
            int delta = 0;
            foreach (var s in Spans)
            {
                // `<=` rather than `<`, and the difference is EQUIVALENT, not load-bearing — checked by
                // mutation, where `<` survived. At displayIndex == DisplayStart the two paths agree by
                // construction: BuildDisplay lays each span down so SourceStart == DisplayStart + delta, so
                // breaking here and falling through to the collapse below return the same number. The break
                // is still needed for indices STRICTLY before the span, which would otherwise be swallowed
                // by the collapse. Do not spend a second round proving this again.
                if (displayIndex <= s.DisplayStart) break;
                if (displayIndex < s.DisplayStart + s.DisplayLength) return s.SourceStart;
                delta += s.SourceLength - s.DisplayLength;
            }
            return displayIndex + delta;
        }

        /// <summary>Source index → display index, with the same collapse in the other direction.</summary>
        public int ToDisplay(int sourceIndex)
        {
            int delta = 0;
            foreach (var s in Spans)
            {
                if (sourceIndex <= s.SourceStart) break;
                if (sourceIndex < s.SourceStart + s.SourceLength) return s.DisplayStart;
                delta += s.SourceLength - s.DisplayLength;
            }
            return sourceIndex - delta;
        }
    }

    /// <summary>
    /// The inline-link grammar: [[kind:id|Отображаемое имя]], where kind is "page" or one of the three
    /// WorldRefKind names lower-cased.
    ///
    /// WHY AN ID AND NOT A NAME. Name-based links (the Obsidian default) break on rename unless every rename
    /// site rewrites every mention, and two POIs may legitimately share a name, which name lookup cannot
    /// disambiguate at all. This project has ruled against name-based lookup once already — PageGroup
    /// .IsReference is a role flag precisely because "the user can rename any group".
    ///
    /// WHY THE NAME IS STORED ANYWAY, AND WHY IT IS NOT AUTHORITATIVE. Rendering ALWAYS resolves the current
    /// name by id, so a rename is visible everywhere the instant it happens and cannot fall behind. The
    /// stored copy is read in exactly one case — the target no longer exists — so the sentence degrades to
    /// prose instead of a hole. The alternative, rewriting every mention on every rename, needs a hook at
    /// every site that can rename anything, and the failure mode of forgetting one is a silently stale link.
    ///
    /// L1 IS THE SAFETY RULE: anything that does not parse is prose. There is no escape syntax and no error
    /// state, so a DM who types "[[" in the middle of a sentence gets "[[" and not a diagnostic.
    /// </summary>
    public static class NotesLinkOps
    {
        /// <summary>Answers "what is this object called RIGHT NOW", or null when it no longer exists. The
        /// pure layer never implements one: the harness passes a dictionary, the app passes a lookup over the
        /// document and the world. That is what makes "resolve, never rewrite" testable without Unity.
        ///
        /// Nested in NotesLinkOps rather than left at namespace scope so that every consumer names it
        /// NotesLinkOps.NameResolver — the grammar owns the contract, and a bare `NameResolver` floating in
        /// WorldGen.Notes.Data would invite a second, unrelated one.</summary>
        public delegate string NameResolver(string kind, string id);

        public const string KindPage = "page";
        const string Open = "[[";
        const string Close = "]]";

        public static bool TryParseWorldKind(string kind, out WorldRefKind worldKind)
        {
            switch (kind)
            {
                case "poi":      worldKind = WorldRefKind.Poi;      return true;
                case "building": worldKind = WorldRefKind.Building; return true;
                case "room":     worldKind = WorldRefKind.Room;     return true;
                default:         worldKind = WorldRefKind.Poi;      return false;
            }
        }

        /// <summary>TryParseWorldKind's inverse, so a token built from a WorldRef reads back as that WorldRef.
        /// The two live next to each other because a disagreement between them would be silent: a token would
        /// simply stop resolving, with nothing to show it had ever been well-formed.</summary>
        public static string KindOf(WorldRefKind worldKind)
        {
            switch (worldKind)
            {
                case WorldRefKind.Building: return "building";
                case WorldRefKind.Room:     return "room";
                default:                    return "poi";
            }
        }

        static bool IsKnownKind(string kind)
            => kind == KindPage || kind == "poi" || kind == "building" || kind == "room";

        public static string MakeToken(string kind, string id, string display)
            => Open + kind + ":" + id + "|" + display + Close;

        /// <summary>Every well-formed token in source order. L1: a malformed candidate contributes nothing and
        /// the scan resumes just past its OPENING bracket rather than past the whole failure — otherwise one
        /// stray "[[" would swallow the real token that starts inside it.</summary>
        public static List<LinkSpan> ParseSpans(string source)
        {
            var found = new List<LinkSpan>();
            if (string.IsNullOrEmpty(source)) return found;

            int at = 0;
            while (true)
            {
                int open = source.IndexOf(Open, at, StringComparison.Ordinal);
                if (open < 0) break;

                int close = source.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);
                if (close < 0) break;

                var span = TryBuild(source.Substring(open + Open.Length, close - open - Open.Length));
                if (span != null)
                {
                    span.SourceStart = open;
                    span.SourceLength = close + Close.Length - open;
                    found.Add(span);
                    at = close + Close.Length;
                }
                else
                {
                    at = open + Open.Length;
                }
            }
            return found;
        }

        /// <summary>Substitutes every well-formed token with its target's CURRENT name, falling back to the
        /// copy stored in the token only when the resolver says the target is gone. Everything outside a
        /// token is copied verbatim, so prose is never touched.
        ///
        /// A null resolver means "nothing resolves", not an error: a view calls this before the document is
        /// wired, and the stored copies are exactly the right thing to show then.</summary>
        public static DisplayText BuildDisplay(string source, NameResolver resolve)
        {
            var result = new DisplayText();
            if (string.IsNullOrEmpty(source)) return result;

            var spans = ParseSpans(source);
            if (spans.Count == 0) { result.Text = source; return result; }

            var sb = new System.Text.StringBuilder(source.Length);
            int copied = 0;
            foreach (var span in spans)
            {
                sb.Append(source, copied, span.SourceStart - copied);

                string current = resolve != null ? resolve(span.Kind, span.Id) : null;
                string shown = current ?? span.Display;

                result.Spans.Add(new DisplaySpan
                {
                    Kind = span.Kind,
                    Id = span.Id,
                    Display = span.Display,
                    SourceStart = span.SourceStart,
                    SourceLength = span.SourceLength,
                    Resolved = current != null,
                    DisplayStart = sb.Length,
                    DisplayLength = shown.Length,
                });

                sb.Append(shown);
                copied = span.SourceStart + span.SourceLength;
            }
            sb.Append(source, copied, source.Length - copied);

            result.Text = sb.ToString();
            return result;
        }

        /// <summary>The inside of a candidate's brackets, or null if it is not a token at all. Note that `id`
        /// is cut at the FIRST pipe after the colon, so it can never itself contain one — a guard against that
        /// would be unreachable code.</summary>
        static LinkSpan TryBuild(string body)
        {
            int colon = body.IndexOf(':');
            if (colon <= 0) return null;
            int pipe = body.IndexOf('|', colon + 1);
            if (pipe < 0) return null;

            string kind = body.Substring(0, colon);
            string id = body.Substring(colon + 1, pipe - colon - 1);
            string display = body.Substring(pipe + 1);

            if (!IsKnownKind(kind)) return null;
            if (string.IsNullOrEmpty(id)) return null;
            if (string.IsNullOrEmpty(display)) return null;

            return new LinkSpan { Kind = kind, Id = id, Display = display };
        }
    }
}
