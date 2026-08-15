using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// ВНЕШНИЙ КОНТУР горы — ОДНА ломаная, а не набор штрихов.
    ///
    /// Прежняя редакция искала границу «по лучам»: на каждом луче из середины звена бралcя ярус с
    /// наибольшим выносом. Точки получались правильные, но соединять их подряд было нельзя: у конуса
    /// максимум сидит на конце (либо подошва, либо вершина), у соседних лучей он перескакивает, и
    /// ломаная выходила клубком — стенд насчитал 68 самопересечений из 115. Оттуда пошло всё
    /// остальное: линия короткими штрихами, отдельная обводка склона на скачке, а у ДМ на карте —
    /// рваный полупрозрачный контур и чёрные клинья у вершин.
    ///
    /// Здесь граница считается ПО X, и этим все те беды снимаются разом.
    ///
    /// Ярус r — та же подошва, стянутая к середине звена в r раз и поднятая на H·lift(r). Значит над
    /// точкой x верхняя граница яруса равна r·U(x/r) + H·lift(r), где U — верхняя огибающая самой
    /// подошвы. Верх объединения — максимум этого по r:
    ///
    ///     yВерх(x) = max_r [ r·U(x/r) + H·lift(r) ]
    ///
    /// Это ФУНКЦИЯ от x — по одному значению на каждый x. Ломаная, проведённая по ней, самопересечься
    /// не может ПО ПОСТРОЕНИЮ, а не потому, что мы её починили: у функции не бывает двух значений в
    /// одной точке. Обход тоже задан сам собой — слева направо.
    ///
    /// Что получается на картинке. У краёв (|x| у самого предела подошвы) ни один ярус, кроме
    /// подошвы, туда не достаёт, поэтому максимум сидит на r = 1 и толщина линии там нулевая: НИЗА У
    /// КОНТУРА НЕТ, он обрывается сам, ровно как на образце ДМ. К вершине r падает, толщина растёт.
    /// Выходит та самая «галочка» — от подошвы вверх по склону, через вершину и вниз по другому
    /// склону, ОДНОЙ НЕПРЕРЫВНОЙ ЛИНИЕЙ.
    ///
    /// Нижняя дуга подошвы в контур не входит вовсе: она ниже всех ярусов и не рисуется. Это не
    /// потеря — «низа нет» и есть манера.
    /// </summary>
    public static class MountainOutline
    {
        /// <summary>
        /// Разрешение огибающей подошвы. Подошва — замкнутая ломаная в сотню-полторы точек; её верх
        /// мы кладём в таблицу по x один раз, а потом дёргаем оттуда для каждого яруса. Иначе на
        /// каждый ярус пришлось бы заново обходить всю подошву.
        ///
        /// Полтысячи, а не сотня: у краёв подошва почти отвесна, и прямая между двумя редкими
        /// отсчётами проходит заметно ниже её самой. Замер на стенде: при 128 отсчётах ярусы
        /// вылезали над контуром на 1.4 единицы из 20 — это уже видно глазом как «тело выше линии».
        /// Срез падает как квадрат шага, поэтому вчетверо частая сетка убирает его в шестнадцать раз.
        /// </summary>
        public const int FootSamples = 256;

        /// <summary>
        /// Таблица огибающей — СВОЯ У КАЖДОГО ПОТОКА и переиспользуемая.
        ///
        /// Полтысячи чисел на гору — это два килобайта; при пятистах горах на пересчёт вышел бы
        /// мегабайт мусора за каждое движение кисти. Считает слой и в фоне (MountainLayer), и на
        /// главном потоке (шип, стенд), поэтому буфер потоковый, а не общий.
        /// </summary>
        [ThreadStatic] static float[] footBuffer;

        /// <summary>Сколько значений r перебирается при поиске верха.</summary>
        public const int LevelSamples = 28;

        /// <summary>Сколько точек в самом контуре. Шаг выходит около R/8 при обычной горе — мельче
        /// не нужно: линия толщиной около R/10 всё равно скрадывает угол.</summary>
        public const int Samples = 56;

        /// <summary>
        /// Контур горы: точки слева направо и ярус r, на котором в каждой из них достигнут верх.
        /// Из r берётся толщина линии (MountainInk.Taper): 1 — подошва, толщина ноль.
        ///
        /// Списки очищаются и заполняются заново — их заводят ОДИН раз на весь массив, а не на гору:
        /// гор сотни, и свой список на каждую — это сотни выбросов в мусор за один пересчёт.
        /// </summary>
        public static void Build(MountainShape shape, LiftSamples profile,
                                 List<Vector2> points, List<float> radii)
        {
            points.Clear();
            radii.Clear();
            if (shape?.Base == null || shape.Base.Length < 3 || profile == null) return;

            Vector2 c = shape.Centre;
            var foot = shape.Base;
            int n = foot.Length;

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                float x = foot[i].X - c.X;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
            float span = maxX - minX;
            if (span <= 1e-4f) return;

            var upper = FootUpper(foot, c, minX, span);

            // Выборка РАВНОМЕРНАЯ по x, и только. Первая редакция клала сюда ещё и вершину каждого
            // яруса — «чтобы сетка не срезала макушку в площадку». Мутация показала, что эти точки
            // ничего не держат: выбрось их — ни одна проверка не падает, потому что макушку всё
            // равно находит подразбиение (Refine), причём в том самом месте, где излом. Настройка,
            // которую нельзя провалить, — это не страховка, а лишний код.
            float height = shape.Height;
            for (int k = 0; k <= Samples; k++)
            {
                float x = minX + span * k / Samples;
                float bestY = Best(upper, minX, maxX, span, x, height, profile, out float bestR);
                if (float.IsNegativeInfinity(bestY)) continue;

                points.Add(new Vector2(c.X + x, c.Y + bestY));
                radii.Add(bestR);
            }

            Refine(points, radii, upper, c, minX, maxX, span, height, profile);
        }

        /// <summary>
        /// ПОДЪЁМ каждой точки контура: 0 у самых низких точек, 1 на вершине.
        ///
        /// Именно от него берётся толщина линии, а НЕ от яруса r. Разница видна на карте сразу.
        /// Ярус — величина косая: у наклонной гряды подошва повёрнута, и с одной стороны горы верх
        /// достигается низкими ярусами (линия тонкая), а с другой — высокими (линия жирная). Гора
        /// выходит однобокой, вся гряда клонится, и ДМ назвал это «криво».
        ///
        /// Подъём же симметричен по самому смыслу: у вершины — полная толщина, к обоим концам сходит
        /// на нет. Это ровно то, что на образце: «толстая линия гребня, жирная у вершины и сходящая
        /// на нет книзу; низа нет вовсе».
        ///
        /// Нормировка — по САМОЙ горе (её собственный размах высот), поэтому низкая гора у края
        /// массива тоже обведена как гора, а не как ниточка.
        /// </summary>
        public static void Heights(List<Vector2> points, List<float> rise)
        {
            rise.Clear();
            if (points.Count == 0) return;

            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Y < lo) lo = points[i].Y;
                if (points[i].Y > hi) hi = points[i].Y;
            }
            float span = hi - lo;
            for (int i = 0; i < points.Count; i++)
                rise.Add(span <= 1e-5f ? 0f : (points[i].Y - lo) / span);
        }

        /// <summary>Насколько хорда между соседними точками контура вправе отойти от настоящей
        /// огибающей, в мировых единицах. То же число и с тем же смыслом, что допуск разбиения тела
        /// (MoundBuilder.Levels): «на глаз незаметно».</summary>
        public const float Tolerance = 0.2f;

        /// <summary>
        /// Подразбиение там, где хорда срезает угол.
        ///
        /// Равномерная сетка по x хороша везде, кроме ВЕРШИНЫ: там огибающая ломается, и прямая
        /// между двумя соседними отсчётами проходит заметно ниже настоящей. Замер: у шпиля срез
        /// доходил до 1.5 единиц при полуширине 20 — гора выходила подстриженной.
        ///
        /// Лечится тем же приёмом, что и разбиение тела: делим пополам, пока отклонение не станет
        /// меньше допуска. Работа идёт только там, где она нужна, — на пологих склонах не
        /// добавляется ни одной точки.
        /// </summary>
        static void Refine(List<Vector2> points, List<float> radii, float[] upper, Vector2 c,
                           float minX, float maxX, float span, float height, LiftSamples profile)
        {
            const int MaxRounds = 5;
            for (int round = 0; round < MaxRounds; round++)
            {
                int added = 0;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    float x = 0.5f * (points[i].X + points[i + 1].X) - c.X;
                    float chord = 0.5f * (points[i].Y + points[i + 1].Y) - c.Y;
                    float y = Best(upper, minX, maxX, span, x, height, profile, out float r);
                    if (float.IsNegativeInfinity(y) || y - chord <= Tolerance) continue;

                    points.Insert(i + 1, new Vector2(c.X + x, c.Y + y));
                    radii.Insert(i + 1, r);
                    i++;                      // вставленную точку на этом круге не трогаем
                    added++;
                }
                if (added == 0) return;
            }
        }

        /// <summary>
        /// Ярус номер j из LevelSamples: от подошвы (1) до вершины (ApexRadius).
        ///
        /// Шаг НЕ равномерный, а сгущённый к подошве. Причина — купол: у него lift = √(1−r²), и
        /// возле r = 1 кривая почти отвесна, тогда как у вершины она полога. Равномерная сетка
        /// тратит отсчёты там, где ничего не происходит, и промахивается там, где всё.
        /// </summary>
        public static float LevelR(int j) => LevelR(j, MountainProfile.ApexRadius);

        /// <summary>Ярус номер j на промежутке от подошвы (1) до заданного нижнего края.</summary>
        static float LevelR(int j, float lowest)
        {
            float t = j / (float)(LevelSamples - 1);
            return 1f - (1f - lowest) * t * t;
        }

        /// <summary>Самый мелкий ярус, который ещё достаёт до точки x. Ярус r — подошва, стянутая в
        /// r раз, поэтому по x он занимает ровно долю r от неё.</summary>
        static float Edge(float minX, float maxX, float x)
        {
            float e = x >= 0f ? (maxX > 1e-6f ? x / maxX : 1f)
                              : (minX < -1e-6f ? x / minX : 1f);
            if (e < MountainProfile.ApexRadius) e = MountainProfile.ApexRadius;
            return e > 1f ? 1f : e;
        }

        /// <summary>
        /// Верх ОБЪЕДИНЕНИЯ над точкой x и ярус, на котором он достигнут. −∞ — горы здесь нет.
        ///
        /// Перебор по ярусам плюс уточнение параболой по лучшей тройке. Уточнение не роскошь: у
        /// КУПОЛА lift = √(1−r²), у самой подошвы кривая почти отвесна, и между двумя соседними
        /// значениями r прячется настоящий максимум — замер показал промах до 8 единиц из 20.
        /// Одно лишнее вычисление дешевле вдвое более частой сетки.
        /// </summary>
        static float Best(float[] upper, float minX, float maxX, float span, float x,
                          float height, LiftSamples profile, out float bestR)
        {
            // Мельче этого яруса подошва до точки x просто не достаёт: ярус r занимает по x долю r
            // от подошвы. Перебор ведём ровно по накрывающему промежутку [край, 1], а не по всему
            // [вершина, 1], — иначе у самой макушки настоящий максимум оказывается МЕЖДУ узлами
            // сетки и теряется. Замер стенда: у шпиля тело вылезало над линией на 2.2 из 20 именно
            // так, и ни частая сетка по подошве, ни уточнение параболой этого не убирали.
            float edge = Edge(minX, maxX, x);

            float bestY = float.NegativeInfinity;
            int bestJ = -1;
            bestR = 1f;

            for (int j = 0; j < LevelSamples; j++)
            {
                float r = LevelR(j, edge);
                float y = Top(upper, minX, span, x, r, height, profile);
                if (float.IsNegativeInfinity(y)) continue;          // этот ярус сюда не достаёт
                if (y > bestY) { bestY = y; bestR = r; bestJ = j; }
            }
            if (bestJ <= 0 || bestJ + 1 >= LevelSamples) return bestY;

            float r0 = LevelR(bestJ - 1, edge), r1 = LevelR(bestJ, edge), r2 = LevelR(bestJ + 1, edge);
            float y0 = Top(upper, minX, span, x, r0, height, profile);
            float y2 = Top(upper, minX, span, x, r2, height, profile);
            if (float.IsNegativeInfinity(y0) || float.IsNegativeInfinity(y2)) return bestY;

            float rv = Vertex(r0, y0, r1, bestY, r2, y2);
            if (float.IsNaN(rv)) return bestY;

            float yv = Top(upper, minX, span, x, rv, height, profile);
            if (yv > bestY) { bestY = yv; bestR = rv; }
            return bestY;
        }

        /// <summary>Верх яруса r над точкой x. −∞ — этот ярус сюда не достаёт.</summary>
        static float Top(float[] upper, float minX, float span, float x, float r,
                         float height, LiftSamples profile)
        {
            float u = Sample(upper, minX, span, x / r);
            if (float.IsNegativeInfinity(u)) return float.NegativeInfinity;
            return r * u + height * profile.LiftAt(r);
        }

        /// <summary>Вершина параболы через три точки. NaN — парабола вырождена или вершина вне
        /// тройки, и уточнять нечего.</summary>
        static float Vertex(float x0, float y0, float x1, float y1, float x2, float y2)
        {
            float d1 = (x1 - x0) * (y1 - y2), d2 = (x1 - x2) * (y1 - y0);
            float den = d1 - d2;
            if (Math.Abs(den) < 1e-12f) return float.NaN;
            float v = x1 - 0.5f * ((x1 - x0) * d1 - (x1 - x2) * d2) / den;
            float lo = Math.Min(x0, x2), hi = Math.Max(x0, x2);
            return v < lo || v > hi ? float.NaN : v;
        }

        /// <summary>
        /// Верхняя огибающая подошвы: на каждый x — самая высокая точка замкнутой ломаной.
        ///
        /// Считается обходом рёбер, а не перебором точек: между двумя соседними точками подошвы
        /// умещается несколько отсчётов сетки, и без интерполяции по ребру огибающая вышла бы
        /// дырявой. Клетки, куда не попало ни одно ребро, остаются −∞ и честно значат «подошвы здесь
        /// нет».
        /// </summary>
        static float[] FootUpper(Vector2[] foot, Vector2 c, float minX, float span)
        {
            var upper = footBuffer ?? (footBuffer = new float[FootSamples + 1]);
            for (int k = 0; k <= FootSamples; k++) upper[k] = float.NegativeInfinity;

            int n = foot.Length;
            float step = span / FootSamples;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = foot[i] - c;
                Vector2 b = foot[(i + 1) % n] - c;
                if (b.X < a.X) { var t = a; a = b; b = t; }

                int k0 = (int)Math.Ceiling((a.X - minX) / step);
                int k1 = (int)Math.Floor((b.X - minX) / step);
                if (k0 < 0) k0 = 0;
                if (k1 > FootSamples) k1 = FootSamples;

                float dx = b.X - a.X;
                for (int k = k0; k <= k1; k++)
                {
                    float x = minX + step * k;
                    float y = dx <= 1e-9f ? Math.Max(a.Y, b.Y) : a.Y + (b.Y - a.Y) * (x - a.X) / dx;
                    if (y > upper[k]) upper[k] = y;
                }
            }

            // САМИ ТОЧКИ ПОДОШВЫ — в обе соседние клетки. Это не мелочь, а условие правильности.
            //
            // Рёбра дают значения только В УЗЛАХ сетки, а точка подошвы стоит между ними. Там, где
            // подошва поворачивает (а у её левого и правого краёв она поворачивает круто), точка
            // оказывается выше обоих узлов, прямая между ними проходит под ней, и посчитанная
            // огибающая выходит НИЖЕ настоящей. На карте это значит, что тело горы вылезает над
            // линией: замер стенда — вылет 1.4 из 20 у самой макушки, и никакая сетка его не
            // убирает, потому что дело в изломе, а не в частоте.
            //
            // Подпирая обе соседние клетки, мы гарантируем «таблица не ниже подошвы ни в одной её
            // точке» — а из этого уже следует, что контур не ниже ни одного яруса. Огибающая при
            // этом чуть завышена, но завышение безопасно: линия закрывает тело, а не наоборот.
            for (int i = 0; i < n; i++)
            {
                Vector2 p = foot[i] - c;
                float t = (p.X - minX) / step;
                int kf = (int)Math.Floor(t), kc = (int)Math.Ceiling(t);
                if (kf < 0) kf = 0; else if (kf > FootSamples) kf = FootSamples;
                if (kc < 0) kc = 0; else if (kc > FootSamples) kc = FootSamples;
                if (p.Y > upper[kf]) upper[kf] = p.Y;
                if (p.Y > upper[kc]) upper[kc] = p.Y;
            }

            // Крайние отсчёты может не накрыть ни одно ребро (точка подошвы стоит чуть внутри
            // клетки) — тогда подпираем их соседом, иначе у контура откусан самый край.
            if (float.IsNegativeInfinity(upper[0]))
                for (int k = 1; k <= FootSamples; k++)
                    if (!float.IsNegativeInfinity(upper[k])) { upper[0] = upper[k]; break; }
            if (float.IsNegativeInfinity(upper[FootSamples]))
                for (int k = FootSamples - 1; k >= 0; k--)
                    if (!float.IsNegativeInfinity(upper[k])) { upper[FootSamples] = upper[k]; break; }

            return upper;
        }

        /// <summary>Значение огибающей подошвы в произвольной точке x. Вне подошвы — −∞.</summary>
        static float Sample(float[] upper, float minX, float span, float x)
        {
            float t = (x - minX) / span * FootSamples;
            // Тысячная доля клетки допуска на каждом краю — ровно на округление, не больше. Без
            // допуска правый край терялся: там t выходит ровно FootSamples, и одного бита вверх
            // хватало, чтобы отсчёт объявили «вне подошвы» — контур обрывался за шаг до края, а на
            // самом краю оставался ярус r < 1, то есть линия ненулевой толщины. Низ горы обводился
            // ровно там, где он обводиться не должен. Допуск шире тоже нельзя: он пускает за край
            // соседние ярусы, и край снова перестаёт быть подошвой.
            if (t < -1e-3f || t > FootSamples + 1e-3f) return float.NegativeInfinity;
            if (t < 0f) t = 0f;
            if (t > FootSamples) t = FootSamples;

            int k = (int)t;
            if (k >= FootSamples) return upper[FootSamples];
            float f = t - k;
            float a = upper[k], b = upper[k + 1];
            if (float.IsNegativeInfinity(a)) return b;
            if (float.IsNegativeInfinity(b)) return a;
            return a + (b - a) * f;
        }

    }
}
