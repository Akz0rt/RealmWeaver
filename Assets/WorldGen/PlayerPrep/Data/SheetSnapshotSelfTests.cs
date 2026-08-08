using UnityEngine;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Самопроверки сравнения «лист против своего снимка».
    ///
    /// ГЛАВНЫЙ МУТАНТ, ради которого написан весь набор: снимок, собранный из «главных» полей —
    /// `Of(file) => file.Name` или `file.Name + file.Level`. Он выглядит разумно, проходит проверку
    /// «одинаковые файлы одинаковы» и теряет всё остальное молча. Поэтому НИ ОДНА проверка ниже не
    /// правит имя: правится предыстория, список снаряжения и вписанный максимум хитов — ровно то,
    /// чего в таком снимке нет.</summary>
    public class SheetSnapshotSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: снимок — нетронутый лист не считается изменённым")]
        public void SelfTestUntouchedFileIsUnchanged()
        {
            // Мутант: Differs всегда true — «сохранить?» спрашивали бы при каждом выходе, и вопрос
            // перестал бы что-либо значить.
            var file = Fixtures.Character();
            string snapshot = SheetSnapshot.Of(file);
            bool ok = !SheetSnapshot.Differs(snapshot, file);
            if (!ok) Debug.LogError("FAIL снимок: нетронутый лист объявлен изменённым");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — правка ОДНОГО поля видна")]
        public void SelfTestOneFieldDiffers()
        {
            // ОБЯЗАТЕЛЬНЫЙ мутант задачи: «считать одинаковыми файлы, различающиеся одним полем».
            // Поле взято НЕ имя и НЕ уровень намеренно — см. шапку класса: снимок из пары «имя +
            // уровень» на правке имени как раз покраснел бы и оставил мутанта в живых.
            var file = Fixtures.Character();
            string snapshot = SheetSnapshot.Of(file);
            file.Backstory = "Его выгнали из гильдии за то, чего он не делал.";
            bool ok = SheetSnapshot.Differs(snapshot, file);
            if (!ok) Debug.LogError("FAIL снимок: правка предыстории не замечена");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — пометка плана прокачки видна")]
        public void SelfTestPlanMarkDiffers()
        {
            // Панель плана — ПЕРВАЯ поверхность, у которой весь итог работы — это file.Plan и прибавки:
            // ни имени, ни предыстории она не трогает. Не попади план в снимок, игрок расписал бы себе
            // уровни с 8 по 20, вышел «← К списку листов» и не получил бы даже вопроса — работа
            // пропала бы молча. Проверяется ОБЕИМИ дорогами панели: пометка и прибавка.
            var byMark = Fixtures.Character();
            string markSnapshot = SheetSnapshot.Of(byMark);
            LevelChoiceOps.ChooseFeat(byMark, 8, "alert");
            bool ok = SheetSnapshot.Differs(markSnapshot, byMark);

            var byBump = Fixtures.Character();
            string bumpSnapshot = SheetSnapshot.Of(byBump);
            LevelChoiceOps.ChooseAsi(byBump, 8, new System.Collections.Generic.List<string> { "dex" });
            ok &= SheetSnapshot.Differs(bumpSnapshot, byBump);

            if (!ok) Debug.LogError("FAIL снимок: правка плана прокачки не замечена "
                                  + $"(черта={SheetSnapshot.Differs(markSnapshot, byMark)}, "
                                  + $"прибавки={SheetSnapshot.Differs(bumpSnapshot, byBump)})");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — правка СПИСКА видна")]
        public void SelfTestListDiffers()
        {
            // Мутант: снимок из скалярных полей. Списки (снаряжение, навыки, план уровней) —
            // половина листа, и потерять их молча — потерять час работы мастера.
            var file = Fixtures.Character();
            string snapshot = SheetSnapshot.Of(file);
            file.Equipment.Add("shield");
            bool ok = SheetSnapshot.Differs(snapshot, file);
            if (!ok) Debug.LogError("FAIL снимок: добавленная вещь не замечена");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — «пусто» и число различаются В ОБЕ стороны")]
        public void SelfTestNullAndValueDiffer()
        {
            // Ровно то, что заказал ДМ: вписать свой максимум хитов И ВЕРНУТЬ его обратно к среднему.
            // Мутант «сравнивать только заполненные поля» ломается на ВТОРОЙ половине: возврат к
            // null выглядел бы как «ничего не менялось», и стёртое число не сохранилось бы.
            var toValue = Fixtures.Character();
            toValue.MaxHpOverride = null;
            string emptySnapshot = SheetSnapshot.Of(toValue);
            toValue.MaxHpOverride = 44;
            bool forward = SheetSnapshot.Differs(emptySnapshot, toValue);

            var toEmpty = Fixtures.Character();
            toEmpty.MaxHpOverride = 44;
            string valueSnapshot = SheetSnapshot.Of(toEmpty);
            toEmpty.MaxHpOverride = null;
            bool backward = SheetSnapshot.Differs(valueSnapshot, toEmpty);

            bool ok = forward && backward;
            if (!ok) Debug.LogError($"FAIL снимок: пусто→44 замечено={forward}, 44→пусто замечено={backward}");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — открытый и не тронутый файл не переспрашивает")]
        public void SelfTestReloadedFileIsUnchanged()
        {
            // Снимок снимается с ОБЪЕКТА, а сравнивается с объектом, пришедшим из файла. Мутант
            // «снимок по личности объекта» (добавить к строке GetHashCode или сравнивать ссылки)
            // проходит проверку «нетронутый лист» — там объект тот же самый — и падает здесь:
            // открытый с диска лист спрашивал бы «сохранить?» сразу, ничего не правив.
            var original = Fixtures.Character();
            string json = CharacterSerializer.ToJson(original);
            string snapshot = SheetSnapshot.Of(original);
            var loaded = CharacterSerializer.FromJson(json);
            bool ok = !SheetSnapshot.Differs(snapshot, loaded);
            if (!ok) Debug.LogError("FAIL снимок: лист, прочитанный из собственной записи, объявлен изменённым");
            Done(ok);
        }

        [ContextMenu("Self-Test: снимок — пустота и лист без снимка")]
        public void SelfTestNothingOpenIsUnchanged()
        {
            // Две границы разом.
            //   • Ничего не открыто: Of(null) обязан вернуть null, а не упасть — мутант «убрать
            //     проверку на null» роняет ToJson на первой же строке (он пишет FormatVersion).
            //     Differs(null, null) — «нечего терять», на экране списка вопроса быть не должно.
            //   • Лист есть, снимка нет — ИЗМЕНЕНИЕ. Мутант «нет снимка → считать неизменённым»
            //     молча выбрасывает только что созданного и заполненного персонажа.
            bool nullIsNull = SheetSnapshot.Of(null) == null;
            bool nothingOpen = !SheetSnapshot.Differs(null, null);
            bool neverSaved = SheetSnapshot.Differs(null, Fixtures.Character());
            bool ok = nullIsNull && nothingOpen && neverSaved;
            if (!ok) Debug.LogError($"FAIL снимок: Of(null)==null={nullIsNull}, пусто-против-пусто={nothingOpen}, "
                                  + $"лист-без-снимка-изменён={neverSaved}");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
