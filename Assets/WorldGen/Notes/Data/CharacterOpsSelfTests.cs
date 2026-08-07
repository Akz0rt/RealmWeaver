using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самопроверки для CharacterOps. По форме — ровно NotesLinkOpsSelfTests.cs: обычный MonoBehaviour,
    /// методы SelfTest*, каждый под [ContextMenu] для запуска из Editor, и офлайн через Tools/notes-harness
    /// (`powershell -File sync.ps1`, затем `dotnet run -c Release -- selftests` из bash).
    ///
    /// Каждый провал печатает ЧТО получилось и ЧЕГО хотели. Утверждения нацелены на правило, которое
    /// сломало бы изменение, а не на производное число.
    /// </summary>
    public class CharacterOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Plain Page Is Not A Character")]
        public void SelfTestPlainPageIsNotCharacter()
        {
            bool ok = true;

            // Мутант, которого ловит этот тест: IsCharacter возвращает true для любой страницы.
            var page = new NotesPage { Name = "Тихая Гавань" };
            if (CharacterOps.IsCharacter(page))
            { Debug.LogError("FAIL: страница без карточки сочтена персонажем"); ok = false; }
            page.Character = new CharacterCard();
            if (!CharacterOps.IsCharacter(page))
            { Debug.LogError("FAIL: страница с карточкой не сочтена персонажем"); ok = false; }

            Debug.Log(ok ? "Self-Test Plain Page Is Not A Character: PASS" : "Self-Test Plain Page Is Not A Character: FAIL");
        }

        [ContextMenu("Self-Test: Copy Card Does Not Alias")]
        public void SelfTestCopyCardDoesNotAlias()
        {
            bool ok = true;

            // Мутант: CopyCard возвращает тот же экземпляр (return card).
            // Фикстура построена так, чтобы правильное и неправильное правило РАСХОДИЛИСЬ: после копии
            // правим ОРИГИНАЛ и смотрим на копию.
            var portrait = new byte[] { 1, 2, 3 };
            var card = new CharacterCard
            {
                Who = "кузнец", Where = "Тихая Гавань", Wants = "выкупить долг",
                HowToPlay = "вытирает руки о фартук", Portrait = portrait,
            };
            var copy = CharacterOps.CopyCard(card);

            card.Who = "ИЗМЕНЕНО";
            card.Wants = "ИЗМЕНЕНО";
            if (copy.Who != "кузнец")
            { Debug.LogError($"FAIL: Who протёк в копию ({copy.Who})"); ok = false; }
            if (copy.Wants != "выкупить долг")
            { Debug.LogError($"FAIL: Wants протёк в копию ({copy.Wants})"); ok = false; }
            if (copy.Where != "Тихая Гавань" || copy.HowToPlay != "вытирает руки о фартук")
            { Debug.LogError($"FAIL: копия потеряла поле (Where={copy.Where}, HowToPlay={copy.HowToPlay})"); ok = false; }
            // Байты портрета РАЗДЕЛЯЮТСЯ намеренно — портрет заменяется целиком, никогда не правится
            // на месте (тот же договор, что у DocBlock.ImageBytes).
            if (!ReferenceEquals(copy.Portrait, portrait))
            { Debug.LogError("FAIL: портрет скопирован, хотя должен разделяться"); ok = false; }

            Debug.Log(ok ? "Self-Test Copy Card Does Not Alias: PASS" : "Self-Test Copy Card Does Not Alias: FAIL");
        }

        [ContextMenu("Self-Test: Copy Null Card")]
        public void SelfTestCopyNullCard()
        {
            bool ok = true;

            // Мутант: CopyCard(null) кидает исключение вместо null.
            if (CharacterOps.CopyCard(null) != null)
            { Debug.LogError("FAIL: копия отсутствующей карточки не null"); ok = false; }

            Debug.Log(ok ? "Self-Test Copy Null Card: PASS" : "Self-Test Copy Null Card: FAIL");
        }
    }
}
