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

            var hits = QuickOpen.Search(doc, "якор");

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
            var detailHits = QuickOpen.Search(doc, "цепь");
            if (detailHits.Count == 0)
            { Debug.LogError("FAIL quickopen: Detail search «цепь» found 0 hits, want >= 1 — Detail must be searchable (Q2)"); ok = false; }

            // Q4 — an empty query is not "everything".
            var emptyHits = QuickOpen.Search(doc, "   ");
            if (emptyHits.Count != 0)
            { Debug.LogError($"FAIL quickopen: whitespace query returned {emptyHits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} hit(s), want 0 (Q4)"); ok = false; }

            // Q4's folding rule checked from its own angle: a broken/removed ToLowerInvariant call would
            // still pass the whitespace check above (that one never touches case), so it needs a dedicated
            // positive check — an upper-case query must still find the lower-case text.
            var upperHits = QuickOpen.Search(doc, "ЯКОРЬ");
            if (upperHits.Count == 0)
            { Debug.LogError("FAIL quickopen: upper-case query «ЯКОРЬ» found 0 hits, want >= 1 — folding must be case-insensitive (Q4)"); ok = false; }

            // Q1 again — a PREFIX match on the page name outranks a MID-WORD one. «Ржавый Якорь» matches
            // «якор» mid-word (checked above); this new page matches it as a prefix and must come first.
            var prefixPage = new NotesPage { Name = "Якорная стоянка" };
            g.Pages.Add(prefixPage);
            var prefixHits = QuickOpen.Search(doc, "якор");
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
            var worldHits = QuickOpen.Search(doc, "карт");
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

            // Q3 — ImageBytes is never scanned, even when it happens to hold the query's own bytes: only
            // Text and Detail are text-bearing fields on a block.
            var stealth = NotesDocOps.NewBlock(BlockKind.Image, 1);
            stealth.ImageBytes = System.Text.Encoding.UTF8.GetBytes("тайнопись");
            session.Blocks.Add(stealth);
            var imageOnlyHits = QuickOpen.Search(doc, "тайнопись");
            if (imageOnlyHits.Count != 0)
            { Debug.LogError($"FAIL quickopen: a query matching only ImageBytes returned {imageOnlyHits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} hit(s), want 0 (Q3)"); ok = false; }

            // Q5 — the cap holds, AND holds for the right reason: 42 matches truly exist below (1 name +
            // 1 body + 40 fresh pages), so a cap implementation that quietly collapses the result set toward
            // zero must fail here just as loudly as one that ignores the limit and returns everything.
            for (int i = 0; i < 40; i++) g.Pages.Add(new NotesPage { Name = $"Якорь {i}" });
            var capped = QuickOpen.Search(doc, "якорь", 20);
            if (capped.Count > 20)
            { Debug.LogError($"FAIL quickopen: {capped.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} results, want <= 20 (Q5)"); ok = false; }
            if (capped.Count != 20)
            { Debug.LogError($"FAIL quickopen: {capped.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} results, want exactly 20 of the 42 available matches (Q5)"); ok = false; }

            Debug.Log(ok ? "Self-Test Quick Open: PASS" : "Self-Test Quick Open: FAIL");
        }
    }
}
