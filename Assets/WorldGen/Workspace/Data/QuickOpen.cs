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
        /// where Target names no page because no page is involved at all. Null for every other hit kind.
        /// QuickOpenPopup.ChooseIndex branches on this: World != null means "open THE PLACE" — its editor, via
        /// WorldSurface.PoiEditor, per Task 10e's ruling that a point of interest IS its editor menu — while
        /// everything else opens Target unchanged. Until Task 10e that branch created a page (EnsurePageFor) and
        /// opened it instead; the identity carried here is the same either way, which is why the retarget touched
        /// no rule in this file.</summary>
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
    /// CollectWorldMapHit is called BEFORE every doc-dependent candidate (body hits, world-object hits, name
    /// hits), so its Seq (the sort's tie-breaker for equal ranks) is always lower than any of theirs — the
    /// plan's "ordered before page hits of equal rank" falls out of that placement, with no second sort key
    /// needed. It is ALSO called unconditionally, above Search's `doc == null` guard: unlike every other
    /// candidate here, opening the world map needs no document at all (it targets the surface directly, not
    /// a page — see QuickHit's own doc), so gating it on `doc` would make Ctrl+K unable to offer the one
    /// doc-independent escape hatch back to the map in exactly the scene state (no document resolved) that
    /// most needs one. Mirrors NavigatorTree.Build's «Мир» group, built above THAT method's doc==null guard
    /// for the identical reason (Task 10b did the same for the row «Мир» then called Pinned; Task 10e folded
    /// that row into «Мир» itself and the doc-independence moved with it) — a review of this task's own
    /// history flagged the same shape as Important there and it was fixed by the same move.
    ///
    /// W1-W5 — every WORLD OBJECT (a POI today; see WorldObjectRef's own doc for why settlements/buildings
    /// can join later without a second search path) is ALSO a candidate on every search, for the identical
    /// reason the world map is: the spec's explicit promise is "everything absent is still one keystroke
    /// away in Ctrl+K" (task-10b-brief.md), and a placed-but-unopened POI is exactly such an absence. It sits
    /// beside CollectWorldMapHit, ABOVE the `doc == null` guard and before the name-hit loop, folded and
    /// ranked by the identical NamePrefix/NameContains rule (W1) — a world object is, for ranking purposes,
    /// one more page-NAME candidate, and its Seq stays below every page hit's exactly as before that move
    /// (world-object hits rank 0/1, body hits 2/3, so Seq never breaks a tie between the two).
    ///
    /// EVERY POI IS A CANDIDATE, ALWAYS — W4 IS GONE (Task 10e, review round 1). Task 10b suppressed a world
    /// object once some page's Bound named it (NotesDocOps.FindPageBoundTo), on the reasoning that the page
    /// hit already stood for it and two rows would open one target. That reasoning died when a world row
    /// stopped creating-and-opening that very page and started opening the PLACE: a note and the place it is
    /// about are two different objects under the Task 10c ruling, so two rows opening two different things is
    /// CORRECT, not duplication. What the rule did instead was hide a place behind its own note — for exactly
    /// the POIs the DM has worked on hardest — which makes false the promise the Task 10e brief accepts the
    /// «Мир» drowning risk ON: «Ctrl+K still reaches everything». A rule that contradicts the document
    /// authorising it does not stand on seniority. (It was live, not theoretical: Bound is persisted at
    /// format 13, so any project saved after the Task 10c checkpoint already carries bound pages.)
    ///
    /// THE `doc` PARAMETER WENT WITH IT, AND THAT IS THE POINT. FindPageBoundTo was the only thing
    /// CollectWorldHits read a document for; with it gone the method cannot take one, so the candidate cannot
    /// be gated on one — it moved above Search's `doc == null` guard by necessity rather than by choice. The
    /// signature is the guarantee: re-introducing the gate would mean re-introducing the parameter, in the
    /// open. This arc has now fixed "a result gated on something it does not depend on" four times (the
    /// pinned row in the checkpoint-3 round, the world-map hit in Task 10b, NavigatorTree's «Мир» and
    /// NavigatorView's own document guard in Task 10e); this is the fifth, and the last one that can be made
    /// unreintroducible by construction.
    ///
    /// KNOWN WEAKNESS, deliberately not solved here: a page NAME hit carries Kind = "" (CollectNameHit), so a
    /// place's row and its note's row differ only by «город» against a blank right-hand column. They are
    /// distinguishable but weakly. Labelling page hits («заметка») is a separate improvement and is NOT a
    /// reason to suppress either row.
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
        /// CollectWorldHits' own null check. `doc` may ALSO be null (no NotesRootBuilder resolved yet), and
        /// as of Task 10e that only costs PAGE hits: both world candidates are collected above the guard —
        /// see the class doc for why neither can be gated on a document any more.</summary>
        public static List<QuickHit> Search(NotesDocument doc, IReadOnlyList<WorldObjectRef> world, string query, int limit = 20)
        {
            var result = new List<QuickHit>();
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

            // BOTH world candidates are unconditional, ABOVE the `doc == null` check below: neither the world
            // map nor a world OBJECT is reachable through a page any more, so neither has a document to be
            // gated on. CollectWorldHits does not merely happen to sit here — it cannot take a `doc` to check
            // (see the class doc's W1-W5 paragraph), which is what stops the gate coming back quietly.
            //
            // Order is unchanged by that move: world-object hits rank 0/1 and body hits 2/3, so Seq never
            // decides between them, and worldmap < world < name within rank 0/1 is the same sequence it was.
            CollectWorldMapHit(needle, candidates, ref seq);
            CollectWorldHits(world, needle, candidates, ref seq);

            // Page hits are the only candidates left that genuinely need a document — they are read straight
            // off doc.Groups.
            if (doc != null)
            {
                // Built once and handed down: body text is folded through NotesLinkOps.BuildDisplay, so what
                // is searched is what the DM sees. See CollectBodyHits for why that is a correctness rule
                // and not a nicety.
                var resolve = ResolverFor(doc, world);

                foreach (var g in doc.Groups)
                    foreach (var p in g.Pages)
                        CollectBodyHits(p, needle, resolve, candidates, ref seq);

                foreach (var g in doc.Groups)
                    foreach (var p in g.Pages)
                        CollectNameHit(p, needle, candidates, ref seq);
            }

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
        /// same requirement NavigatorTree's own world-map row (the first node of «Мир») carries for the same
        /// reason.</summary>
        static void CollectWorldMapHit(string needle, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (!TryFoldMatch(WorkspaceOps.DefaultWorldMapTitle, needle, out bool isPrefix)) return;

            int rank = isPrefix ? NamePrefix : NameContains;
            candidates.Add((rank, seq++, new QuickHit
            {
                Target = new SurfaceRef { Kind = SurfaceKind.WorldMap, Id = "" },
                Title = WorkspaceOps.DefaultWorldMapTitle,
                Kind = "карта",
                Snippet = null,
            }));
        }

        /// <summary>W1-W5 — every candidate `world` object, folded and ranked exactly like CollectWorldMapHit
        /// folds and ranks the world map (same NamePrefix/NameContains rule, no new ranking constants). EVERY
        /// object, unconditionally: the W4 suppression of objects that already had a bound page was removed in
        /// Task 10e's review round, along with the `doc` this method used to take for it — see the class doc.
        /// W5: a null `world` is the pre-generation state, not an error — this loop simply has nothing to
        /// add.</summary>
        static void CollectWorldHits(IReadOnlyList<WorldObjectRef> world, string needle,
            List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (world == null) return;
            foreach (var w in world)
            {
                if (w == null) continue;

                if (!TryFoldMatch(w.Name, needle, out bool isPrefix)) continue;

                int rank = isPrefix ? NamePrefix : NameContains;   // W1
                candidates.Add((rank, seq++, new QuickHit
                {
                    // W2 — a world object is not a page, so Target carries no page id; the world identity
                    // travels on World instead, and QuickOpenPopup.ChooseIndex turns it into the place's own
                    // editor surface at the moment the user picks the row. (Task 10b created a page there
                    // instead — hence "no page EXISTS YET", the phrasing this comment used to carry.)
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
            if (!TryFoldMatch(p.Name, needle, out bool isPrefix)) return;

            int rank = isPrefix ? NamePrefix : NameContains;
            candidates.Add((rank, seq++, new QuickHit
            {
                Target = new SurfaceRef { Kind = SurfaceKind.Page, Id = p.Id },
                Title = p.Name,
                Kind = "",
                Snippet = null,
            }));
        }

        /// <summary>THE one substring-match rule this class uses for a NAME-style field — fold with
        /// Trim().ToLowerInvariant(), find the (already-folded) `needle` by ordinal IndexOf, and report
        /// whether the match sits at index 0 (a prefix) or merely somewhere inside the field. Deliberately
        /// NOT what CollectBodyHits uses — that fold is UN-trimmed on purpose (see its own comment, S1), so
        /// it stays a second, separate rule rather than being folded into this one.
        ///
        /// EXTRACTED FOR TASK 10a, not duplicated a fourth time: MentionSuggest (Notes/Data) ranks its own
        /// name/subtitle candidates and the DM's ruling is that "@" must reach exactly what Ctrl+K reaches —
        /// two hand-written copies of "fold, IndexOf, idx==0" are two chances for that to quietly stop being
        /// true. CollectWorldMapHit/CollectWorldHits/CollectNameHit above call this now instead of repeating
        /// the same three lines; their ranking (NamePrefix/NameContains) and result order are unchanged —
        /// this only names the rule they already shared.</summary>
        internal static bool TryFoldMatch(string field, string needle, out bool isPrefix)
        {
            string folded = (field ?? "").Trim().ToLowerInvariant();
            int idx = folded.IndexOf(needle, StringComparison.Ordinal);
            isPrefix = idx == 0;
            return idx >= 0;
        }

        /// <summary>Turns a search hit into the three parts of an inline link, or refuses.
        ///
        /// WHICH HALF CARRIES THE IDENTITY IS NOT SYMMETRICAL, which is the whole reason this is a function
        /// and not three lines at the call site. A WORLD hit's identity travels on `World` and its `Target`
        /// carries no usable id at all; a PAGE hit is the other way round. Reading the wrong half would not
        /// throw — it would quietly make a token pointing at "" that renders as prose forever.
        ///
        /// THE WORLD MAP IS REFUSED. Its hit exists so Ctrl+K can open the map tab; it has no World and an
        /// empty Target id, so there is nothing for [[kind:id|name]] to point at. The picker also drops it
        /// from the list (QuickOpenPopup.RunSearch), so this is the second of two guards on the same case —
        /// deliberately, because the first is a UI decision and this one is the rule.</summary>
        public static bool TryTokenFor(QuickHit hit, out string kind, out string id, out string name)
        {
            kind = null;
            id = null;
            name = hit != null ? hit.Title : null;
            if (hit == null) return false;

            if (hit.World != null)
            {
                if (string.IsNullOrEmpty(hit.World.Id)) return false;
                kind = NotesLinkOps.KindOf(hit.World.Kind);
                id = hit.World.Id;
                return true;
            }

            if (hit.Target == null || string.IsNullOrEmpty(hit.Target.Id)) return false;
            kind = NotesLinkOps.KindPage;
            id = hit.Target.Id;
            return true;
        }

        /// <summary>The same question asked of a SURFACE rather than a search hit: what would a link to this
        /// tab's target look like, if it can be one at all. The navigator's rows are SurfaceRefs, so the drag
        /// gesture (Р2 Task 11) asks this; the palette's rows are QuickHits, so the button asks the one above.
        ///
        /// TWO FUNCTIONS, ONE RULE, AND THEY CANNOT DISAGREE — but only because of a fact worth writing down
        /// rather than assuming: WorldSurface.PoiEditor is the sole producer of SurfaceKind.PoiEditor and it
        /// takes a bare POI id, the same id a WorldObjectRef carries. The other world surfaces do NOT have
        /// that property — Settlement/BuildingInterior/Dungeon ids are SurfaceIds.Interior composites
        /// (`poiId#roomId`), which are not what [[poi:…]] means — and they are refused here by simply not
        /// being listed, rather than by a second check that would be the redundant half of one guard.
        ///
        /// THE WORLD MAP IS REFUSED for the same reason as above: SurfaceKind.WorldMap carries an empty id by
        /// contract, so there is nothing to point at.</summary>
        public static bool TryTokenForSurface(SurfaceRef target, out string kind, out string id)
        {
            kind = null;
            id = null;
            if (target == null || string.IsNullOrEmpty(target.Id)) return false;

            if (target.Kind == SurfaceKind.Page) kind = NotesLinkOps.KindPage;
            else if (target.Kind == SurfaceKind.PoiEditor) kind = NotesLinkOps.KindOf(WorldRefKind.Poi);
            else return false;

            id = target.Id;
            return true;
        }

        /// <summary>Resolves a link token's target to its CURRENT name, from the two sources Search already
        /// receives: the document for "page", the world list for the three WorldRefKind names. Returns null
        /// for a target that no longer exists, which makes BuildDisplay fall back to the copy stored in the
        /// token — so a page mentioning a deleted object stays findable by the name it was written with,
        /// which is the only name the DM can still remember it by.
        ///
        /// Building and Room resolve to null today because no producer emits them yet (see WorldObjectRef's
        /// own doc); the branch exists so that when one does, search follows without a second edit here.
        ///
        /// PUBLIC SINCE Р2 TASK 7, and shared rather than copied: the page renders the same links Ctrl+K
        /// searches, so a second implementation would be two answers to "what is this called now" — and the
        /// day they disagreed, a link would render one name and be findable under another.</summary>
        public static NotesLinkOps.NameResolver ResolverFor(NotesDocument doc, IReadOnlyList<WorldObjectRef> world)
        {
            return (kind, id) =>
            {
                if (kind == NotesLinkOps.KindPage)
                {
                    if (doc == null) return null;
                    foreach (var g in doc.Groups)
                        foreach (var p in g.Pages)
                            if (p.Id == id) return p.Name;
                    return null;
                }
                if (world == null) return null;
                if (!NotesLinkOps.TryParseWorldKind(kind, out var wk)) return null;
                foreach (var o in world)
                    if (o != null && o.Kind == wk && o.Id == id) return o.Name;
                return null;
            };
        }

        /// <summary>S1 — WHAT IS SEARCHED IS WHAT THE DM SEES, not what is stored.
        ///
        /// A block's Text holds inline links as [[poi:a1|Ржавый Якорь]], where the name is a stale fallback
        /// copy and the object's real name is resolved at render time (NotesLinkOps' class doc). Folding the
        /// RAW field would therefore undo that in the one reader that bypasses resolution: rename «Ржавый
        /// Якорь» to «Тихая Гавань» and the page would render correctly everywhere while Ctrl+K still found
        /// it by the dead name and failed to find it by the live one. It would also print brackets and a raw
        /// id into the palette, since MakeSnippet slices whatever string the match index came from.
        ///
        /// So both fields go through BuildDisplay first, and the snippet is cut from that same string —
        /// which keeps the index alignment TryAddBodyHit's own comment depends on, by construction.</summary>
        static void CollectBodyHits(NotesPage p, string needle, NotesLinkOps.NameResolver resolve, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (p.Blocks == null) return;
            foreach (var b in p.Blocks)
            {
                // Q3 guard — see the class comment above. b.Text and b.Detail are the ONLY fields read here;
                // b.ImageBytes is never touched, regardless of how large it is.
                TryAddBodyHit(p, b.Text, needle, resolve, candidates, ref seq);
                TryAddBodyHit(p, b.Detail, needle, resolve, candidates, ref seq);
            }
        }

        static void TryAddBodyHit(NotesPage p, string field, string needle, NotesLinkOps.NameResolver resolve, List<(int, int, QuickHit)> candidates, ref int seq)
        {
            if (string.IsNullOrEmpty(field)) return;

            // S1 — from here down, `field` is the DISPLAY text. Everything below (the fold, the match index,
            // the snippet) reads this one string, so they cannot come out of alignment with each other.
            field = NotesLinkOps.BuildDisplay(field, resolve).Text;
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
