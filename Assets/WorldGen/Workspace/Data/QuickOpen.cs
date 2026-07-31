using System;
using System.Collections.Generic;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>One Ctrl+K result. Target and Title name the page this hit would open — quick-open never
    /// opens a block directly, the same "navigator opens pages" rule NavigatorTree.MakeNode follows — with
    /// TWO exceptions: the world-map hit (W1) targets the world map itself, since there is no page behind it
    /// to open instead; and a world-OBJECT hit (W2, e.g. a POI with no page yet) carries no page id at all —
    /// see World below. Kind is "" for a page-NAME hit, the source page's name for a body-TEXT hit (Q2 —
    /// "its Kind names the page it came from"), the fixed string "карта" for the world-map hit, and the
    /// object's own KindLabel (e.g. «город») for a world-object hit (W3) — four values that never collide, so
    /// the palette's right-hand column can always tell which kind of hit it is rendering. Snippet is null for
    /// every hit except a body-TEXT hit, where it is the matched fragment (with a little context) —
    /// QuickOpenSelfTests leans on exactly that null/non-null split to tell a name hit from a body hit apart.</summary>
    public class QuickHit
    {
        public SurfaceRef Target;
        public string Title;
        public string Kind;
        public string Snippet;

        /// <summary>Non-null ONLY for a world-object hit (W2) — the world identity to open, for the one case
        /// where Target cannot yet name a page because none exists. Null for every other hit kind.
        /// QuickOpenPopup.Choose branches on this: World != null means "create-or-find the page THEN open
        /// it" (NotesDocOps.EnsurePageFor), everything else opens Target unchanged.</summary>
        public WorldRef World;
    }

    /// <summary>
    /// Ctrl+K search over page names and page body text. A pure function of a NotesDocument, following the
    /// same free-of-UnityEngine arrangement as NavigatorTree and WorkspaceOps, so it runs in
    /// Tools/notes-harness without an Editor. Folding follows NavigatorTree.Matches's own idiom —
    /// Trim().ToLowerInvariant(), nothing fancier — rather than inventing a second folding scheme.
    ///
    /// Q1: a page-NAME match always outranks a body-TEXT match, and within either category a PREFIX match
    /// (the folded field starts with the needle) outranks a mid-field one. Four ranks, ascending = better:
    /// name-prefix, name-contains, body-prefix, body-contains — see the Collect* helpers below.
    ///
    /// Q3 — a PERFORMANCE constraint, not a style preference, guarded by construction: CollectBodyHits below
    /// reads ONLY DocBlock.Text and DocBlock.Detail, the two text-bearing fields. DocBlock.ImageBytes (and
    /// any canvas payload — pages don't even carry one) is NEVER dereferenced here. A page can embed
    /// megabytes of pictures, and Search runs on every keystroke typed into the Ctrl+K box, so "just search
    /// everything" would make every keystroke walk that data. Do not widen this loop to read ImageBytes,
    /// however tempting that looks to a future edit.
    ///
    /// W1 — the world map is a candidate on every search too, so closing its tab (WorkspaceOps.CloseTab)
    /// never makes it unreachable from Ctrl+K. It is matched against WorkspaceOps.DefaultWorldMapTitle with
    /// the exact same fold-and-rank rule CollectNameHit uses for a page NAME — same NamePrefix/NameContains
    /// ranks, same idx==0 prefix test — rather than a third, fixed-position "always show it first" scheme.
    /// CollectWorldMapHit is called after the body-hit loop but BEFORE the name-hit loop, so its Seq (the
    /// sort's tie-breaker for equal ranks) is always lower than any page-name hit's — the plan's "ordered
    /// before page hits of equal rank" falls out of that placement, with no second sort key needed.
    ///
    /// W1-W5 — every WORLD OBJECT (a POI today; see WorldObjectRef's own doc for why settlements/buildings
    /// can join later without a second search path) is ALSO a candidate on every search, for the identical
    /// reason the world map is: the spec's explicit promise is "everything absent is still one keystroke
    /// away in Ctrl+K" (task-10b-brief.md), and a placed-but-unopened POI is exactly such an absence.
    /// CollectWorldHits sits in the SAME spot CollectWorldMapHit does — after body hits, before the name-hit
    /// loop — folded and ranked by the identical NamePrefix/NameContains rule (W1), for the same reason:
    /// a world object is, for ranking purposes, one more page-NAME candidate. W4 suppresses a world object
    /// once it already has a bound page, so a worked-on place never yields two rows that open the same
    /// target — see FindPageBoundTo's own doc for why that check is NOT reimplemented here.
    /// </summary>
    public static class QuickOpen
    {
        const int NamePrefix = 0;
        const int NameContains = 1;
        const int BodyPrefix = 2;
        const int BodyContains = 3;

        /// <summary>Characters of context kept on each side of a match when building a Snippet (Q2).</summary>
        const int SnippetContext = 24;

        /// <summary>Q4: folding is Trim().ToLowerInvariant(); an empty (or whitespace-only) query returns an
        /// empty list, never "everything". Q5: results are capped at <paramref name="limit"/>. W5: `world`
        /// may be null OR empty — that is the pre-generation state (no POIs placed yet, or PoiManager not
        /// found — see QuickOpenPopup.Attach), not an error, so this never throws or special-cases it beyond
        /// CollectWorldHits' own null check.</summary>
        public static List<QuickHit> Search(NotesDocument doc, IReadOnlyList<WorldObjectRef> world, string query, int limit = 20)
        {
            var result = new List<QuickHit>();
            if (doc == null) return result;
            if (limit < 0) limit = 0;

            string needle = (query ?? "").Trim().ToLowerInvariant();
            if (needle.Length == 0) return result;   // Q4 — empty is empty, not everything.

            // Body hits are gathered into `candidates` BEFORE name hits, and every candidate carries the
            // Seq it was added at as an explicit sort tie-breaker. That makes the eventual sort deterministic
            // without depending on List.Sort's (unstable) implementation — and it means that if a future edit
            // ever collapses the name/body ranks to the same value (the very regression Q1 exists to
            // prevent), the body hits — added first, so lower Seq — would then sort AHEAD of the page-name
            // hit they must lose to, and QuickOpenSelfTests' very first assertion fails loudly instead of
            // passing by accident of whatever order pages happen to be stored in.
            var candidates = new List<(int Rank, int Seq, QuickHit Hit)>();
            int seq = 0;

            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    CollectBodyHits(p, needle, candidates, ref seq);

            // W1 — see the class comment above for why this sits exactly HERE: after body hits (whose ranks
            // are strictly worse, so their relative Seq order doesn't matter) but before the name-hit loop
            // (whose ranks it can TIE with), so its Seq always wins that tie.
            CollectWorldMapHit(needle, candidates, ref seq);

            // W1-W5 — same placement reasoning as CollectWorldMapHit immediately above; see the class doc.
            CollectWorldHits(doc, world, needle, candidates, ref seq);

            foreach (var g in doc.Groups)
                foreach (var p in g.Pages)
                    CollectNameHit(p, needle, candidates, ref seq);

            candidates.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Seq.CompareTo(b.Seq));

            for (int i = 0; i < candidates.Count && result.Count < limit; i++)   // Q5 — capped, never more.
                result.Add(candidates[i].Hit);

            return result;
        }

        /// <summary>W1 — the world map behaves like one more page NAME for ranking purposes, folded and
        /// ranked by the exact same rule CollectNameHit uses (see that method — this deliberately does not
        /// invent a second folding scheme). Target is byte-identical to WorkspaceOps.NewDefault's own seed
        /// tab (Kind=WorldMap, Id="") — see WorkspaceOps.SameSurface — so opening this hit through
        /// WorkspaceController.Open focuses the world map's existing tab instead of adding a second one, the
        /// same requirement NavigatorTree's Pinned node carries for the same reason.</summary>
        static void CollectWorldMapHit(string needle, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            string folded = WorkspaceOps.DefaultWorldMapTitle.Trim().ToLowerInvariant();
            int idx = folded.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return;

            int rank = idx == 0 ? NamePrefix : NameContains;
            candidates.Add((rank, seq++, new QuickHit
            {
                Target = new SurfaceRef { Kind = SurfaceKind.WorldMap, Id = "" },
                Title = WorkspaceOps.DefaultWorldMapTitle,
                Kind = "карта",
                Snippet = null,
            }));
        }

        /// <summary>W1-W5 — every candidate `world` object, folded and ranked exactly like CollectWorldMapHit
        /// folds and ranks the world map (same NamePrefix/NameContains rule, no new ranking constants). W4:
        /// an object already represented by a page (NotesDocOps.FindPageBoundTo — the SAME Kind+Id predicate
        /// EnsurePageFor and NavigatorTree's own Bound scan agree on, not a second one invented here) is
        /// skipped entirely — the page hit from the name-hit loop below already stands for it, so showing
        /// both would open the same target from two different rows. W5: a null `world` is the pre-generation
        /// state, not an error — this loop simply has nothing to add.</summary>
        static void CollectWorldHits(NotesDocument doc, IReadOnlyList<WorldObjectRef> world, string needle,
            List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (world == null) return;
            foreach (var w in world)
            {
                if (w == null) continue;
                if (NotesDocOps.FindPageBoundTo(doc, w.Kind, w.Id) != null) continue;   // W4

                string folded = (w.Name ?? "").Trim().ToLowerInvariant();
                int idx = folded.IndexOf(needle, StringComparison.Ordinal);
                if (idx < 0) continue;

                int rank = idx == 0 ? NamePrefix : NameContains;   // W1
                candidates.Add((rank, seq++, new QuickHit
                {
                    // W2 — no page exists yet for a fresh world object, so Target carries no page id; the
                    // world identity travels on World instead, and QuickOpenPopup.Choose creates the page
                    // only at the moment the user actually picks this row (not on every keystroke that
                    // merely matches it — see Choose's own comment).
                    Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = "" },
                    World = new WorldRef { Kind = w.Kind, Id = w.Id },
                    Title = w.Name,
                    Kind = w.KindLabel,   // W3
                    Snippet = null,
                }));
            }
        }

        static void CollectNameHit(NotesPage p, string needle, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            string folded = (p.Name ?? "").Trim().ToLowerInvariant();
            int idx = folded.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return;

            int rank = idx == 0 ? NamePrefix : NameContains;
            candidates.Add((rank, seq++, new QuickHit
            {
                Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = p.Id },
                Title = p.Name,
                Kind = "",
                Snippet = null,
            }));
        }

        static void CollectBodyHits(NotesPage p, string needle, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (p.Blocks == null) return;
            foreach (var b in p.Blocks)
            {
                // Q3 guard — see the class comment above. b.Text and b.Detail are the ONLY fields read here;
                // b.ImageBytes is never touched, regardless of how large it is.
                TryAddBodyHit(p, b.Text, needle, candidates, ref seq);
                TryAddBodyHit(p, b.Detail, needle, candidates, ref seq);
            }
        }

        static void TryAddBodyHit(NotesPage p, string field, string needle, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (string.IsNullOrEmpty(field)) return;

            // NOT trimmed before folding, unlike the page name and the query: the match index found here is
            // used, unmodified, to slice the ORIGINAL (untrimmed, un-lowered) `field` for the Snippet below —
            // trimming would shift indices between the two strings out of alignment.
            string folded = field.ToLowerInvariant();
            int idx = folded.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return;

            int rank = idx == 0 ? BodyPrefix : BodyContains;
            candidates.Add((rank, seq++, new QuickHit
            {
                Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = p.Id },
                Title = p.Name,
                Kind = p.Name,
                Snippet = MakeSnippet(field, idx, needle.Length),
            }));
        }

        /// <summary>Q2 — "the matched fragment with a little context": a window of SnippetContext characters
        /// either side of the match, with an ellipsis marking whichever end was actually trimmed away.</summary>
        static string MakeSnippet(string field, int matchIndex, int needleLength)
        {
            int start = Math.Max(0, matchIndex - SnippetContext);
            int end = Math.Min(field.Length, matchIndex + needleLength + SnippetContext);
            string core = field.Substring(start, end - start);
            string prefix = start > 0 ? "…" : "";
            string suffix = end < field.Length ? "…" : "";
            return prefix + core + suffix;
        }
    }
}
