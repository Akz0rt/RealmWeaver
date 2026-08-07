using System.Collections.Generic;
using WorldGen.Workspace.Data;

namespace WorldGen.Notes.Data
{
    /// <summary>One row the «@» popup can offer. Kind carries the same three-letter vocabulary a link token
    /// does (NotesLinkOps.KindPage, or NotesLinkOps.KindOf a WorldRefKind) — a later task turns a chosen
    /// candidate straight into [[Kind:Id|Name]] the same way QuickOpen.TryTokenFor turns a QuickHit into
    /// one, so the two shapes deliberately mirror each other. Subtitle is the row's second line: a
    /// character's role (CharacterOps — page.Character.Who) for a character page, a world object's
    /// KindLabel (e.g. «город») for a POI/building/room, or "" for an ordinary page, which has neither.</summary>
    public class MentionCandidate
    {
        public string Kind;
        public string Id;
        public string Name;
        public string Subtitle;
    }

    /// <summary>
    /// RANKING ONLY — the popup itself (matching UI, keyboard nav, the insert gesture) is a later task; this
    /// is the pure "who to propose, in which order" layer, testable offline in Tools/notes-harness because
    /// it touches no UnityEngine.
    ///
    /// ДМ рулинг 2026-08-07, «@» reaches exactly what Ctrl+K reaches: персонажи, любые страницы, объекты
    /// мира — один список, одно правило. The substring MATCH itself is not a second implementation of
    /// QuickOpen's: TryMatchRank below calls QuickOpen.TryFoldMatch, the exact rule
    /// CollectWorldMapHit/CollectWorldHits/CollectNameHit already shared before this task extracted it — see
    /// that method's own doc. A second, hand-rolled fold here is exactly the drift the DM's ruling warns
    /// against: the day the two disagreed about what "matches" means, nobody could explain why «@» found
    /// something Ctrl+K didn't, or the other way round.
    ///
    /// EMPTY QUERY IS NOT EMPTY (rule 2). The instant the DM types «@», before a single letter of the name
    /// follows, the scene almost always continues talking about someone already in it — so the first
    /// suggestions are the page's OWN mentions, most-recently-mentioned first — a mention meaning exactly
    /// what NotesDocOps.BlockReferencesPage means (the rule FindBacklinks uses for «Упомянута в»): an inline
    /// [[page:…]]/[[poi:…]]/… token found via ParseSpans over a block's Text OR Detail (a mention hidden in
    /// a ⊕ Detail body is a mention there and it is one here too), OR a bare 📄 LinkedPageId pin with no
    /// inline token at all — then whatever the DM inserted via this very popup earlier in the session
    /// (`recentIds`, order preserved, nothing re-ranks it). See MentionedOnPage's own doc for why both
    /// mechanisms are read and where a pin sits in the recency order.
    ///
    /// RECENTIDS' FORMAT IS "kind:id" — NOT bare ids. The interface only promises a list of strings; a bare
    /// id would be ambiguous the moment a page and a POI happened to share one (both id spaces are free-form
    /// GUIDs from independent generators, so nothing rules that out by construction). "kind:id" costs
    /// nothing extra for the caller — it already has both halves from the very insert this list is built
    /// from — and removes the ambiguity outright. Parsed with IndexOf(':') rather than Split, so an id that
    /// itself contains a colon (none do today, but nothing enforces that) still round-trips: only the FIRST
    /// colon is the separator.
    ///
    /// NON-EMPTY QUERY (rule 3) drops the recency ordering and searches Name and Subtitle the normal way,
    /// exactly like Ctrl+K's page-name rank (NamePrefix beats NameContains) — with Subtitle treated as one
    /// rank tier weaker than Name, matching QuickOpen's own name-before-body precedent (Q1) rather than
    /// inventing a fifth rule. A candidate mentioned on THIS page still sorts ahead of an equally-ranked one
    /// that isn't — the ONLY tie-break rule 3 asks for, so `recentIds` plays no part once the DM has typed
    /// anything.
    ///
    /// NO DUPLICATES (rule 4), BY CONSTRUCTION ON BOTH PATHS, not by a de-dup pass bolted on after. The
    /// non-empty path visits every page and every world object EXACTLY ONCE each (one candidate per real
    /// object, full stop), so two rows for the same id cannot arise there. The empty path tracks a `seen`
    /// set explicitly, because it stitches two DIFFERENT lists (mentions, then recents) that legitimately
    /// name the same object twice — the mention wins, as the stronger rule, and the recent entry for the
    /// same id is skipped rather than appended a second time.
    ///
    /// THE CURRENT PAGE NEVER SUGGESTS ITSELF (rule 6) — a page linking to itself reads as a DM mistake
    /// (leftover from a copy-paste), never an intent, so it is refused wherever a candidate could be built:
    /// inside ParseSpans' own walk (a stray [[page:currentId|...]] token contributes nothing), inside the
    /// non-empty search loop (the current page is skipped before it is even matched against `query`), and
    /// inside recentIds' resolution (the same guard, so a recent list saved before a rename cannot resurrect
    /// it either).
    /// </summary>
    public static class MentionSuggest
    {
        // Тот же порядок рангов, что у QuickOpen (Q1): полное имя важнее подписи, префикс важнее
        // совпадения в середине. Не переизобретаем пятое правило — расширяем то же самое на две колонки
        // (имя, подпись) вместо одной (имя).
        const int NamePrefix = 0;
        const int NameContains = 1;
        const int SubtitlePrefix = 2;
        const int SubtitleContains = 3;

