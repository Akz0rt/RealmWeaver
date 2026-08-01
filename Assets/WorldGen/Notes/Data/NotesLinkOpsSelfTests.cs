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
    }
}
