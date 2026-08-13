using System;
using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// ПРОТОТИП другого устройства горы, предложенного ДМ 2026-08-15: гора — не силуэт (гребень плюс
    /// подошва), а СТОПКА ЯРУСОВ. Каждый ярус — та же доля-гармошка, только уже и выше предыдущего;
    /// огибающая стопки и даёт форму, а «остроконечность» задаётся расписанием ярусов.
    ///
    /// Живёт в стенде, а не в слое: ДМ сначала смотрит лист вариантов и выбирает расписание, число
    /// ярусов и манеру обводки, и только выбранное переезжает в MoundBuilder. Две линии рисования
    /// (изо-подземелье, первая арка гор) уже отменялись ПОСЛЕ того, как были написаны в приложении.
    /// </summary>
    enum Schedule
    {
        /// <summary>Нынешний вид: радиус падает быстро у подножия и медленно у макушки — шпиль.</summary>
        Peak,
        /// <summary>Прямые склоны: радиус убывает равномерно.</summary>
        Cone,
        /// <summary>Полукруг: крутая юбка, круглая макушка. Самый «взгляд сверху».</summary>
        Dome,
        /// <summary>Косинус: полого у подошвы, круто в середине, полого у макушки — та самая
        /// «синусоидовидная фигура» из слов ДМ.</summary>
        Sine,
    }

    /// <summary>Гора-стопка: ярусы снизу вверх плюс то, что нужно маляру.</summary>
    sealed class StackShape
    {
        public List<List<Vector2>> Levels = new List<List<Vector2>>();
        /// <summary>Подъём каждого яруса над подошвой. Нужен не для построения, а для СТЕНКИ: без
        /// неё стопка распадается на блюдца, висящие в воздухе (первый же прогон 2026-08-15).</summary>
        public List<float> Lifts = new List<float>();
        /// <summary>Середина звена — там спрашивается цвет карты под горой.</summary>
        public Vector2 Centre;
        /// <summary>Y самой нижней точки подошвы — ключ порядка маляра, как у MountainShape.Depth.</summary>
        public float Depth;
        public int Tier;
    }

    static class Stack
    {
        /// <summary>
        /// Высота яруса по его радиусу — ОБРАТНАЯ функция к «радиус от высоты», и взята она так
        /// намеренно. Ярусы раздаются равными шагами по РАДИУСУ (1, (N−1)/N, … 1/N), а расписание
        /// решает, на какой высоте каждый из них стоит. Если делить наоборот — равными шагами по
        /// высоте, — у купола почти всё сужение приходится на последний шаг, и при трёх-четырёх
        /// ярусах он выходит цилиндром с блюдцем наверху.
        ///
        /// Возвращает долю полной высоты: 0 у подошвы (r = 1), 1 у вершины (r = 0).
        /// </summary>
        public static float LiftAt(Schedule schedule, float r)
        {
            r = Math.Min(1f, Math.Max(0f, r));
            switch (schedule)
            {
                case Schedule.Cone: return 1f - r;
                case Schedule.Dome: return (float)Math.Sqrt(Math.Max(0f, 1f - r * r));
                case Schedule.Sine: return (float)(Math.Acos(2.0 * r - 1.0) / Math.PI);
                // Нынешний склон задан как y = t^1.6, где t — доля пути от края к вершине, то есть
                // t = 1 − r. Отсюда и показатель: расписание Peak — это ровно сегодняшняя гора.
                default: return (float)Math.Pow(1f - r, 1.6);
            }
        }

        /// <summary>
        /// Ярусы одной горы, снизу вверх. outline — замкнутый силуэт доли (LinkOutline.Build), то же,
        /// что получает MoundBuilder.
        ///
        /// Подошва строится теми же двумя преобразованиями, что и сейчас (§10): растяжение вдоль оси
        /// — чтобы подошвы соседей перекрывались и цепь читалась грядой, — и сжатие по вертикали
        /// под угол взгляда. Дальше ярус j — та же подошва, СЖАТАЯ К СЕРЕДИНЕ ЗВЕНА в r раз и
        /// поднятая на долю высоты. Сжатие равномерное, а не только поперёк оси: ДМ сказал
        /// «эллипс, чем выше — тем меньше радиус», а поперечное сжатие оставило бы наверху не
        /// вершину, а длинный конёк во всю длину звена.
        ///
        /// Потолка на длину подошвы (LobeCapFactor) здесь НЕТ намеренно: он чинил юбку СИЛУЭТА, а
        /// подошва стопки — честный план, вид сверху, и укорачивать её нечем и незачем. Проверяется
        /// это на карточке с грядой, уходящей вверх по картинке, — там изъян и находили.
        /// </summary>
        public static StackShape Build(IReadOnlyList<Vector2> outline, AxisLink link,
                                       float heightFactor, float squash,
                                       float stretchBack, float stretchFwd,
                                       int levels, Schedule schedule)
        {
            if (outline == null || outline.Count < 6 || link == null || levels < 2) return null;

            int n = outline.Count;
            Vector2 c = link.Mid, along = link.Tan;
            Vector2 across = new Vector2(-along.Y, along.X);

            var baseline = new Vector2[n];
            float depth = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = outline[i] - c;
                float a = d.X * along.X + d.Y * along.Y;
                a *= a >= 0f ? stretchFwd : stretchBack;
                float s = d.X * across.X + d.Y * across.Y;
                Vector2 p = c + along * a + across * s;
                baseline[i] = new Vector2(p.X, c.Y + (p.Y - c.Y) * squash);
                if (baseline[i].Y < depth) depth = baseline[i].Y;
            }

            float height = heightFactor * link.MidW * link.HeightJitter * link.TierScale;

            var shape = new StackShape { Depth = depth, Tier = link.Tier, Centre = c };
            for (int j = 0; j < levels; j++)
            {
                float r = (levels - j) / (float)levels;   // 1 … 1/N, никогда не ноль
                float lift = height * LiftAt(schedule, r);
                var ring = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = c + (baseline[i] - c) * r;
                    ring.Add(new Vector2(p.X, p.Y + lift));
                }
                shape.Levels.Add(ring);
                shape.Lifts.Add(lift);
            }
            return shape;
        }

        /// <summary>Весь массив: звенья настоящего конвейера → стопки, уже в порядке маляра.</summary>
        public static List<StackShape> BuildAll(IReadOnlyList<AxisLink> links, MountainSettings settings,
                                                int levels, Schedule schedule)
        {
            var result = new List<StackShape>();
            if (links == null) return result;

            foreach (var link in links)
            {
                var outline = LinkOutline.Build(link, settings.Waist, settings.SampleStep);
                if (outline == null) continue;
                float back = link.FreeStart ? 1f : settings.Stretch;
                float fwd = link.FreeEnd ? 1f : settings.Stretch;
                var shape = Build(outline, link, settings.HeightFactor, settings.Squash,
                                  back, fwd, levels, schedule);
                if (shape != null) result.Add(shape);
            }

            // Тот же порядок, что у MountainGeometry.SortForPainting: дальняя гора (БОЛЬШИЙ Y)
            // рисуется раньше. Внутри горы ярусы идут снизу вверх — верхний закрывает нижний.
            var order = new int[result.Count];
            var copy = new StackShape[result.Count];
            for (int i = 0; i < result.Count; i++) { order[i] = i; copy[i] = result[i]; }
            Array.Sort(order, (a, b) =>
            {
                int byDepth = copy[b].Depth.CompareTo(copy[a].Depth);
                if (byDepth != 0) return byDepth;
                int byTier = copy[b].Tier.CompareTo(copy[a].Tier);
                if (byTier != 0) return byTier;
                return a.CompareTo(b);
            });
            for (int i = 0; i < order.Length; i++) result[i] = copy[order[i]];
            return result;
        }
    }
}