        public static List<MentionCandidate> Rank(NotesDocument doc, NotesPage current,
            IReadOnlyList<WorldObjectRef> world, string query, IReadOnlyList<string> recentIds, int limit)
        {
            var result = new List<MentionCandidate>();
            if (limit < 0) limit = 0;

            string currentId = current != null ? current.Id : null;
            var mentioned = MentionedOnPage(doc, current, world, currentId);

            string needle = (query ?? "").Trim().ToLowerInvariant();
            if (needle.Length == 0)
            {
                var seen = new HashSet<(string Kind, string Id)>();
                // `seen` is populated from `mentioned` WITHOUT a defensive re-filter here: MentionedOnPage
                // already guarantees one entry per (kind,id) by construction (TrackLinkedPage and TrackSpans
                // share the same order/lastPos, see its own doc). A redundant `if (!seen.Add(...)) continue`
                // here would silently ABSORB a bug in that guarantee instead of exposing it — the exact
                // "two dedup layers mask each other" trap a review round caught when the pin+inline-token
                // dedup test passed against a broken TrackSpans guard for no reason but this line. `seen`
                // is still built (just unconditionally) because AppendRecent needs it below, to skip a
                // recentIds entry that already came from `mentioned`.
                foreach (var m in mentioned)
                {
                    if (result.Count >= limit) break;
                    seen.Add((m.Kind, m.Id));
                    result.Add(m);
                }
                AppendRecent(result, doc, world, recentIds, currentId, seen, limit);
                return result;
            }

            // Непустой запрос — что упомянуто на странице, нужно только для тай-брейка при равном ранге
            // совпадения (rule 3), не для собственного места в списке.
            var mentionedKeys = new HashSet<(string Kind, string Id)>();
            foreach (var m in mentioned) mentionedKeys.Add((m.Kind, m.Id));

            var candidates = new List<(int Rank, int MentionedRank, int Seq, MentionCandidate Candidate)>();
            int seq = 0;

            if (doc != null)
                foreach (var g in doc.Groups)
                    foreach (var p in g.Pages)
                    {
                        if (p == null || p.Id == currentId) continue;   // rule 6
                        var c = MakePageCandidate(p);
                        if (TryMatchRank(c, needle, out int rank))
                            candidates.Add((rank, mentionedKeys.Contains((c.Kind, c.Id)) ? 0 : 1, seq++, c));
                    }

            if (world != null)
                foreach (var w in world)
                {
                    if (w == null) continue;
                    var c = MakeWorldCandidate(w);
                    if (TryMatchRank(c, needle, out int rank))
                        candidates.Add((rank, mentionedKeys.Contains((c.Kind, c.Id)) ? 0 : 1, seq++, c));
                }

            candidates.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank)
                : a.MentionedRank != b.MentionedRank ? a.MentionedRank.CompareTo(b.MentionedRank)
                : a.Seq.CompareTo(b.Seq));

            for (int i = 0; i < candidates.Count && result.Count < limit; i++)
                result.Add(candidates[i].Candidate);

