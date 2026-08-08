using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самопроверки для MentionQuery. По форме — ровно MentionSuggestSelfTests.cs: обычный MonoBehaviour,
    /// методы SelfTest*, каждый под [ContextMenu], офлайн через Tools/notes-harness.
    /// </summary>
    public class MentionQuerySelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Right After At Is Empty Query")]
        public void SelfTestRightAfterAtIsEmptyQuery()
        {
            bool ok = true;

            // Каретка сразу за только что набранным «@» — рулинг 1: «пока ничего не набрано».
            bool found = MentionQuery.TryFind("Привет, @", 9, out int atIndex, out string query);
            if (!found || atIndex != 8 || query != "")
            { Debug.LogError($"FAIL: «Привет, @» с кареткой в конце — хотели найдено=true, atIndex=8, query=\"\", получили найдено={found}, atIndex={atIndex}, query=\"{query}\""); ok = false; }

            Debug.Log(ok ? "Self-Test Right After At Is Empty Query: PASS" : "Self-Test Right After At Is Empty Query: FAIL");
        }

        [ContextMenu("Self-Test: Query Narrows With Each Character")]
        public void SelfTestQueryNarrowsWithEachCharacter()
        {
            bool ok = true;

            // Мутант «off-by-one в срезе query»: включить «@» в query дало бы "@Оль" вместо "Оль".
            bool found = MentionQuery.TryFind("@Оль", 4, out int atIndex, out string query);
            if (!found || atIndex != 0 || query != "Оль")
            { Debug.LogError($"FAIL: «@Оль» целиком — хотели atIndex=0, query=\"Оль\", получили atIndex={atIndex}, query=\"{query}\""); ok = false; }

            Debug.Log(ok ? "Self-Test Query Narrows With Each Character: PASS" : "Self-Test Query Narrows With Each Character: FAIL");
        }

        [ContextMenu("Self-Test: Space Closes, Email Is Not A Trap")]
        public void SelfTestSpaceClosesEmailIsNotATrap()
        {
            bool ok = true;

            // «@» СРАЗУ после слова без пробела (обычный e-mail) — рулинг 1 не требует границы слова
            // перед «@», так что список ДОЛЖЕН находиться, пока далее нет пробела.
            bool foundBefore = MentionQuery.TryFind("почта@сайт", 10, out int atIndexBefore, out string queryBefore);
            if (!foundBefore || atIndexBefore != 5 || queryBefore != "сайт")
            { Debug.LogError($"FAIL: «почта@сайт» без пробела — хотели найдено=true, atIndex=5, query=\"сайт\", получили найдено={foundBefore}, atIndex={atIndexBefore}, query=\"{queryBefore}\""); ok = false; }

            // МУТАНТ: снята проверка char.IsWhiteSpace — без неё разбор прошёл бы СКВОЗЬ пробел и
            // «нашёл» бы то же самое «@», хотя рулинг 4 требует, чтобы пробел до выбора закрывал список.
            bool foundAfter = MentionQuery.TryFind("почта@сайт ", 11, out _, out _);
            if (foundAfter)
            { Debug.LogError("FAIL: пробел после «@сайт» должен закрыть запрос (рулинг 4), а TryFind всё ещё его нашёл"); ok = false; }

            Debug.Log(ok ? "Self-Test Space Closes Email Is Not A Trap: PASS" : "Self-Test Space Closes Email Is Not A Trap: FAIL");
        }

        [ContextMenu("Self-Test: No At At All Or Caret Before It Finds Nothing")]
        public void SelfTestNoAtAtAllOrCaretBeforeItFindsNothing()
        {
            bool ok = true;

            if (MentionQuery.TryFind("просто текст без собаки", 10, out _, out _))
            { Debug.LogError("FAIL: строка без «@» не должна ничего находить"); ok = false; }

            // Каретка ДО «@» (например, после стрелки влево) — «@» лежит впереди, а не позади каретки.
            if (MentionQuery.TryFind("@текст", 0, out _, out _))
            { Debug.LogError("FAIL: каретка перед «@» не должна находить запрос — «@» ещё не набран каретой"); ok = false; }

            Debug.Log(ok ? "Self-Test No At At All Or Caret Before It Finds Nothing: PASS" : "Self-Test No At At All Or Caret Before It Finds Nothing: FAIL");
        }
    }
}
