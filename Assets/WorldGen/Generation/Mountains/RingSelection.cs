using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>Принятое кольцо: линия уровня плюс сам уровень (он же — полуширина массы под ней).</summary>
    public sealed class RingAxis
    {
        public IsoContour Contour;
        public float Level;
    }

    /// <summary>
    /// §4 «Оси-кольца и почему шаг равен 2R» и §5 «Когда кольцо вырождается».
    ///
    /// Идея покрытия: гора — это круг радиуса R, поставленный на ось. Если ось идёт по линии уровня
    /// {D = L}, круги радиуса R, едущие по ней, накрывают ровно полосу L−R ≤ D ≤ L+R, шириной 2R по
    /// значению D. Значит, чтобы полосы стыковались встык, без щелей и без перехлёста, уровни надо
    /// брать с шагом 2R: L_k = (2k+1)·R. Первое кольцо идёт на расстоянии R от края мазка и
    /// закрывает всё от края до 2R, второе на 3R закрывает от 2R до 4R, и так вглубь. Вложенность
    /// получается сама — вырезать покрытое и строить оси заново по остатку не нужно.
    ///
    /// Но буквальное применение правила ломается там, где масса лишь чуть шире 2R: изолиния уровня R
    /// превращается в узкую петлю, огибающую пятно с двух сторон. Формально это кольцо, по существу
    /// — дважды пройденная осевая линия, и хуже того: такая петля разрезает сквозную ось надвое, и
    /// цепь гор рвётся. Поэтому кольцо принимается, только если ПОД НИМ ЕСТЬ ГЛУБИНА: не меньше
    /// L + 0.6R. Отвергнутый кусок заведомо не толще 1.6R — ровно того предела, до которого
    /// раздувается скелетная ось (§9), поэтому он ничего не теряет, уходя к скелету.
    ///
    /// Глубина меряется МЕСТНО, и это правка против PDF и прототипа, сделанная по решению ДМ.
    /// Буквальное правило берёт максимум поля по всей компоненте связности {D ≥ L} — и на развилке
    /// протекает: в месте схождения рукавов вписанная окружность больше самих рукавов, одного этого
    /// утолщения хватает, чтобы кольцо приняли ЦЕЛИКОМ, после чего оно уходит вдоль тонких рукавов
    /// той самой вырожденной петлёй, ради запрета которой §5 и написан. Рукав получал две цепи гор
    /// по бокам вместо одной посередине.
    ///
    /// Местная глубина — максимум поля в окне радиуса R вокруг точки контура, по своей компоненте.
    /// Окно именно R, и это не на глаз: поле расстояний липшицево, поэтому ячейка с D ≥ L + 0.6R
    /// лежит не ближе 0.6R от линии уровня L — то есть если глубина есть, окно R её заведомо
    /// поймает; а под локально толстой массой D дорастает до ≈ L + R, и порог берётся с запасом.
    /// Чужая компонента из окна исключается — формулировка «глубина под ЭТИМ кольцом» требует
    /// именно этого. Честная оговорка: ни одна фикстура стенда мутанта «считать по любой
    /// компоненте» не убивает, и это не небрежность, а следствие той же липшицевости. Чтобы
    /// ограничение сработало, нужна ячейка глубиной ≥ 1.6R не дальше R от точки контура, а между
    /// ними — перемычка, где поле проседает ниже L. Спуск до перемычки и подъём до 1.6R вместе
    /// требуют почти всего окна, поэтому такая расстановка возможна лишь впритык, на пределе.
    /// Ограничение оставлено как более правильная формулировка, а не как замеренный выигрыш.
    ///
    /// Кольцо после замера режется на принятые куски: замкнутая петля над развилкой становится
    /// дугой над узлом, рукава остаются скелету.
    /// </summary>
    public static class RingSelection
    {
        /// <summary>Запас глубины, ниже которого кольцо отвергается. Связан с потолком ширины
        /// скелетной оси: 1 + 0.6 = 1.6R.</summary>
        public const float DepthMargin = 0.6f;

        /// <summary>Радиус окна замера местной глубины, в радиусах горы.</summary>
        public const float LocalWindow = 1f;

        /// <summary>Принятый кусок короче этого (в радиусах) — огрызок: одна гора, приставленная
        /// сбоку к цепи со скелета. Пусть лучше весь кусок ведёт скелет.</summary>
        public const float MinRunFactor = 1.5f;

        /// <summary>Отвергнутый провал короче этого (в радиусах) зарастает: на ровном кольце замер
        /// у порога дрожит, и без этого целое кольцо рассыпалось бы на дуги по швам дрожания.</summary>
        public const float GapHealFactor = 1f;

        /// <summary>Предохранитель от бесконечного цикла на испорченном поле.</summary>
        const int MaxLevels = 64;

        /// <summary>Уровни L_k = (2k+1)·R, пока они не глубже самой массы.</summary>
        public static List<float> Levels(float maxDistance, float radiusCells)
        {
            var levels = new List<float>();
            if (radiusCells <= 0f) return levels;
            for (float level = radiusCells; level <= maxDistance && levels.Count < MaxLevels; level += 2f * radiusCells)
                levels.Add(level);
            return levels;
        }

        /// <summary>Кольца — и куски колец — прошедшие отбор по глубине, от края вглубь.</summary>
        public static List<RingAxis> Select(float[] field, int w, int h, float radiusCells)
        {
            var accepted = new List<RingAxis>();
            float maxDistance = DistanceField.Max(field);

            foreach (float level in Levels(maxDistance, radiusCells))
            {
                var labels = new int[w * h];
                for (int i = 0; i < labels.Length; i++) labels[i] = -1;
                LabelComponents(field, w, h, level, labels);

                foreach (var contour in IsoContours.Trace(field, w, h, level))
                {
                    if (contour.Points.Count < 4) continue;
                    int id = LabelAt(labels, w, h, contour.Points[0].X, contour.Points[0].Y);
                    if (id < 0) continue;
                    Cut(contour, field, labels, id, w, h, level, radiusCells, accepted);
                }
            }
            return accepted;
        }

        /// <summary>
        /// Меряет глубину вдоль контура и складывает в список принятые куски. Целиком принятое
        /// кольцо кладётся как есть — замкнутость важна, ось по нему пойдёт без шва.
        /// </summary>
        static void Cut(IsoContour contour, float[] field, int[] labels, int id, int w, int h,
                        float level, float radiusCells, List<RingAxis> accepted)
        {
            var pts = contour.Points;
            bool closed = contour.Closed;
            // У замкнутого контура последняя точка повторяет первую — считаем её один раз.
            int n = closed ? pts.Count - 1 : pts.Count;
            if (n < 4) return;

            var ok = Classify(pts, n, field, labels, id, w, h,
                              LocalWindow * radiusCells, level + DepthMargin * radiusCells);

            bool all = true, none = true;
            for (int i = 0; i < n; i++) { if (ok[i]) none = false; else all = false; }
            if (none) return;
            if (all)
            {
                accepted.Add(new RingAxis { Contour = contour, Level = level });
                return;
            }

            // Обход выкладываем в прямую линию. У замкнутого начинаем с начала принятого куска —
            // иначе кусок, лежащий на шве, развалился бы надвое на ровном месте.
            int start = 0;
            if (closed)
                for (int i = 0; i < n; i++)
                    if (ok[i] && !ok[(i + n - 1) % n]) { start = i; break; }

            var order = new int[n];
            var flag = new bool[n];
            for (int k = 0; k < n; k++)
            {
                order[k] = closed ? (start + k) % n : k;
                flag[k] = ok[order[k]];
            }

            for (int k = 0; k < n; )
            {
                if (flag[k]) { k++; continue; }
                int j = k;
                while (j < n && !flag[j]) j++;
                // У замкнутого «конец» смыкается с началом, поэтому внутренними считаются все провалы.
                bool interior = closed || (k > 0 && j < n);
                if (interior && ArcLength(pts, order, k, j) < GapHealFactor * radiusCells)
                    for (int t = k; t < j; t++) flag[t] = true;
                k = j;
            }

            for (int k = 0; k < n; )
            {
                if (!flag[k]) { k++; continue; }
                int j = k;
                while (j < n && flag[j]) j++;
                if (ArcLength(pts, order, k, j) >= MinRunFactor * radiusCells && j - k >= 4)
                {
                    var piece = new IsoContour { Closed = false };
                    for (int t = k; t < j; t++) piece.Points.Add(pts[order[t]]);
                    accepted.Add(new RingAxis { Contour = piece, Level = level });
                }
                k = j;
            }
        }

        /// <summary>
        /// Для каждой точки контура: есть ли под ней глубина. Замер стоит окна на точку, а меняется
        /// вдоль контура плавно (поле липшицево: шаг в клетку меняет глубину не больше чем на клетку),
        /// поэтому меряем через каждую четверть окна, а ответ держим до следующего замера. Границу
        /// принятого это сдвигает не больше чем на четверть окна — меньше, чем зарастает провал.
        /// </summary>
        static bool[] Classify(List<Vector2> pts, int n, float[] field, int[] labels, int id,
                               int w, int h, float window, float need)
        {
            var ok = new bool[n];
            float step = Math.Max(1f, window * 0.25f);
            float travelled = float.MaxValue;
            bool value = false;

            for (int i = 0; i < n; i++)
            {
                if (i > 0) travelled += Vector2.Distance(pts[i - 1], pts[i]);
                if (travelled >= step)
                {
                    value = LocalDepth(field, labels, id, w, h, pts[i], window) >= need;
                    travelled = 0f;
                }
                ok[i] = value;
            }
            return ok;
        }

        /// <summary>Максимум поля в окне вокруг точки, по своей компоненте.</summary>
        static float LocalDepth(float[] field, int[] labels, int id, int w, int h, Vector2 p, float window)
        {
            int x0 = Math.Max(0, (int)Math.Floor(p.X - window));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling(p.X + window));
            int y0 = Math.Max(0, (int)Math.Floor(p.Y - window));
            int y1 = Math.Min(h - 1, (int)Math.Ceiling(p.Y + window));

            float best = 0f, limit = window * window;
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - p.Y;
                for (int x = x0; x <= x1; x++)
                {
                    int k = y * w + x;
                    if (labels[k] != id) continue;
                    float dx = x - p.X;
                    if (dx * dx + dy * dy > limit) continue;
                    if (field[k] > best) best = field[k];
                }
            }
            return best;
        }

        /// <summary>Длина куска обхода [from, to) вдоль контура, в ячейках.</summary>
        static float ArcLength(List<Vector2> pts, int[] order, int from, int to)
        {
            float sum = 0f;
            for (int k = from; k + 1 < to; k++)
                sum += Vector2.Distance(pts[order[k]], pts[order[k + 1]]);
            return sum;
        }

        /// <summary>Размечает компоненты связности множества {D ≥ level}. Обход по четырём соседям:
        /// по восьми две массы, соприкасающиеся только углом, считались бы одной, и мелкая заусеница
        /// делилась бы глубиной соседа.</summary>
        static void LabelComponents(float[] field, int w, int h, float level, int[] labels)
        {
            var stack = new Stack<int>();
            int next = 0;

            for (int start = 0; start < field.Length; start++)
            {
                if (field[start] < level || labels[start] >= 0) continue;

                int id = next++;
                labels[start] = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % w, y = i / w;
                    Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
                }

                void Push(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    int k = ny * w + nx;
                    if (field[k] < level || labels[k] >= 0) return;
                    labels[k] = id;
                    stack.Push(k);
                }
            }
        }

        /// <summary>Метка компоненты рядом с точкой контура. Сама точка лежит НА линии уровня, то
        /// есть между ячейками «внутри» и «снаружи», поэтому смотрим все четыре ячейки её квадрата.</summary>
        static int LabelAt(int[] labels, int w, int h, float gx, float gy)
        {
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            for (int dy = 0; dy <= 1; dy++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    int label = labels[y * w + x];
                    if (label >= 0) return label;
                }
            }
            return -1;
        }
    }
}
