using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class StrokeOpsSelfTests : MonoBehaviour
    {
        static List<StrokePoint> Line(int n, float step)
        {
            var pts = new List<StrokePoint>(n);
            for (int i = 0; i < n; i++) pts.Add(new StrokePoint(i * step, 0.5f, 0.02f));
            return pts;
        }

        /// <summary>Дрожащая прямая. Разброс НЕ ЗНАКОПЕРЕМЕННЫЙ нарочно: у идеального зигзага
        /// хорда через чётное число шагов схлопывается ровно в горизонталь, и проверка проходила
        /// бы из-за симметрии фикстуры, а не потому, что правило работает. Период 7 не делится ни
        /// на длину плеча, ни на окно сглаживания.</summary>
        static readonly float[] Jitter = { 1f, -0.7f, 0.9f, -1f, 0.5f, -0.9f, 0.8f };

        static List<StrokePoint> Tremor(int n, float step, float amplitude)
        {
            var pts = new List<StrokePoint>(n);
            for (int i = 0; i < n; i++)
                pts.Add(new StrokePoint(i * step, 0.5f + amplitude * Jitter[i % Jitter.Length], 0.02f));
            return pts;
        }

        [ContextMenu("Self-Test: Чистка — двойники выбрасываются")]
        public void SelfTestDuplicatesDropped()
        {
            var pts = new List<StrokePoint>();
            for (int i = 0; i < 10; i++) pts.Add(new StrokePoint(0.5f, 0.5f, 0.02f));
            var cleaned = StrokeOps.Dedupe(pts, StrokeOps.MinPointDistance);
            bool ok = cleaned.Count == 1;
            if (!ok) Debug.LogError($"FAIL двойники: осталось {cleaned.Count}, ожидалась 1");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — концы не двигаются")]
        public void SelfTestEndsAreFixed()
        {
            // Иначе линия отползает от места, где ДМ начал и где закончил.
            var pts = Line(9, 0.1f);
            pts[4] = new StrokePoint(pts[4].X, 0.8f, 0.02f);   // горб посередине
            var s = StrokeOps.Smooth(pts, 1f, 5, StrokeOps.CornerAngleDegrees);
            bool ok = Mathf.Approximately(s[0].X, pts[0].X) && Mathf.Approximately(s[0].Y, pts[0].Y)
                   && Mathf.Approximately(s[s.Count - 1].X, pts[pts.Count - 1].X)
                   && Mathf.Approximately(s[s.Count - 1].Y, pts[pts.Count - 1].Y);
            if (!ok) Debug.LogError("FAIL концы: первая или последняя точка сдвинулась");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — угол не скругляется")]
        public void SelfTestCornerNeighboursDoNotMove()
        {
            // ЭТА ПРОВЕРКА СМОТРИТ НА СОСЕДЕЙ ВЕРШИНЫ, А НЕ НА НЕЁ САМУ, и это принципиально.
            // Слабая реализация — «закрепить вершину, но пустить окно через неё» — оставит вершину
            // на месте и всё равно утянет соседей к диагонали, то есть скруглит угол. Правило:
            // окно НЕ ПЕРЕСЕКАЕТ угол, цепочка разрезается по углам.
            var pts = new List<StrokePoint>();
            for (int i = 0; i < 5; i++) pts.Add(new StrokePoint(i * 0.1f, 0.5f, 0.02f));   // → вправо
            for (int i = 1; i <= 4; i++) pts.Add(new StrokePoint(0.4f, 0.5f + i * 0.1f, 0.02f)); // ↑ вверх

            var s = StrokeOps.Smooth(pts, 1f, 5, StrokeOps.CornerAngleDegrees);

            // Соседи вершины (индексы 3 и 5) обязаны остаться на своих осях: сосед слева на прежней
            // высоте, сосед сверху на прежней вертикали.
            bool ok = Mathf.Abs(s[3].Y - 0.5f) < 0.001f && Mathf.Abs(s[5].X - 0.4f) < 0.001f;
            if (!ok) Debug.LogError($"FAIL угол скруглился: сосед слева Y={s[3].Y} (ждём 0.5), сосед сверху X={s[5].X} (ждём 0.4)");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — дрожь на прямой убирается")]
        public void SelfTestTremorIsReduced()
        {
            // Правило обязано что-то ДЕЛАТЬ, иначе предыдущие две проверки проходят и на пустышке.
            // УБИВАЕТ МУТАНТА «плечо в одну точку» (CornerArmMinPoints = 1): на редкой цепочке
            // одного шага хватает по длине, мерка вырождается в соседний отрезок, каждая точка
            // дрожи объявляется углом, цепочка режется в труху и сглаживание даёт 0.78 вместо 0.18.
            var pts = Tremor(21, 0.08f, 0.02f);

            var s = StrokeOps.Smooth(pts, 1f, 5, StrokeOps.CornerAngleDegrees);

            float before = 0f, after = 0f;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                before += Mathf.Abs(pts[i].Y - 0.5f);
                after += Mathf.Abs(s[i].Y - 0.5f);
            }
            bool ok = after < before * 0.6f;
            if (!ok) Debug.LogError($"FAIL дрожь: было {before}, стало {after} — сглаживание почти ничего не сделало");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — густая дрожь не считается углами")]
        public void SelfTestDenseTremorHasNoCorners()
        {
            // ЭТО ПРОВЕРКА МАСШТАБА, и без неё правило держится на редкости фикстуры. Точки идут
            // тем гуще, чем МЕДЛЕННЕЕ ведёт рука, а амплитуда дрожи от скорости не зависит: здесь
            // шаг 0.0021 (чуть выше порога Dedupe = 0.002) при дрожи ±0.0025, то есть дрожь ШИРЕ
            // шага. Плечо, отмеренное в точках, оказалось бы короче самой дрожи.
            //
            // УБИВАЕТ МУТАНТА «нет пола по длине» (CornerArmMinLength = 0): он даёт 42 угла.
            // Отношением дрожи его НЕ УБИТЬ — там выходит 0.56 при пороге 0.6, то есть мутант
            // проползает; ловится только прямым утверждением про углы.
            var pts = Tremor(101, 0.0021f, 0.0025f);
            var corners = StrokeOps.CornerIndices(pts, StrokeOps.CornerAngleDegrees);
            bool ok = corners.Count == 0;
            if (!ok) Debug.LogError($"FAIL густая дрожь: найдено {corners.Count} «углов» (первый — {corners[0]}), ожидалось 0");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — нулевая сила ничего не меняет")]
        public void SelfTestZeroStrengthIsIdentity()
        {
            // Требование ДМ «не переборщить» опирается на возможность выключить сглаживание одним
            // числом. Если ноль не вырождается в тождество, выключить его нечем.
            var pts = Line(7, 0.1f);
            pts[3] = new StrokePoint(pts[3].X, 0.9f, 0.02f);
            var s = StrokeOps.Smooth(pts, 0f, 5, StrokeOps.CornerAngleDegrees);
            bool ok = Mathf.Approximately(s[3].Y, 0.9f);
            if (!ok) Debug.LogError($"FAIL нулевая сила: точка сдвинулась в {s[3].Y}, ожидалась 0.9");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — прореживание не меняет форму")]
        public void SelfTestSimplifyKeepsShape()
        {
            var pts = Line(50, 0.02f);
            var s = StrokeOps.Simplify(pts, StrokeOps.SimplifyEpsilon);
            bool ok = s.Count < pts.Count && s.Count >= 2
                   && Mathf.Approximately(s[0].X, pts[0].X)
                   && Mathf.Approximately(s[s.Count - 1].X, pts[pts.Count - 1].X);
            if (!ok) Debug.LogError($"FAIL прореживание: {pts.Count} → {s.Count}, концы {s[0].X}…{s[s.Count-1].X}");
            Done(ok);
        }

        [ContextMenu("Self-Test: Чистка — прореживание не срезает угол")]
        public void SelfTestSimplifyKeepsCorner()
        {
            var pts = new List<StrokePoint>();
            for (int i = 0; i < 10; i++) pts.Add(new StrokePoint(i * 0.05f, 0.5f, 0.02f));
            for (int i = 1; i <= 9; i++) pts.Add(new StrokePoint(0.45f, 0.5f + i * 0.05f, 0.02f));
            var s = StrokeOps.Simplify(pts, StrokeOps.SimplifyEpsilon);

            bool hasCorner = false;
            foreach (var p in s)
                if (Mathf.Abs(p.X - 0.45f) < 0.001f && Mathf.Abs(p.Y - 0.5f) < 0.001f) hasCorner = true;
            if (!hasCorner) Debug.LogError("FAIL прореживание срезало вершину угла");
            Done(hasCorner);
        }

        [ContextMenu("Self-Test: Чистка — мазок из одной точки переживает всё")]
        public void SelfTestSinglePointSurvivesCleanup()
        {
            var pts = new List<StrokePoint> { new StrokePoint(0.5f, 0.5f, 0.05f) };
            var s = StrokeOps.Cleanup(pts);
            bool ok = s.Count == 1 && Mathf.Approximately(s[0].W, 0.05f);
            if (!ok) Debug.LogError($"FAIL тычок кистью: осталось {s.Count} точек");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
