using System.Collections.Generic;
using UnityEngine;
using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>
    /// Self-tests for Ctrl+K search. Runs two ways: right-click this component in the Editor, or offline via
    /// Tools/notes-harness (see WorkspaceOpsSelfTests for the exact commands), which compiles these very
    /// sources against UnityEngine stubs.
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would break
    /// (Q1..Q5, W1 in the plan) and, per the recurring lesson on this plan, always include the POSITIVE side
    /// of each check: a mutation that collapses the result set toward empty, or that scores two rank tiers
    /// equally, must fail a real assertion here rather than let an "X is absent" check pass vacuously.
    /// </summary>
    public class QuickOpenSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Quick Open")]
        public void SelfTestQuickOpen()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            var anchor = new NotesPage { Name = "Ржавый Якорь" };
            var session = new NotesPage { Name = "Сессия 2" };
            session.Blocks.Add(NotesDocOps.NewBlock(BlockKind.Section, 0, "Секреты"));
            var line = NotesDocOps.NewBlock(BlockKind.Item, 1, "Он бросил якорь у Кургана и ушёл");
            line.Detail = "деталь про якорную цепь";
            session.Blocks.Add(line);
            var picture = NotesDocOps.NewBlock(BlockKind.Image, 1);
            picture.ImageBytes = new byte[4096];
            session.Blocks.Add(picture);
            g.Pages.Add(anchor); g.Pages.Add(session);

            var hits = QuickOpen.Search(doc, null, "якор");

            // Q1 — the page NAME wins over body text.
            if (hits.Count < 2 || hits[0].Title != "Ржавый Якорь")
            {
                string actual = hits.Count > 0 ? hits[0].Title : "<none>";
                Debug.LogError($"FAIL quickopen: first hit «{actual}» of {hits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}, want «Ржавый Якорь» first (Q1)");
                ok = false;
            }

            // Q2 — a text hit explains where it came from.
            var textHit = hits.Find(h => !string.IsNullOrEmpty(h.Snippet));
            if (textHit == null || !textHit.Snippet.Contains("якорь"))
            {
                string actual = textHit == null ? "<no hit carries a Snippet>" : $"«{textHit.Snippet}»";
                Debug.LogError($"FAIL quickopen: body-hit snippet = {actual}, want it to contain «якорь» (Q2)");
                ok = false;
            }
            else if (!textHit.Kind.Contains("Сессия 2"))
            {
                Debug.LogError($"FAIL quickopen: snippet Kind «{textHit.Kind}», want it to contain «Сессия 2» (Q2)");
                ok = false;
            }

            // Q2 again — Detail is searched too.
            var detailHits = QuickOpen.Search(doc, null, "цепь");
            if (detailHits.Count == 0)
            { Debug.LogError("FAIL quickopen: Detail search «цепь» found 0 hits, want >= 1 — Detail must be searchable (Q2)"); ok = false; }

            // Q4 — an empty query is not "everything".
            var emptyHits = QuickOpen.Search(doc, null, "   ");
            if (emptyHits.Count != 0)
            { Debug.LogError($"FAIL quickopen: whitespace query returned {emptyHits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} hit(s), want 0 (Q4)"); ok = false; }

            // Q4's folding rule checked from its own angle: a broken/removed ToLowerInvariant call would
            // still pass the whitespace check above (that one never touches case), so it needs a dedicated
            // positive check — an upper-case query must still find the lower-case text.
            var upperHits = QuickOpen.Search(doc, null, "ЯКОРЬ");
            if (upperHits.Count == 0)
            { Debug.LogError("FAIL quickopen: upper-case query «ЯКОРЬ» found 0 hits, want >= 1 — folding must be case-insensitive (Q4)"); ok = false; }

            // Q1 again — a PREFIX match on the page name outranks a MID-WORD one. «Ржавый Якорь» matches
            // «якор» mid-word (checked above); this new page matches it as a prefix and must come first.
            var prefixPage = new NotesPage { Name = "Якорная стоянка" };
            g.Pages.Add(prefixPage);
            var prefixHits = QuickOpen.Search(doc, null, "якор");
            if (prefixHits.Count == 0 || prefixHits[0].Title != "Якорная стоянка")
            {
                string actual = prefixHits.Count > 0 ? prefixHits[0].Title : "<none>";
                Debug.LogError($"FAIL quickopen: first hit «{actual}», want «Якорная стоянка» ahead of the mid-word «Ржавый Якорь» (Q1 prefix)");
                ok = false;
            }

            // W1 — the world map is a candidate too, ranked like a page name against
            // WorkspaceOps.DefaultWorldMapTitle ("Карта мира"). The assertion needs a page that reaches the
            // SAME rank on the SAME needle, or it is vacuous — «Картотека» prefix-matches «карт» exactly
            // like «карта мира» does (both NamePrefix, idx 0), so a mutant that ranks the world hit wrong,
            // orders CollectWorldMapHit after the name loop, or drops its Kind tag would make «Картотека»
            // win this position check instead of tying and losing to it.
            var catalog = new NotesPage { Name = "Картотека" };
            g.Pages.Add(catalog);
            var worldHits = QuickOpen.Search(doc, null, "карт");
            if (worldHits.Count < 1 || worldHits[0].Target == null || worldHits[0].Target.Kind != SurfaceKind.WorldMap)
            {
                string actual = worldHits.Count > 0 ? $"«{worldHits[0].Title}» ({worldHits[0].Target?.Kind})" : "<none>";
                Debug.LogError($"FAIL quickopen: first hit for «карт» = {actual}, want the world map, ordered ahead of the equal-rank «Картотека» (W1)");
                ok = false;
            }
            else
            {
                if (worldHits[0].Target.Id != "")
                { Debug.LogError($"FAIL quickopen: world-map hit Id «{worldHits[0].Target.Id}», want «» — must stay byte-identical to WorkspaceOps.NewDefault's seed tab (W1)"); ok = false; }
                if (worldHits[0].Title != WorkspaceOps.DefaultWorldMapTitle)
                { Debug.LogError($"FAIL quickopen: world-map hit title «{worldHits[0].Title}», want «{WorkspaceOps.DefaultWorldMapTitle}» (W1)"); ok = false; }
                if (worldHits[0].Kind != "карта")
                { Debug.LogError($"FAIL quickopen: world-map hit Kind «{worldHits[0].Kind}», want «карта» so the palette can tell it apart from a page/body hit (W1)"); ok = false; }
            }
            // The positive side of the SAME check: «Картотека» must still BE found, just not first — a
            // mutant that made the world hit crowd out real results (rather than merely outrank them) would
            // pass the checks above (which only look at index 0) while silently losing a page hit.
            if (!worldHits.Exists(h => h.Title == "Картотека"))
            { Debug.LogError("FAIL quickopen: «карт» found 0 hits for «Картотека», want >= 1 — the world map must not crowd out real page results (W1)"); ok = false; }

            // W1, the MIRROR direction (checkpoint3-review.md Important 2): the check above only ever
            // matches the world map at idx 0 (NamePrefix), so a mutant that forces
            // `rank = idx == 0 ? NamePrefix : NameContains` down to a bare `NamePrefix` — always, regardless
            // of idx — survives every assertion above undetected. Needle «мира» matches «карта мира» at
            // index 6 (NameContains, not a prefix), while a page named «Мираж» matches «мира» at index 0
            // (NamePrefix) — «Мираж» must therefore rank AHEAD of the world map here, the exact mirror of
            // the «Картотека» check: there, the map ties a NamePrefix page and wins the tie; here, the map
            // is genuinely worse-ranked than a real NamePrefix page and must lose outright.
            var mirage = new NotesPage { Name = "Мираж" };
            g.Pages.Add(mirage);
            var midRankHits = QuickOpen.Search(doc, null, "мира");
            if (midRankHits.Count == 0 || midRankHits[0].Title != "Мираж")
            {
                string actual = midRankHits.Count > 0 ? $"«{midRankHits[0].Title}»" : "<none>";
                Debug.LogError($"FAIL quickopen: first hit for «мира» = {actual}, want «Мираж» (NamePrefix) ahead of the world map (NameContains at idx 6) (W1)");
                ok = false;
            }
            // Positive side: the world map must still BE found here, just ranked below «Мираж» — a mutant
            // that instead excluded the map from mid-string matches entirely would pass the check above
            // (which only looks at index 0) while silently breaking "Search always considers the world map".
            if (!midRankHits.Exists(h => h.Target != null && h.Target.Kind == SurfaceKind.WorldMap))
            { Debug.LogError("FAIL quickopen: «мира» found 0 world-map hits, want >= 1 — the world map must still match mid-string, just rank below a real NamePrefix hit (W1)"); ok = false; }

            // Q3 — ImageBytes is never scanned, even when it happens to hold the query's own bytes: only
            // Text and Detail are text-bearing fields on a block.
            var stealth = NotesDocOps.NewBlock(BlockKind.Image, 1);
            stealth.ImageBytes = System.Text.Encoding.UTF8.GetBytes("тайнопись");
            session.Blocks.Add(stealth);
            var imageOnlyHits = QuickOpen.Search(doc, null, "тайнопись");
            if (imageOnlyHits.Count != 0)
            { Debug.LogError($"FAIL quickopen: a query matching only ImageBytes returned {imageOnlyHits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} hit(s), want 0 (Q3)"); ok = false; }

            // Q5 — the cap holds, AND holds for the right reason: 42 matches truly exist below (1 name +
            // 1 body + 40 fresh pages), so a cap implementation that quietly collapses the result set toward
            // zero must fail here just as loudly as one that ignores the limit and returns everything.
            for (int i = 0; i < 40; i++) g.Pages.Add(new NotesPage { Name = $"Якорь {i}" });
            var capped = QuickOpen.Search(doc, null, "якорь", 20);
            if (capped.Count > 20)
            { Debug.LogError($"FAIL quickopen: {capped.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} results, want <= 20 (Q5)"); ok = false; }
            if (capped.Count != 20)
            { Debug.LogError($"FAIL quickopen: {capped.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} results, want exactly 20 of the 42 available matches (Q5)"); ok = false; }

            Debug.Log(ok ? "Self-Test Quick Open: PASS" : "Self-Test Quick Open: FAIL");
        }

        /// <summary>Fix round 1 (checkpoint review) — the world-MAP hit must survive a null `doc`, unlike
        /// every other candidate: opening the map needs no document (it targets the surface directly, not a
        /// page — QuickHit's own doc), so a scene where no NotesRootBuilder was ever resolved must still be
        /// able to offer «Карта мира» from Ctrl+K. This is the SAME shape an earlier review in this arc
        /// flagged Important against NavigatorTree.Build's world-map row (then a group of its own, called
        /// Pinned; folded into «Мир» in Task 10e), fixed there by moving it above that method's own doc==null
        /// guard — QuickOpen.Search does the identical move for the world map (see its class doc's W1
        /// paragraph). A world-OBJECT hit (W2) now gets the same treatment for the same reason (Task 10e's
        /// review round removed the gate along with W4); SelfTestQuickOpenWorldObjects owns that half, and
        /// this test stays the world map's, which is why it passes `world: null` throughout — the two halves
        /// fail separately rather than one masking the other.</summary>
        [ContextMenu("Self-Test: Quick Open World Map Survives Null Doc")]
        public void SelfTestQuickOpenWorldMapSurvivesNullDoc()
        {
            bool ok = true;

            var hits = QuickOpen.Search(null, null, "карта");
            if (hits.Count != 1 || hits[0].Target == null || hits[0].Target.Kind != SurfaceKind.WorldMap)
            {
                string actual = hits.Count > 0 ? $"«{hits[0].Title}» ({hits[0].Target?.Kind})" : "<none>";
                Debug.LogError($"FAIL quickopen-nulldoc: Search(null, null, «карта») = {actual}, want exactly the world-map hit — it must not be gated behind doc==null");
                ok = false;
            }
            else
            {
                if (hits[0].Target.Id != "")
                { Debug.LogError($"FAIL quickopen-nulldoc: world-map hit Id «{hits[0].Target.Id}», want «» even with a null doc"); ok = false; }
                if (hits[0].Title != WorkspaceOps.DefaultWorldMapTitle)
                { Debug.LogError($"FAIL quickopen-nulldoc: world-map hit title «{hits[0].Title}», want «{WorkspaceOps.DefaultWorldMapTitle}»"); ok = false; }
            }

            // A null doc must not throw, and a query the world map does NOT match must return an EMPTY list —
            // this call passes `world: null` too, so the only candidates a document could have contributed
            // are page hits, and zero confirms a null doc really does suppress those and nothing else.
            var noMatch = QuickOpen.Search(null, null, "нет такого");
            if (noMatch.Count != 0)
            { Debug.LogError($"FAIL quickopen-nulldoc: Search(null, null, «нет такого») returned {noMatch.Count}, want 0"); ok = false; }

            Debug.Log(ok ? "Self-Test Quick Open World Map Survives Null Doc: PASS" : "Self-Test Quick Open World Map Survives Null Doc: FAIL");
        }

        /// <summary>W1-W5 — the world (today: POI) side of Ctrl+K, task-10b-brief.md. Its own fixture rather
        /// than sharing SelfTestQuickOpen's (which grows to 40+ pages by its own final assertion): W4 needs a
        /// page whose Bound names a SPECIFIC POI id, which is easiest to reason about starting clean.</summary>
        [ContextMenu("Self-Test: Quick Open World Objects")]
        public void SelfTestQuickOpenWorldObjects()
        {
            bool ok = true;
            var doc = new NotesDocument();
            var g = new PageGroup { Title = "Сессии" };
            doc.Groups.Add(g);

            // W1, mirror direction of SelfTestQuickOpen's own Мираж check: there, the world MAP loses to a
            // real NamePrefix page. Here a world OBJECT must lose the same way — «Ратуша» prefix-matches
            // «рат» (NamePrefix); the POI «Пиратская застава» only matches mid-name (NameContains), so a
            // mutant that ranked every world-object hit as NamePrefix regardless of idx would make the POI
            // win this position check instead of losing it.
            var townHall = new NotesPage { Name = "Ратуша" };
            g.Pages.Add(townHall);
            var unboundPoi = new WorldObjectRef { Kind = WorldRefKind.Poi, Id = "poi-1", Name = "Пиратская застава", KindLabel = "город" };

            var hits = QuickOpen.Search(doc, new List<WorldObjectRef> { unboundPoi }, "рат");
            if (hits.Count == 0 || hits[0].Title != "Ратуша")
            {
                string actual = hits.Count > 0 ? hits[0].Title : "<none>";
                Debug.LogError($"FAIL quickopen-world: first hit «{actual}», want «Ратуша» (NamePrefix) ahead of the mid-name POI «Пиратская застава» (NameContains) (W1)");
                ok = false;
            }
            // Positive side: the POI must still BE found, just ranked below — a mutant that excluded world
            // objects from mid-string matches entirely would pass the check above (index 0 only) while
            // silently breaking "a world object is searched like a page name" (W1).
            var poiHit = hits.Find(h => h.World != null);
            if (poiHit == null || poiHit.Title != "Пиратская застава")
            { Debug.LogError("FAIL quickopen-world: «рат» found 0 world-object hits, want >= 1 — a POI must still match mid-name, just rank below a NamePrefix page (W1)"); ok = false; }

            if (poiHit != null)
            {
                // W2 — the hit carries the world identity, and Target names no page (none exists yet).
                if (poiHit.World == null || poiHit.World.Kind != WorldRefKind.Poi || poiHit.World.Id != "poi-1")
                { Debug.LogError("FAIL quickopen-world: POI hit's World must carry Kind=Poi, Id=«poi-1» (W2)"); ok = false; }
                if (poiHit.Target == null || poiHit.Target.Kind != SurfaceKind.Page || poiHit.Target.Id != "")
                { Debug.LogError($"FAIL quickopen-world: POI hit Target = {poiHit.Target?.Kind}/«{poiHit.Target?.Id}», want Page/«» — no page exists yet (W2)"); ok = false; }

                // W3 — Kind is the object's KindLabel, NOT its Name (Title already equals Name, so a mutant
                // that swapped Kind for Name would pass every check above while breaking the palette's
                // right-hand column). Snippet stays null: QuickOpenPopup.BuildRow renders `Snippet ?? Kind`,
                // so a stray Snippet would silently hide the label this assertion is pinning.
                if (poiHit.Kind != "город")
                { Debug.LogError($"FAIL quickopen-world: POI hit Kind «{poiHit.Kind}», want «город» (KindLabel) (W3)"); ok = false; }
                if (poiHit.Snippet != null)
                { Debug.LogError("FAIL quickopen-world: a world-object hit must carry no Snippet, or its Kind label would never render (W3)"); ok = false; }
            }

            // W4 IS GONE (Task 10e review round 1) — a POI that already has a bound page produces BOTH rows:
            // the note AND the place. Two rows are correct here, not duplication, because they open two
            // different things (the page hit a note, the world hit the POI's editor) — and suppressing the
            // place made false the very promise the Task 10e brief accepts the «Мир» drowning risk on,
            // «Ctrl+K still reaches everything», for exactly the POIs the DM worked on hardest.
            //
            // Asserted POSITIVELY on BOTH rows, by identity, not as a count of 2: a count alone would pass on
            // two page hits, or two world hits, or one of each for the WRONG objects. The page row is
            // recognised by World == null AND its own page id; the place row by World naming poi-2.
            var boundPage = new NotesPage { Name = "Тихий Брод", Bound = new WorldRef { Kind = WorldRefKind.Poi, Id = "poi-2" } };
            g.Pages.Add(boundPage);
            var boundPoi = new WorldObjectRef { Kind = WorldRefKind.Poi, Id = "poi-2", Name = "Тихий Брод", KindLabel = "город" };
            var boundHits = QuickOpen.Search(doc, new List<WorldObjectRef> { boundPoi }, "брод");
            if (!boundHits.Exists(h => h.World != null && h.World.Kind == WorldRefKind.Poi && h.World.Id == "poi-2"))
            { Debug.LogError($"FAIL quickopen-world: a POI with a bound page produced no PLACE row ({boundHits.Count} hit(s) total) — having a note must not hide the place itself"); ok = false; }
            if (!boundHits.Exists(h => h.World == null && h.Target != null && h.Target.Id == boundPage.Id))
            { Debug.LogError($"FAIL quickopen-world: the bound page's own row is missing ({boundHits.Count} hit(s) total) — removing W4 must add the place's row, never replace the note's"); ok = false; }

            // The `doc == null` gate went with W4, and by construction: CollectWorldHits no longer TAKES a
            // document, so it cannot be gated on one. Pinned behaviourally here rather than trusted to the
            // signature — a future edit that re-added the parameter and the guard would fail this line.
            var noDocWorldHits = QuickOpen.Search(null, new List<WorldObjectRef> { boundPoi }, "брод");
            if (!noDocWorldHits.Exists(h => h.World != null && h.World.Id == "poi-2"))
            { Debug.LogError($"FAIL quickopen-world: Search(null, world, «брод») returned {noDocWorldHits.Count} hit(s) with no place among them — a world object needs no document to be opened, so it must not be gated on one"); ok = false; }

            // W5 — `world == null` is the pre-generation state (no POIs placed yet, or PoiManager not found),
            // not an error: page hits still come back, and Search must not throw. Wrapped in try/catch, a
            // deliberate departure from this file's usual bare-if idiom, so a THROWING mutant fails a named
            // W5 assertion instead of surfacing as a bare stack trace the harness attributes to no rule.
            try
            {
                var noWorldHits = QuickOpen.Search(doc, null, "рат");
                if (noWorldHits.Count == 0 || noWorldHits[0].Title != "Ратуша")
                { Debug.LogError("FAIL quickopen-world: Search(doc, null, ...) must still return page hits, not silently drop them (W5)"); ok = false; }
            }
            catch (System.Exception ex)
            { Debug.LogError($"FAIL quickopen-world: Search(doc, null, ...) threw {ex.GetType().Name} — a null `world` is the pre-generation state, not an error (W5)"); ok = false; }

            Debug.Log(ok ? "Self-Test Quick Open World Objects: PASS" : "Self-Test Quick Open World Objects: FAIL");
        }
    }
}
