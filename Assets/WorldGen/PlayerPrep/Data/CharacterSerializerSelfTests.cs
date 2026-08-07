using System.Linq;
using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    public class CharacterSerializerSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: файл — круговой обход сохраняет все выборы")]
        public void SelfTestRoundTripKeepsChoices()
        {
            var before = Fixtures.Character();
            before.Plan.Add(new LevelChoice { Level = 4, Kind = "feat", ValueId = "alert" });
            before.Backstory = "Три строки\nс переносами\nи «кавычками»";
            // Ожидаемые значения снимаются ДО вызова ToJson, а не читаются из `before` ПОСЛЕ него:
            // если сериализатор мутирует сам объект file (например, "чистит" текст перед записью —
            // `before` и `file` внутри ToJson это ОДИН И ТОТ ЖЕ объект), сравнение с `before` после
            // вызова не заметило бы потери — обе стороны сравнения оказались бы одинаково испорчены.
            string expectedName = before.Name;
            int expectedLevel = before.Level;
            int expectedDex = before.Base.Dex;
            int expectedBumpsCount = before.Bumps.Count;
            var expectedSkillIds = before.SkillIds.ToList();
            var expectedExpertiseIds = before.ExpertiseIds.ToList();
            string expectedBackstory = before.Backstory;

            var after = CharacterSerializer.FromJson(CharacterSerializer.ToJson(before));
            bool ok = after.Name == expectedName
                   && after.Level == expectedLevel
                   && after.Base.Dex == expectedDex
                   && after.Bumps.Count == expectedBumpsCount
                   && after.Bumps[0].Source == "background" && after.Bumps[0].AbilityId == "str"
                   && after.SkillIds.SequenceEqual(expectedSkillIds)
                   && after.ExpertiseIds.SequenceEqual(expectedExpertiseIds)
                   && after.Plan.Count == 1 && after.Plan[0].Kind == "feat"
                   && after.Backstory == expectedBackstory;
            if (!ok) Debug.LogError("FAIL круговой обход: что-то не пережило запись и чтение");
            Done(ok);
        }

        [ContextMenu("Self-Test: файл — версия формата записывается")]
        public void SelfTestFormatVersionIsWritten()
        {
            // FormatVersion нарочно взведён в заведомо неверное значение: CharacterFile.FormatVersion
            // по умолчанию и так равно CurrentFormatVersion (оба = 1), поэтому проверка на объекте
            // "как есть" не отличила бы ToJson, который проставляет версию сам, от ToJson, который
            // просто полагается на совпадение значения по умолчанию.
            var before = Fixtures.Character();
            before.FormatVersion = -1;
            string json = CharacterSerializer.ToJson(before);
            bool ok = json.Contains("\"FormatVersion\"")
                   && CharacterSerializer.FromJson(json).FormatVersion == CharacterSerializer.CurrentFormatVersion;
            if (!ok) Debug.LogError("FAIL версия формата: " + json.Substring(0, System.Math.Min(120, json.Length)));
            Done(ok);
        }

        [ContextMenu("Self-Test: файл — версия из будущего не читается молча")]
        public void SelfTestFutureVersionRejected()
        {
            string original = CharacterSerializer.ToJson(Fixtures.Character());
            string json = original.Replace("\"FormatVersion\": 1", "\"FormatVersion\": 99");
            // Если подмена не сработала (поменялось оформление JSON), самопроверка была бы
            // зелёной от того, что проверяла неизменённый файл. Ловим это отдельно.
            if (json == original)
            { Debug.LogError("FAIL версия из будущего: подмена номера в JSON не сработала"); return; }
            bool threw = false;
            try { CharacterSerializer.FromJson(json); }
            catch (CharacterFormatException) { threw = true; }
            if (!threw) Debug.LogError("FAIL версия из будущего: файл прочитался, хотя формат новее программы");
            Done(threw);
        }

        [ContextMenu("Self-Test: файл — пустой JSON не читается молча")]
        public void SelfTestEmptyJsonRejected()
        {
            // Newtonsoft на "null" и на "" отдаёт null, а не бросает исключение сам — проверка
            // на null внутри FromJson не подстрахована ничем другим. Ни один другой самотест
            // не зовёт FromJson с таким входом, так что дыра в этом месте была бы невидимой.
            bool threw = false;
            try { CharacterSerializer.FromJson("null"); }
            catch (CharacterFormatException) { threw = true; }
            if (!threw) Debug.LogError("FAIL пустой JSON: \"null\" прочитался как лист персонажа");
            Done(threw);
        }

        [ContextMenu("Self-Test: файл — портрет переживает запись")]
        public void SelfTestPortraitSurvives()
        {
            var before = Fixtures.Character();
            before.Portrait = new byte[] { 1, 2, 3, 250, 251 };
            // Снимок ДО вызова ToJson — той же причины, что и в круговом обходе: если ToJson
            // подменит file.Portrait на пустой массив ПЕРЕД записью (тот же объект, что и `before`),
            // сравнение с `before` после вызова не заметило бы потери.
            var expectedPortrait = before.Portrait.ToArray();
            var after = CharacterSerializer.FromJson(CharacterSerializer.ToJson(before));
            bool ok = after.Portrait != null && after.Portrait.Length == expectedPortrait.Length
                   && after.Portrait.SequenceEqual(expectedPortrait);
            if (!ok) Debug.LogError("FAIL портрет не пережил запись");
            Done(ok);
        }

        [ContextMenu("Self-Test: файл — незаконченный персонаж сохраняется")]
        public void SelfTestIncompleteCharacterSaves()
        {
            // Бросить мастер на середине и вернуться завтра — нормально, это требование спека.
            var half = new CharacterFile { RulesId = "test", Name = "Полу" };
            var after = CharacterSerializer.FromJson(CharacterSerializer.ToJson(half));
            bool ok = after.Name == "Полу" && after.ClassId == null;
            if (!ok) Debug.LogError("FAIL незаконченный персонаж не сохраняется");
            Done(ok);
        }

        [ContextMenu("Self-Test: файл — вписанный максимум хитов переживает запись, а невписанный остаётся пустым")]
        public void SelfTestMaxHpOverrideRoundTrips()
        {
            // Единственное ПОСЧИТАННОЕ число, которое файл хранит вообще. Проверяем обе стороны:
            // вписанное значение доезжает, а НЕвписанное остаётся null — иначе мутант «писать 0
            // вместо null» превратил бы «беру среднее» в «у меня ноль хитов» незаметно.
            var withValue = Fixtures.Character(); withValue.MaxHpOverride = 44;
            var back = CharacterSerializer.FromJson(CharacterSerializer.ToJson(withValue));
            var without = Fixtures.Character(); without.MaxHpOverride = null;
            var backNull = CharacterSerializer.FromJson(CharacterSerializer.ToJson(without));
            bool ok = back.MaxHpOverride == 44 && !backNull.MaxHpOverride.HasValue;
            if (!ok) Debug.LogError($"FAIL максимум хитов: вписанный дал {back.MaxHpOverride} (ждали 44), "
                                  + $"невписанный дал {backNull.MaxHpOverride} (ждали пусто)");
            Done(ok);
        }

        [ContextMenu("Self-Test: файл СТАРОЙ версии читается, а не отвергается")]
        public void SelfTestOlderFormatVersionIsAccepted()
        {
            // Отвергать надо только версии ИЗ БУДУЩЕГО. Мутант «отвергать всё, что не равно
            // текущей» ломает совместимость со всеми ранее сохранёнными листами — и без этой
            // проверки такое изменение проходит незамеченным.
            string original = CharacterSerializer.ToJson(Fixtures.Character());
            string old = original.Replace("\"FormatVersion\": 1", "\"FormatVersion\": 0");
            if (old == original)
            { Debug.LogError("FAIL старая версия: подмена номера в JSON не сработала"); return; }
            bool ok;
            try { ok = CharacterSerializer.FromJson(old) != null; }
            catch (CharacterFormatException e) { ok = false; Debug.LogError($"FAIL старая версия отвергнута: {e.Message}"); }
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
