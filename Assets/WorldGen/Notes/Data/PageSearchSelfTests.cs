using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class PageSearchSelfTests : MonoBehaviour
    {
        static NotesLinkOps.NameResolver Resolver()
            => (kind, id) => id == "poi-1" ? "Тихий Брод" : id == "p1" ? "Сессия 1" : null;

        static List<DocBlock> Page(params string[] texts)
        {
            var blocks = new List<DocBlock>();
            foreach (var t in texts) blocks.Add(new DocBlock { Kind = BlockKind.Prose, Depth = 1, Text = t });
            return blocks;
        }

        /// <summary>Находка ДМ с проверки 8 августа: «не находился текст из граф персонажа». Карточка —
        /// ГРАНЬ страницы (`Page.Character`), а не блок, поэтому обход `blocks` в неё не заглядывал вовсе.
        /// Фикстура построена так, чтобы верное и неверное правило РАЗОШЛИСЬ: слово «сумрак» лежит и в
        /// карточке, и в прозе, поэтому «карточку не ищут» даёт 1 попадание вместо 5, а «ищут, но одно
        /// поле» — 2 вместо 5.</summary>
        [ContextMenu("Self-Test: Page Search Character Card")]
        public void SelfTestPageSearchCharacterCard()
        {
            bool ok = true;
            var resolve = Resolver();

            var blocks = Page("В прозе тоже сумрак");
            var card = new CharacterCard
            {
                Who = "сумрак в глазах",
                Where = "живёт в сумрак-переулке",
                Wants = "сумрак и тишины",
                HowToPlay = "говорит про сумрак",
            };

            // МУТАНТ: карточку не ищут вовсе — ровно нынешнее поведение, ради отмены которого правка.
            // МУТАНТ: ищут только одно поле (легко написать `Who` и забыть остальные три).
            var hits = PageSearch.Find(blocks, card, "сумрак", resolve);
            if (hits.Count != 5)
            { Debug.LogError($"FAIL: карточка+проза дали {hits.Count} попаданий, надо 5"); ok = false; }

            // МУТАНТ: попадания карточки дописаны ПОСЛЕ блоков. Шапка персонажа рисуется НАД строками
            // страницы, значит в порядке чтения она первая — иначе Enter повёл бы ДМ снизу вверх.
            if (hits.Count == 5)
            {
                if (hits[0].Field != PageSearch.CardField.Who
                    || hits[1].Field != PageSearch.CardField.Where
                    || hits[2].Field != PageSearch.CardField.Wants
                    || hits[3].Field != PageSearch.CardField.HowToPlay
                    || hits[4].Field != PageSearch.CardField.None)
                {
                    Debug.LogError("FAIL: порядок полей карточки не «Кто→Где→Чего хочет→Как играть, потом проза»");
                    ok = false;
                }

                // МУТАНТ: попаданию карточки оставили BlockId (например, первого блока). Подсветка ищет
                // строку по BlockId — и зажгла бы ЧУЖУЮ строку прозы там, где совпадения нет.
                for (int i = 0; i < 4; i++)
                    if (!string.IsNullOrEmpty(hits[i].BlockId) || hits[i].BlockIndex >= 0)
                    { Debug.LogError($"FAIL: попадание в карточке несёт блок ({hits[i].BlockId}/{hits[i].BlockIndex})"); ok = false; break; }

                // МУТАНТ: смещение считается от начала карточки, а не от начала ПОЛЯ. Подсветка в поле
                // выражается смещением внутри этого поля, иначе она уедет.
                if (hits[1].DisplayStart != "живёт в ".Length)
                { Debug.LogError($"FAIL: смещение во втором поле {hits[1].DisplayStart}, надо {"живёт в ".Length}"); ok = false; }
            }

            // МУТАНТ: нет проверки на пустую карточку. Страница без персонажа — это ОБЫЧНАЯ страница,
            // то есть почти все страницы документа; `Character` там null.
            if (PageSearch.Find(blocks, null, "сумрак", resolve).Count != 1)
            { Debug.LogError("FAIL: страница без карточки ищется не как раньше"); ok = false; }

            // Карточка с одним заполненным полем и пустыми остальными — так выглядит только что созданная.
            var sparse = new CharacterCard { Who = "сумрак", Where = null, Wants = "", HowToPlay = null };
            if (PageSearch.Find(blocks, sparse, "сумрак", resolve).Count != 2)
            { Debug.LogError("FAIL: пустые поля карточки посчитаны как совпадения"); ok = false; }

            if (ok) Debug.Log("Self-Test: Page Search Character Card — OK");
        }

        [ContextMenu("Self-Test: Page Search")]
        public void SelfTestPageSearch()
        {
            bool ok = true;
            var resolve = Resolver();

            // THE WHOLE POINT: the query matches the rendered name, and the offset is where that name is on
            // SCREEN. Searching the source would find neither — «Брод» appears nowhere in the stored text,
            // which reads «Отряд идёт в [[poi:poi-1|Тихий Брод]] к утру».
            var linked = Page("Отряд идёт в [[poi:poi-1|Тихий Брод]] к утру");
            var hits = PageSearch.Find(linked, "Брод", resolve);
            if (hits.Count != 1)
            { Debug.LogError($"FAIL: a name inside a link gave {hits.Count} hits, want 1"); ok = false; }
            else
            {
                // «Отряд идёт в Тихий Брод к утру» — «Брод» starts at 19.
                string display = NotesLinkOps.BuildDisplay(linked[0].Text, resolve).Text;
                if (display.Substring(hits[0].DisplayStart, hits[0].Length) != "Брод")
                { Debug.LogError($"FAIL: the offset pointed at «{display.Substring(hits[0].DisplayStart, hits[0].Length)}», want «Брод»"); ok = false; }
            }

            // A query that only occurs in the MACHINERY — the kind, the id, the brackets — is not on the
            // page and must not be found. This is the half of the rule that a source search would fail.
            foreach (var invisible in new[] { "poi", "poi-1", "[[", "|" })
                if (PageSearch.Find(linked, invisible, resolve).Count != 0)
                { Debug.LogError($"FAIL: «{invisible}» was found in text the DM cannot see"); ok = false; }

            // An UNRESOLVED link still renders its stored name (the deleted-target rule), so it is still
            // findable — the sentence reads the same, only muted.
            var dead = Page("Дорога к [[poi:gone|Старый Мост]] размыта");
            if (PageSearch.Find(dead, "Мост", resolve).Count != 1)
            { Debug.LogError("FAIL: a name whose target is gone was not findable"); ok = false; }

            // Document order across blocks, and the block a hit belongs to.
            var many = Page("Тихий вечер", "ничего", "тихий рассвет");
            var ordered = PageSearch.Find(many, "тих", resolve);
            if (ordered.Count != 2 || ordered[0].BlockIndex != 0 || ordered[1].BlockIndex != 2
                || ordered[0].BlockId != many[0].Id || ordered[1].BlockId != many[2].Id)
            { Debug.LogError("FAIL: hits were not in document order, or named the wrong blocks"); ok = false; }

            // Case-insensitive over Cyrillic, in BOTH directions.
            if (PageSearch.Find(Page("ТИХИЙ"), "тихий", resolve).Count != 1
                || PageSearch.Find(Page("тихий"), "ТИХИЙ", resolve).Count != 1)
            { Debug.LogError("FAIL: Cyrillic case folding did not work in both directions"); ok = false; }

            // NON-OVERLAPPING: «аа» in «аааа» is two, not three. «2 из 3» has to be steppable.
            var repeated = PageSearch.Find(Page("аааа"), "аа", resolve);
            if (repeated.Count != 2 || repeated[0].DisplayStart != 0 || repeated[1].DisplayStart != 2)
            { Debug.LogError($"FAIL: overlapping matches were counted ({repeated.Count} hits), want 2 at 0 and 2"); ok = false; }

            // Several hits inside ONE block, each with its own offset.
            var twice = PageSearch.Find(Page("тихо, ещё тихо"), "тихо", resolve);
            if (twice.Count != 2 || twice[0].DisplayStart != 0 || twice[1].DisplayStart != 10)
            { Debug.LogError("FAIL: two hits in one block were not both found at their own offsets"); ok = false; }

            // A picture stores no text the DM reads; whatever else it carries is not searchable.
            var withImage = Page("Тихий Брод");
            withImage.Add(new DocBlock { Kind = BlockKind.Image, Depth = 1, Text = "Тихий Брод" });
            if (PageSearch.Find(withImage, "Тихий", resolve).Count != 1)
            { Debug.LogError("FAIL: a picture block was searched"); ok = false; }

            // A COLLAPSED section is still searched — hiding it is a reading decision, and the bar's job is
            // to open what it landed in.
            var collapsed = Page("раздел", "спрятанное слово");
            collapsed[0].Kind = BlockKind.Section;
            collapsed[0].Depth = 0;
            collapsed[0].Collapsed = true;
            if (PageSearch.Find(collapsed, "спрятанное", resolve).Count != 1)
            { Debug.LogError("FAIL: text inside a collapsed section was skipped"); ok = false; }

            // Ordinary inputs that must not throw and must not match everything.
            if (PageSearch.Find(Page("что-нибудь"), "", resolve).Count != 0
                || PageSearch.Find(Page("что-нибудь"), null, resolve).Count != 0
                || PageSearch.Find(null, "что", resolve).Count != 0)
            { Debug.LogError("FAIL: an empty query or an absent page did not come back empty"); ok = false; }

            // A WHITESPACE-ONLY query finds nothing, and the text here has to CONTAIN spaces for that to be
            // worth asserting — against a page with no spaces in it, a rule that refuses whitespace and one
            // that searches for it give the same answer, and the assertion proves nothing. (It was written
            // that way first; mutation testing said so.) The rule itself: the bar sits open and half-typed
            // for as long as it takes to reach the first letter, and lighting every gap between words at
            // that moment is the opposite of helping.
            if (PageSearch.Find(Page("что нибудь ещё"), " ", resolve).Count != 0
                || PageSearch.Find(Page("что нибудь ещё"), "   ", resolve).Count != 0)
            { Debug.LogError("FAIL: a whitespace-only query lit up the gaps between words"); ok = false; }

            // A query LONGER than the text must not walk off the end.
            if (PageSearch.Find(Page("ох"), "охохо", resolve).Count != 0)
            { Debug.LogError("FAIL: a query longer than the text was not handled"); ok = false; }

            // A null resolver renders links as their STORED names (NotesLinkOps' own rule), so a page still
            // searches before the world has been wired up.
            if (PageSearch.Find(linked, "Брод", null).Count != 1)
            { Debug.LogError("FAIL: searching with no resolver did not fall back to stored names"); ok = false; }

            Debug.Log(ok ? "Self-Test Page Search: PASS" : "Self-Test Page Search: FAIL");
        }

        /// <summary>Opening what hides a hit — the other half of «a collapsed section is still searched».
        /// A search that reports twelve matches and cannot show four of them is worse than one that found
        /// eight.</summary>
        [ContextMenu("Self-Test: Expand To Block")]
        public void SelfTestExpandToBlock()
        {
            bool ok = true;

            // The shape matters more than the contents here. Every one of these five collapsed blocks is
            // shallower-or-equal to the target, and only TWO of them are its ancestors — which is the whole
            // thing this method has to get right:
            //
            //   0 чужой раздел  d0  ✗ collapsed — an earlier top-level section, already CLOSED before the
            //                          target's branch begins. Shallower than the target but not above it.
            //   1 раздел        d0  ✓ collapsed — ancestor
            //   2 пункт         d1  ✓ collapsed — ancestor
            //   3 сосед         d2  ✗ collapsed — the target's own SIBLING, at exactly its depth
            //   4 цель          d2    the target
            //   5 другой        d0  ✗ collapsed — later in the document; never even walked
            //
            // Written this flat first time round and it proved nothing: with only ancestors in front of the
            // target, a rule that walks the ancestor chain and a rule that opens every earlier block give
            // the same answer. Mutation testing said so twice.
            var blocks = new List<DocBlock>
            {
                new DocBlock { Kind = BlockKind.Section, Depth = 0, Text = "чужой раздел", Collapsed = true },
                new DocBlock { Kind = BlockKind.Section, Depth = 0, Text = "раздел", Collapsed = true },
                new DocBlock { Kind = BlockKind.Item, Depth = 1, Text = "пункт", Collapsed = true },
                new DocBlock { Kind = BlockKind.Prose, Depth = 2, Text = "сосед", Collapsed = true },
                new DocBlock { Kind = BlockKind.Prose, Depth = 2, Text = "цель" },
                new DocBlock { Kind = BlockKind.Section, Depth = 0, Text = "другой", Collapsed = true },
            };
            const int Target = 4;

            if (NotesDocOps.VisibleIndices(blocks).Contains(Target))
            { Debug.LogError("FAIL: the target was visible before anything was expanded"); ok = false; }

            if (!NotesDocOps.ExpandTo(blocks, blocks[Target].Id))
            { Debug.LogError("FAIL: expanding to a hidden block reported no change"); ok = false; }

            if (!NotesDocOps.VisibleIndices(blocks).Contains(Target))
            { Debug.LogError("FAIL: the target was still hidden after expanding to it"); ok = false; }

            // BOTH ancestors opened — opening only the outermost would leave the target inside the inner one.
            if (blocks[1].Collapsed || blocks[2].Collapsed)
            { Debug.LogError("FAIL: an ancestor was left collapsed"); ok = false; }

            // AND NOTHING ELSE DID. Jumping to a match must not quietly unfold the rest of the page: not the
            // section that closed before this branch, not the sibling beside it, not the one after.
            if (!blocks[0].Collapsed)
            { Debug.LogError("FAIL: an earlier, unrelated section was opened"); ok = false; }
            if (!blocks[3].Collapsed)
            { Debug.LogError("FAIL: the target's own sibling was opened"); ok = false; }
            if (!blocks[5].Collapsed)
            { Debug.LogError("FAIL: a section after the target was opened"); ok = false; }

            // Already visible: nothing to do, and it says so rather than reporting a change that would cost
            // the caller a rebuild.
            if (NotesDocOps.ExpandTo(blocks, blocks[Target].Id))
            { Debug.LogError("FAIL: expanding to an already visible block reported a change"); ok = false; }

            // A top-level block has no ancestors to open.
            if (NotesDocOps.ExpandTo(blocks, blocks[0].Id))
            { Debug.LogError("FAIL: expanding to a top-level block reported a change"); ok = false; }

            if (NotesDocOps.ExpandTo(blocks, "нет такого") || NotesDocOps.ExpandTo(null, "что-то"))
            { Debug.LogError("FAIL: expanding to a block that does not exist reported a change"); ok = false; }

            Debug.Log(ok ? "Self-Test Expand To Block: PASS" : "Self-Test Expand To Block: FAIL");
        }

        [ContextMenu("Self-Test: Page Search Step")]
        public void SelfTestPageSearchStep()
        {
            bool ok = true;

            // Forward from nowhere lands on the first; backward from nowhere lands on the last, so
            // Shift+Enter as the FIRST key goes to the end of the page rather than refusing.
            if (PageSearch.Step(5, -1, true) != 0 || PageSearch.Step(5, -1, false) != 4)
            { Debug.LogError("FAIL: stepping from no current hit went to the wrong end"); ok = false; }

            if (PageSearch.Step(5, 2, true) != 3 || PageSearch.Step(5, 2, false) != 1)
            { Debug.LogError("FAIL: an ordinary step went to the wrong place"); ok = false; }

            // WRAPPING BOTH WAYS. A search that stops at the last match reads as a search that broke.
            if (PageSearch.Step(5, 4, true) != 0 || PageSearch.Step(5, 0, false) != 4)
            { Debug.LogError("FAIL: stepping did not wrap round"); ok = false; }

            // One hit: every step stays on it rather than going to -1.
            if (PageSearch.Step(1, 0, true) != 0 || PageSearch.Step(1, 0, false) != 0)
            { Debug.LogError("FAIL: a single hit was stepped off"); ok = false; }

            if (PageSearch.Step(0, -1, true) != -1 || PageSearch.Step(0, 3, false) != -1)
            { Debug.LogError("FAIL: stepping an empty result did not come back empty"); ok = false; }

            Debug.Log(ok ? "Self-Test Page Search Step: PASS" : "Self-Test Page Search Step: FAIL");
        }
    }
}
