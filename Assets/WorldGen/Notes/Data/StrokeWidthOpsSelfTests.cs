using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class StrokeWidthOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Толщина — скорость в секундах, а не за кадр")]
        public void SelfTestSpeedIsPerSecond()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: то же движение, снятое вдвое реже. «Расстояние
            // за кадр» дало бы вдвое большее число и вдвое более тонкую линию на слабом компьютере.
            float dense = StrokeWidthOps.SpeedOf(10f, 0.1f);
            float sparse = StrokeWidthOps.SpeedOf(20f, 0.2f);
            bool ok = Mathf.Approximately(dense, sparse) && Mathf.Approximately(dense, 100f);
            if (!ok) Debug.LogError($"FAIL скорость: {dense} и {sparse}, ожидались обе по 100");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — нулевой шаг времени не даёт бесконечность")]
        public void SelfTestZeroDeltaIsSafe()
        {
            float s = StrokeWidthOps.SpeedOf(10f, 0f);
            bool ok = !float.IsInfinity(s) && !float.IsNaN(s);
            if (!ok) Debug.LogError($"FAIL нулевой шаг времени: {s}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — медленно толще, быстро тоньше")]
        public void SelfTestSlowIsThicker()
        {
            float still = StrokeWidthOps.MultiplierFor(0f);
            float fast = StrokeWidthOps.MultiplierFor(StrokeWidthOps.FastSpeed);
            bool ok = still > fast
                   && Mathf.Approximately(still, StrokeWidthOps.SlowMultiplier)
                   && Mathf.Approximately(fast, StrokeWidthOps.FastMultiplier);
            if (!ok) Debug.LogError($"FAIL направление: стоя {still}, быстро {fast}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — разброс ограничен с обеих сторон")]
        public void SelfTestMultiplierIsClamped()
        {
            // Без ограничения «тонкая» кисть на очень медленном движении становилась бы толстой, и
            // три кнопки толщины теряли бы смысл.
            float crawl = StrokeWidthOps.MultiplierFor(-50f);
            float rocket = StrokeWidthOps.MultiplierFor(StrokeWidthOps.FastSpeed * 100f);
            bool ok = crawl <= StrokeWidthOps.SlowMultiplier && rocket >= StrokeWidthOps.FastMultiplier;
            if (!ok) Debug.LogError($"FAIL ограничение: {crawl} и {rocket} вышли за {StrokeWidthOps.FastMultiplier}…{StrokeWidthOps.SlowMultiplier}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — множитель сглаживается по времени")]
        public void SelfTestMultiplierIsSmoothed()
        {
            // Без сглаживания каждый рывок мыши даёт свою кочку, и линия выходит бугристой.
            float once = StrokeWidthOps.Smooth(1.3f, 0.7f, 1f / 60f);
            bool movedNotJumped = once > 0.7f && once < 1.3f;

            // НА ОПОРНОЙ ЧАСТОТЕ (60 Гц) Smooth ОБЯЗАН СОВПАДАТЬ С ПРОСТЫМ Lerp НА SmoothingPerFrame
            // — это ровно то, что обещает doc-комментарий класса про смысл константы «доля за кадр».
            // Без этой проверки требование «на 60 Гц ничего не поменялось» живёт только в
            // комментарии: подмена опорной частоты (например 60→30) не меняется в «сдвинулось, но
            // не прыгнуло» (once всё ещё между 0.7 и 1.3) и не видна в проверке независимости от
            // частоты кадров (та сравнивает результат сам с собой на другой опорной частоте, а не
            // с эталоном — мутация масштабно-инвариантна и режет любую долю времени одинаково).
            // Mathf.Approximately тут проверен, а не принят на веру: для dt=1/60 промежуточная
            // арифметика даёт dt*60=1 и Pow(0.7,1)=0.7 без накопленной погрешности (степень ровно
            // 1), so once совпадает с прямым Lerp побитово (diff=0) — узкий допуск Approximately не
            // мешает; на мутанте 30 Гц разница ~0.08 (1.202 против 1.12), Approximately её ловит.
            float expected = Mathf.Lerp(StrokeWidthOps.SlowMultiplier, StrokeWidthOps.FastMultiplier, StrokeWidthOps.SmoothingPerFrame);
            bool matchesReferenceRate = Mathf.Approximately(once, expected);

            bool ok = movedNotJumped && matchesReferenceRate;
            if (!ok) Debug.LogError($"FAIL сглаживание множителя: {once} (ожидание на 60 Гц — {expected}), сдвинулось={movedNotJumped}, совпало с опорной частотой={matchesReferenceRate}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — сглаживание не зависит от частоты кадров")]
        public void SelfTestSmoothingIsFrameRateIndependent()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: одно и то же время (0.1 с), нарезанное по-разному
            // — одним крупным шагом и десятью мелкими. «Доля за вызов» без приведения к времени дала
            // бы множителю на частых кадрах (десять мелких шагов) уйти к цели куда дальше, чем на
            // редких (один крупный шаг) — тот же дефект, от которого уже защищена скорость в SpeedOf,
            // только теперь в сглаживании. Допуск 0.001: на три порядка меньше разрыва, который даёт
            // подделка (~0.4 на этих числах), и на порядки больше шума накопления float32 по десяти
            // шагам (в double тот же расчёт даёт разницу ~1e-16 — экспонента складывается точно).
            float oneStep = StrokeWidthOps.Smooth(1.3f, 0.7f, 0.1f);
            float tenSteps = 1.3f;
            for (int i = 0; i < 10; i++) tenSteps = StrokeWidthOps.Smooth(tenSteps, 0.7f, 0.01f);
            bool ok = Mathf.Abs(oneStep - tenSteps) < 0.001f;
            if (!ok) Debug.LogError($"FAIL частота кадров: один шаг {oneStep}, десять шагов {tenSteps}, разница {Mathf.Abs(oneStep - tenSteps)}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — нулевой шаг времени не двигает сглаживание")]
        public void SelfTestSmoothZeroDeltaIsSafe()
        {
            float s = StrokeWidthOps.Smooth(1.3f, 0.7f, 0f);
            bool ok = Mathf.Approximately(s, 1.3f) && !float.IsNaN(s);
            if (!ok) Debug.LogError($"FAIL нулевой шаг времени в сглаживании: {s}, ожидалось 1.3 без изменений");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — первая точка берёт толщину второй")]
        public void SelfTestFirstPointTakesSecond()
        {
            // Иначе каждый мазок начинается с кляксы: у первой точки скорости ещё нет.
            var pts = new List<StrokePoint>
            {
                new StrokePoint(0f, 0f, 0.10f),
                new StrokePoint(0.1f, 0f, 0.04f),
                new StrokePoint(0.2f, 0f, 0.03f),
            };
            StrokeWidthOps.FixFirstWidth(pts);
            bool ok = Mathf.Approximately(pts[0].W, 0.04f) && Mathf.Approximately(pts[2].W, 0.03f);
            if (!ok) Debug.LogError($"FAIL первая точка: {pts[0].W}, ожидалась 0.04 (и остальные не тронуты)");
            Done(ok);
        }

        [ContextMenu("Self-Test: Толщина — мазок из одной точки переживает правку")]
        public void SelfTestSinglePointSurvives()
        {
            var pts = new List<StrokePoint> { new StrokePoint(0f, 0f, 0.05f) };
            StrokeWidthOps.FixFirstWidth(pts);
            bool ok = pts.Count == 1 && Mathf.Approximately(pts[0].W, 0.05f);
            if (!ok) Debug.LogError("FAIL одна точка: тычок кистью обязан остаться как был");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