            return result;
        }

        /// <summary>Rule 2's ordering: every reference inside `current`'s own blocks, one entry per distinct
        /// (kind,id), ordered by the position of its LAST occurrence, most-recent first.
        ///
        /// "REFERENCE" MEANS WHAT NotesDocOps.BlockReferencesPage MEANS, not a narrower reading of it — a
        /// review round caught the first version of this method reading only ParseSpans over Text/Detail,
        /// which is exactly half of what BlockReferencesPage (the rule FindBacklinks uses for «Упомянута
        /// в») actually counts: a bare `block.LinkedPageId == pageId` (the 📄 pin) is a reference there too,
        /// with no inline token needed at all. Missing it here made a page pinned-but-never-typed invisible
        /// to «@» while «Упомянута в» found it — precisely the "two searches disagree" failure rule 1 exists
        /// to prevent, just for mention-DETECTION instead of for matching. So this walks BOTH mechanisms,
        /// mirroring BlockReferencesPage's own two checks rather than reconstructing them from scratch.
        ///
        /// WHERE A PIN SITS IN THE RECENCY ORDER — A PIN HAS NO CHARACTER POSITION, so a rule had to be
        /// chosen, not derived. TrackLinkedPage runs BEFORE TrackSpans for the same block: a 📄 pin is the
        /// row's own identity, attached to the row itself rather than to a position within its prose, so it
        /// is treated as encountered the moment the row is reached — before anything inline in that same
        /// row. It still sits AFTER every block that came earlier and BEFORE every block that comes later,
        /// so "a pinned row ranks like any other reference in that row" holds at the block granularity that
        /// is the only granularity a pin has.
        ///
        /// DEDUPLICATION WHEN A ROW CARRIES BOTH — TrackLinkedPage and TrackSpans share the same `order`/
        /// `lastPos` pair and the same "add to `order` only on first sighting of the key" guard, so a block
        /// whose LinkedPageId AND whose Text both name the same page yields exactly one entry in `order`
        /// (added at the pin's earlier position), whose `lastPos` is then overwritten by the inline token's
        /// later position — the STRONGER of the two positions wins, the same "last write wins" rule that
        /// already resolves two separate mentions of the same page. No dedup pass exists separately from
        /// that overwrite; there is nothing else to write one.
        ///
        /// A self-reference is dropped inside the walk itself, for BOTH mechanisms (rule 6), and a reference
        /// whose target no longer exists resolves to null and is silently skipped — there is nothing to show
        /// a name for.</summary>
        static List<MentionCandidate> MentionedOnPage(NotesDocument doc, NotesPage current,
            IReadOnlyList<WorldObjectRef> world, string currentId)
        {
            var result = new List<MentionCandidate>();
            if (current == null || current.Blocks == null) return result;

            var order = new List<(string Kind, string Id)>();
            var lastPos = new Dictionary<(string Kind, string Id), int>();
            int pos = 0;

            foreach (var b in current.Blocks)
            {
                if (b == null) continue;
                TrackLinkedPage(b.LinkedPageId, currentId, order, lastPos, ref pos);
                TrackSpans(b.Text, currentId, order, lastPos, ref pos);
                TrackSpans(b.Detail, currentId, order, lastPos, ref pos);
            }

            // Later mention — higher: sort the DISTINCT keys by the position of their LAST sighting,
            // descending. A mutant that instead kept `order`'s first-seen sequence (i.e. skipped this sort
            // entirely) would list objects in APPEARANCE order rather than RECENCY order — the two disagree
            // whenever an object is mentioned more than once, or (as in the self-test fixture) whenever the
            // first-mentioned object's name happens to sort differently from its position.
            order.Sort((a, b) => lastPos[b].CompareTo(lastPos[a]));

            foreach (var key in order)
            {
                var c = ResolveCandidate(doc, world, key.Kind, key.Id);
                if (c != null) result.Add(c);
            }
            return result;
        }

        /// <summary>The 📄 half of BlockReferencesPage — see MentionedOnPage's own doc for where this sits
        /// in the recency order and how it dedups against an inline token to the same page.</summary>
        static void TrackLinkedPage(string linkedPageId, string currentId, List<(string, string)> order,
            Dictionary<(string, string), int> lastPos, ref int pos)
        {
            if (string.IsNullOrEmpty(linkedPageId)) return;
            pos++;
            if (linkedPageId == currentId) return;   // rule 6
            var key = (NotesLinkOps.KindPage, linkedPageId);
            if (!lastPos.ContainsKey(key)) order.Add(key);
            lastPos[key] = pos;
        }

        static void TrackSpans(string field, string currentId, List<(string, string)> order,
            Dictionary<(string, string), int> lastPos, ref int pos)
        {
            foreach (var span in NotesLinkOps.ParseSpans(field))
            {
                pos++;
                if (span.Kind == NotesLinkOps.KindPage && span.Id == currentId) continue;   // rule 6
                var key = (span.Kind, span.Id);
                if (!lastPos.ContainsKey(key)) order.Add(key);
                lastPos[key] = pos;
            }
        }

        /// <summary>The second half of the empty-query order — recently inserted THIS SESSION, in the
        /// order given, with anything already added (via `seen`) skipped rather than repeated (rule 4). See
        /// the class doc for why an entry is "kind:id" rather than a bare id.</summary>
        static void AppendRecent(List<MentionCandidate> result, NotesDocument doc, IReadOnlyList<WorldObjectRef> world,
            IReadOnlyList<string> recentIds, string currentId, HashSet<(string Kind, string Id)> seen, int limit)
        {
            if (recentIds == null) return;
            foreach (var raw in recentIds)
            {
                if (result.Count >= limit) break;
                if (string.IsNullOrEmpty(raw)) continue;

                int sep = raw.IndexOf(':');
                if (sep <= 0 || sep == raw.Length - 1) continue;
                string kind = raw.Substring(0, sep);
                string id = raw.Substring(sep + 1);

                if (kind == NotesLinkOps.KindPage && id == currentId) continue;   // rule 6
                if (!seen.Add((kind, id))) continue;   // rule 4 — already shown as a mention

                var c = ResolveCandidate(doc, world, kind, id);
                if (c != null) result.Add(c);
            }
        }

        /// <summary>What a "kind:id" pair names RIGHT NOW, or null when it names nothing any more (a
        /// deleted page, a POI that vanished with the DM's last regeneration). Reused by both the mention
        /// walk and recentIds' resolution — one lookup, so the two cannot disagree about what a given id
        /// currently is.</summary>
        static MentionCandidate ResolveCandidate(NotesDocument doc, IReadOnlyList<WorldObjectRef> world, string kind, string id)
        {
            if (kind == NotesLinkOps.KindPage)
            {
                var page = NotesDocOps.FindPage(doc, id);
                return page != null ? MakePageCandidate(page) : null;
            }
            if (world == null) return null;
            if (!NotesLinkOps.TryParseWorldKind(kind, out var worldKind)) return null;
            foreach (var o in world)
                if (o != null && o.Kind == worldKind && o.Id == id)
                    return MakeWorldCandidate(o);
            return null;
        }

        static MentionCandidate MakePageCandidate(NotesPage p) => new MentionCandidate
        {
            Kind = NotesLinkOps.KindPage,
            Id = p.Id,
            Name = p.Name,
            // Подпись — только у персонажа (CharacterOps.IsCharacter); у обычной страницы её нет.
            Subtitle = CharacterOps.IsCharacter(p) ? p.Character.Who : "",
        };

        static MentionCandidate MakeWorldCandidate(WorldObjectRef o) => new MentionCandidate
        {
            Kind = NotesLinkOps.KindOf(o.Kind),
            Id = o.Id,
            Name = o.Name,
            Subtitle = o.KindLabel,
        };

        /// <summary>Rule 3's search: Name first, Subtitle second, each split into prefix/contains exactly
        /// like QuickOpen's own name rank (Q1) — QuickOpen.TryFoldMatch is the SAME fold-and-IndexOf rule
        /// CollectWorldMapHit/CollectWorldHits/CollectNameHit already share, not a second one written here.
        /// A candidate with no Subtitle (an ordinary page) simply never reaches the Subtitle branch, since
        /// TryFoldMatch on an empty field cannot match a non-empty needle.</summary>
        static bool TryMatchRank(MentionCandidate c, string needle, out int rank)
        {
            if (QuickOpen.TryFoldMatch(c.Name, needle, out bool namePrefix))
            {
                rank = namePrefix ? NamePrefix : NameContains;
                return true;
            }
            if (!string.IsNullOrEmpty(c.Subtitle) && QuickOpen.TryFoldMatch(c.Subtitle, needle, out bool subtitlePrefix))
            {
                rank = subtitlePrefix ? SubtitlePrefix : SubtitleContains;
                return true;
            }
            rank = -1;
            return false;
        }
    }
}
