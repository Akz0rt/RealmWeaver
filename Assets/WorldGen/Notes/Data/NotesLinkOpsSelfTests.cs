using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Self-tests for the inline-link grammar. Runs two ways, exactly as NotesDocOpsSelfTests does:
    /// right-click this component in the Editor, or offline via Tools/notes-harness
    /// (`powershell -File sync.ps1` then `dotnet run -c Release -- selftests` from bash).
    ///
    /// Every failure prints the ACTUAL and the WANTED value. Assertions target the rule a change would
    /// break, not a derived summary number.
    /// </summary>
    public class NotesLinkOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Link Grammar Parse")]
        public void SelfTestLinkParse()
        {
            bool ok = true;

            // L2 — a token yields one span whose SourceStart/SourceLength cut EXACTLY the token out.
            const string one = "Мы вошли в [[poi:a1b2c3|Ржавый Якорь]] под дождём";
            var spans = NotesLinkOps.ParseSpans(one);
            if (spans.Count != 1)
            { Debug.LogError($"FAIL L2: {spans.Count} spans, want 1"); ok = false; }
            else
            {
                var s = spans[0];
                if (s.Kind != "poi" || s.Id != "a1b2c3" || s.Display != "Ржавый Якорь")
                { Debug.LogError($"FAIL L2: parsed ({s.Kind}/{s.Id}/{s.Display}), want (poi/a1b2c3/Ржавый Якорь)"); ok = false; }
                var cut = one.Substring(s.SourceStart, s.SourceLength);
                if (cut != "[[poi:a1b2c3|Ржавый Якорь]]")
                { Debug.LogError($"FAIL L2: span cuts \"{cut}\", want the whole token"); ok = false; }
            }

            // L2 — two tokens keep source order.
            var two = NotesLinkOps.ParseSpans("[[page:p1|А]] и [[poi:x|Б]]");
            if (two.Count != 2 || two[0].Id != "p1" || two[1].Id != "x")
            {
                var got = new List<string>();
                foreach (var t in two) got.Add(t.Id);
                Debug.LogError($"FAIL L2: two tokens parsed as [{string.Join(",", got)}], want [p1,x]"); ok = false;
            }

            // L1 — every malformed form is TEXT, not an error and not a span.
            string[] malformed =
            {
                "[[poi:a1b2c3 Ржавый Якорь]]",   // no pipe
                "[[npc:a1|Ольга]]",              // unknown kind
                "[[poi:|Безымянный]]",           // empty id
                "[[poi:a1|]]",                   // empty display
                "[[poi:a1|Якорь",                // unterminated
                "просто [[ скобки ]]",           // no kind:id at all
            };
            foreach (var m in malformed)
            {
                var got = NotesLinkOps.ParseSpans(m);
                if (got.Count != 0)
                { Debug.LogError($"FAIL L1: \"{m}\" produced {got.Count} span(s), want 0 — malformed is prose"); ok = false; }
            }

            // L1 — a rejected candidate must not swallow the real token that starts inside it. This pins the
            // resume rule: the scan continues just past the opening bracket, not past the whole failure.
            var afterBad = NotesLinkOps.ParseSpans("[[bad [[poi:a1|Якорь]]");
            if (afterBad.Count != 1 || afterBad[0].Id != "a1")
            { Debug.LogError($"FAIL L1: a broken \"[[\" swallowed the token that followed it ({afterBad.Count} span(s))"); ok = false; }

            // L4 — MakeToken is the only writer, and ParseSpans reads back what it wrote.
            var made = NotesLinkOps.MakeToken("room", "r7", "Зал Троих");
            var back = NotesLinkOps.ParseSpans(made);
            if (back.Count != 1 || back[0].Kind != "room" || back[0].Id != "r7" || back[0].Display != "Зал Троих")
            { Debug.LogError($"FAIL L4: MakeToken produced \"{made}\", which did not read back"); ok = false; }

            // The three world kinds map onto WorldRefKind; "page" does not.
            if (!NotesLinkOps.TryParseWorldKind("building", out var wk) || wk != WorldRefKind.Building)
            { Debug.LogError("FAIL: \"building\" did not map to WorldRefKind.Building"); ok = false; }
            if (NotesLinkOps.TryParseWorldKind(NotesLinkOps.KindPage, out _))
            { Debug.LogError("FAIL: \"page\" must NOT map to a WorldRefKind — a page is not a world object"); ok = false; }

            // KindOf is TryParseWorldKind's inverse, so a token made from a WorldRef reads back as one.
            if (NotesLinkOps.KindOf(WorldRefKind.Room) != "room" || NotesLinkOps.KindOf(WorldRefKind.Poi) != "poi")
            { Debug.LogError($"FAIL: KindOf gave ({NotesLinkOps.KindOf(WorldRefKind.Room)}/{NotesLinkOps.KindOf(WorldRefKind.Poi)}), want (room/poi)"); ok = false; }

            // Null and empty are ordinary inputs, not special cases.
            if (NotesLinkOps.ParseSpans(null).Count != 0 || NotesLinkOps.ParseSpans("").Count != 0)
            { Debug.LogError("FAIL: null/empty source must give an empty list, never throw"); ok = false; }

            Debug.Log(ok ? "Self-Test Link Grammar Parse: PASS" : "Self-Test Link Grammar Parse: FAIL");
        }

        [ContextMenu("Self-Test: Link Display And Index Map")]
        public void SelfTestLinkDisplay()
        {
            bool ok = true;

            // The resolver knows the POI by its CURRENT name — which is NOT the name stored in the token.
            NotesLinkOps.NameResolver resolve = (kind, id) =>
                kind == "poi" && id == "a1" ? "Тихая Гавань" : null;

            const string src = "Мы вошли в [[poi:a1|Ржавый Якорь]] под дождём";
            var d = NotesLinkOps.BuildDisplay(src, resolve);

            // D1 — the CURRENT name wins over the stored copy. This is the whole rename mechanism.
            if (d.Text != "Мы вошли в Тихая Гавань под дождём")
            { Debug.LogError($"FAIL D1: display = \"{d.Text}\", want the CURRENT name substituted"); ok = false; }
            if (d.Spans.Count != 1 || !d.Spans[0].Resolved)
            { Debug.LogError("FAIL D1: the span must be marked Resolved"); ok = false; }

            // The stored copy is the fallback, and ONLY when the target is gone.
            var gone = NotesLinkOps.BuildDisplay(src, (k, i) => null);
            if (gone.Text != "Мы вошли в Ржавый Якорь под дождём")
            { Debug.LogError($"FAIL D1: unresolved display = \"{gone.Text}\", want the stored copy"); ok = false; }
            if (gone.Spans.Count != 1 || gone.Spans[0].Resolved)
            { Debug.LogError("FAIL D1: an unresolved span must NOT be marked Resolved — the renderer mutes it"); ok = false; }

            if (d.Spans.Count != 1)
            { Debug.Log("Self-Test Link Display And Index Map: FAIL"); return; }
            var sp = d.Spans[0];

            // L3a — the span's display range cuts exactly the substituted name.
            if (d.Text.Substring(sp.DisplayStart, sp.DisplayLength) != "Тихая Гавань")
            { Debug.LogError($"FAIL L3: display range cuts \"{d.Text.Substring(sp.DisplayStart, sp.DisplayLength)}\""); ok = false; }

            // L3b — OUTSIDE a token the map is exact in both directions, including both boundaries and the
            // very end of the string.
            for (int s = 0; s <= src.Length; s++)
            {
                bool insideToken = s > sp.SourceStart && s < sp.SourceStart + sp.SourceLength;
                if (insideToken) continue;
                int roundTrip = d.ToSource(d.ToDisplay(s));
                if (roundTrip != s)
                { Debug.LogError($"FAIL L3: source {s} round-tripped to {roundTrip}"); ok = false; break; }
            }

            // L3c — an index INSIDE the token's machinery collapses to the token's start, in both directions.
            int mid = sp.SourceStart + 5;   // somewhere in "[[poi:"
            if (d.ToDisplay(mid) != sp.DisplayStart)
            { Debug.LogError($"FAIL L3: source inside a token mapped to {d.ToDisplay(mid)}, want {sp.DisplayStart}"); ok = false; }
            if (d.ToSource(sp.DisplayStart + 3) != sp.SourceStart)
            { Debug.LogError($"FAIL L3: display inside a name mapped to {d.ToSource(sp.DisplayStart + 3)}, want {sp.SourceStart}"); ok = false; }

            // L3d — THE BOUNDARY THAT MATTERS MOST: a caret immediately AFTER a link must land past "]]",
            // not inside the token. This is the offset a click just to the right of a link produces, which
            // is the commonest caret position anywhere near one.
            int afterLink = d.ToSource(sp.DisplayStart + sp.DisplayLength);
            if (afterLink != sp.SourceStart + sp.SourceLength)
            { Debug.LogError($"FAIL L3: the caret after a link mapped to source {afterLink}, want {sp.SourceStart + sp.SourceLength} — just past the closing brackets"); ok = false; }

            // Two tokens: the second one's offsets must account for the FIRST one's length change.
            var t = NotesLinkOps.BuildDisplay("[[poi:a1|Старое]] и [[poi:a1|Тоже]]", resolve);
            if (t.Text != "Тихая Гавань и Тихая Гавань")
            { Debug.LogError($"FAIL L3: two tokens gave \"{t.Text}\""); ok = false; }
            if (t.Spans.Count == 2 && t.Text.Substring(t.Spans[1].DisplayStart, t.Spans[1].DisplayLength) != "Тихая Гавань")
            { Debug.LogError("FAIL L3: the SECOND span's display range is wrong — the running offset is not accumulating"); ok = false; }

            // A source with no tokens is its own display, and the map is the identity.
            var plain = NotesLinkOps.BuildDisplay("никаких ссылок", resolve);
            if (plain.Text != "никаких ссылок" || plain.ToSource(4) != 4 || plain.ToDisplay(4) != 4)
            { Debug.LogError("FAIL L3: a token-free string must map as the identity"); ok = false; }

            // Null and empty stay ordinary inputs here too.
            if (NotesLinkOps.BuildDisplay(null, resolve).Text != "" || NotesLinkOps.BuildDisplay("", resolve).Spans.Count != 0)
            { Debug.LogError("FAIL: null/empty source must give an empty DisplayText, never throw"); ok = false; }

            // A null resolver is the "nothing resolves" case, not a crash — it is what a view uses before
            // the document is wired.
            if (NotesLinkOps.BuildDisplay(src, null).Text != "Мы вошли в Ржавый Якорь под дождём")
            { Debug.LogError("FAIL: a null resolver must fall back to stored copies, not throw"); ok = false; }

            Debug.Log(ok ? "Self-Test Link Display And Index Map: PASS" : "Self-Test Link Display And Index Map: FAIL");
        }

        [ContextMenu("Self-Test: Stored Names Self-Heal")]
        public void SelfTestRefreshStoredNames()
        {
            bool ok = true;

            NotesLinkOps.NameResolver resolve = (kind, id) =>
            {
                if (kind == "poi" && id == "a1") return "Тихая Гавань";
                if (kind == NotesLinkOps.KindPage && id == "p1") return "Первая сессия";
                return null;   // anything else has been deleted
            };

            // The stored copy is brought up to date, and only the copy — the id and the prose around it are
            // untouched, which is what makes this safe to run after every edit.
            var healed = NotesLinkOps.RefreshStoredNames("Мы вошли в [[poi:a1|Ржавый Якорь]] под дождём", resolve, out bool changed);
            if (healed != "Мы вошли в [[poi:a1|Тихая Гавань]] под дождём")
            { Debug.LogError($"FAIL: healed to \"{healed}\""); ok = false; }
            if (!changed)
            { Debug.LogError("FAIL: a rewritten name must report changed=true — the caller marks the project dirty from it"); ok = false; }

            // NOTHING CHANGED MEANS NOTHING CHANGED. An unconditional rewrite would dirty the project on
            // every focus change, which is the defect this flag exists to prevent.
            var already = "[[poi:a1|Тихая Гавань]] и [[page:p1|Первая сессия]]";
            var same = NotesLinkOps.RefreshStoredNames(already, resolve, out bool changed2);
            if (same != already || changed2)
            { Debug.LogError($"FAIL: up-to-date names reported changed={changed2}, text \"{same}\""); ok = false; }

            // A DELETED target keeps its stored name — it is the only trace left of what the sentence meant.
            const string dead = "у [[poi:gone|Старой Мельницы]] нет хозяина";
            var kept = NotesLinkOps.RefreshStoredNames(dead, resolve, out bool changed3);
            if (kept != dead || changed3)
            { Debug.LogError($"FAIL: an unresolved token was rewritten to \"{kept}\""); ok = false; }

            // A mixed row heals only the half that resolves, and the offsets of the second token still land
            // right after the first one changed length.
            var mixed = NotesLinkOps.RefreshStoredNames(
                "[[poi:gone|Мельница]], [[poi:a1|Якорь]], [[page:p1|Сессия]]", resolve, out bool changed4);
            if (mixed != "[[poi:gone|Мельница]], [[poi:a1|Тихая Гавань]], [[page:p1|Первая сессия]]")
            { Debug.LogError($"FAIL: mixed row healed to \"{mixed}\""); ok = false; }
            if (!changed4)
            { Debug.LogError("FAIL: mixed row must report changed=true"); ok = false; }

            // Prose is prose: no tokens, no rewrite, and a malformed candidate is not a token (L1).
            var prose = NotesLinkOps.RefreshStoredNames("просто [[ текст", resolve, out bool changed5);
            if (prose != "просто [[ текст" || changed5)
            { Debug.LogError($"FAIL: prose was rewritten to \"{prose}\""); ok = false; }

            // Null source and null resolver are ordinary inputs — a row refreshes before the document is wired.
            if (NotesLinkOps.RefreshStoredNames(null, resolve, out _) != null)
            { Debug.LogError("FAIL: a null source must come back null, never throw"); ok = false; }
            var unwired = NotesLinkOps.RefreshStoredNames(dead, null, out bool changed6);
            if (unwired != dead || changed6)
            { Debug.LogError("FAIL: a null resolver must leave the text alone"); ok = false; }

            Debug.Log(ok ? "Self-Test Stored Names Self-Heal: PASS" : "Self-Test Stored Names Self-Heal: FAIL");
        }
    }
}
