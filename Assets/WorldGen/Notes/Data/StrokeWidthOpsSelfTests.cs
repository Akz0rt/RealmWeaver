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
            float once = StrokeWidthOps.Smooth(1.3f, 0.7f);
            bool ok = once > 0.7f && once < 1.3f;
            if (!ok) Debug.LogError($"FAIL сглаживание множителя: {once} — прыжок сразу в цель или отсутствие движения");
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
