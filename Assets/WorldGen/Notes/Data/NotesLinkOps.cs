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
