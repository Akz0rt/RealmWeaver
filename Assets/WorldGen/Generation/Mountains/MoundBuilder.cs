using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §10 «Горы над звеньями»: доля-гармошка — это план, вид сверху; гора строится над ней как
    /// объём. Вся геометрия задана одним соглашением о взгляде: смотрим горизонтально, земля видна
    /// под углом, высота откладывается прямо вверх по экрану (у нас — по +Y в Site-координатах).
    ///
    /// Подошва — та же доля, с двумя преобразованиями относительно середины звена: ×k вдоль оси и
    /// ×1/a по вертикали. Сжатие по вертикали — тот же угол взгляда, из-за которого позвонки чаще
    /// идут по вертикали; без него гора сидит на круглом блине и читается как палатка. Растяжение
    /// вдоль оси заставляет подошвы соседей перекрываться: склоны пересекаются ВЫШЕ земли, и это
    /// перевал — цепь читается как гряда, а не как ряд кучек.
    ///
    /// Порт `moundShape` из `docs/mountain-brush-step1.html` с двумя правками:
    /// • ось Y перевёрнута (см. AxisLink) — высота прибавляется, ближняя дуга та, что ниже по Y;
    /// • у свободного конца оси растягивать некуда: соседа с этой стороны нет, и растяжение только
    ///   вылезает за нарисованный мазок (§14 «вылет за мазок»). Поэтому стороны растягиваются
    ///   независимо, и крайнее звено растягивают только внутрь.
    /// </summary>
    public static class MoundBuilder
    {
        /// <summary>Отсчётов на один склон.</summary>
        const int SlopeSamples = 18;

        /// <summary>Показатель склона: больше единицы — склон вогнутый, пологий у подножия и самый
        /// крутой у гребня, поэтому два склона сходятся в ОСТРУЮ вершину. Привычная сглаживающая
        /// 3t²−2t³ дала бы нулевую производную на вершине, то есть плоскую макушку.</summary>
        const double SlopeExponent = 1.6;

        /// <summary>Доля полуразмаха подошвы, ближе которой к краю вершина не ставится: иначе у
        /// косого звена вершина съезжает на самый угол и склон вырождается в вертикаль.</summary>
        const float ApexInset = 0.15f;

        /// <summary>
        /// Гора над звеном. outline — замкнутый силуэт доли (LinkOutline.Build).
        /// stretchBack/stretchFwd — растяжение подошвы вдоль оси назад и вперёд (k из §10); у
        /// свободного конца ставится 1. minSpan — ширина подошвы, ниже которой горы нет.
        /// Возвращает null, если строить нечего.
        /// </summary>
        public static MountainShape Build(IReadOnlyList<Vector2> outline, AxisLink link,
                                          float heightFactor, float squash,
                                          float stretchBack, float stretchFwd, float minSpan)
        {
            if (outline == null || outline.Count < 6 || link == null) return null;

            int n = outline.Count;
            Vector2 c = link.Mid, u = link.Tan;
            Vector2 across = new Vector2(-u.Y, u.X);

            // Подошва: вдоль оси — растяжение (каждая сторона своим множителем), поперёк — ничего,
            // поэтому ширина горы остаётся ровно той, что дало поле расстояний, и силуэт не теряет
            // связи с формой мазка. Затем всё сплющивается по вертикали относительно середины.
            var baseline = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 d = outline[i] - c;
                float along = d.X * u.X + d.Y * u.Y;
                along *= along >= 0f ? stretchFwd : stretchBack;
                float side = d.X * across.X + d.Y * across.Y;
                Vector2 p = c + u * along + across * side;
                baseline[i] = new Vector2(p.X, c.Y + (p.Y - c.Y) * squash);
            }

            int iLeft = 0, iRight = 0;
            float nearest = float.PositiveInfinity;   // самая НИЗКАЯ точка подошвы — ближняя к зрителю
            for (int i = 0; i < n; i++)
            {
                if (baseline[i].X < baseline[iLeft].X) iLeft = i;
                if (baseline[i].X > baseline[iRight].X) iRight = i;
                if (baseline[i].Y < nearest) nearest = baseline[i].Y;
            }

            Vector2 pl = baseline[iLeft], pr = baseline[iRight];
            float span = pr.X - pl.X;
            if (span < minSpan) return null;

            // Силуэт распадается на две дуги между крайними точками. Ближняя — та, что ниже по
            // экрану; дальняя не видна, её закрывает сама гора.
            var arcA = Chain(baseline, iRight, iLeft);
            var arcB = Chain(baseline, iLeft, iRight);
            Reverse(arcB);
            var front = AverageY(arcA) <= AverageY(arcB) ? arcA : arcB;
            Reverse(front);   // обе дуги идут справа налево — разворачиваем в «слева направо»

            float height = heightFactor * link.MidW * link.HeightJitter * link.TierScale;
            float apexX = Math.Max(pl.X + span * ApexInset, Math.Min(pr.X - span * ApexInset, link.Mid.X));
            var apex = new Vector2(apexX, link.Mid.Y + height);

            var crest = new List<Vector2>(SlopeSamples * 2 + 1) { pl };
            for (int i = 1; i <= SlopeSamples; i++)
            {
                float t = i / (float)SlopeSamples;
                float g = (float)Math.Pow(t, SlopeExponent);
                crest.Add(new Vector2(pl.X + (apex.X - pl.X) * t, pl.Y + (apex.Y - pl.Y) * g));
            }
            for (int i = 1; i <= SlopeSamples; i++)
            {
                float t = i / (float)SlopeSamples;
                float g = (float)Math.Pow(1f - t, SlopeExponent);
                crest.Add(new Vector2(apex.X + (pr.X - apex.X) * t, pr.Y + (apex.Y - pr.Y) * g));
            }

            return new MountainShape
            {
                Crest = crest,
                Front = front,
                Apex = apex,
                Depth = nearest,
                Tier = link.Tier,
            };
        }

        /// <summary>Кусок замкнутого силуэта от индекса from до to по возрастанию индекса.</summary>
        static List<Vector2> Chain(Vector2[] loop, int from, int to)
        {
            var result = new List<Vector2>();
            int i = from;
            while (true)
            {
                result.Add(loop[i]);
                if (i == to) break;
                i = (i + 1) % loop.Length;
            }
            return result;
        }

        static void Reverse(List<Vector2> pts) => pts.Reverse();

        static float AverageY(List<Vector2> pts)
        {
            if (pts.Count == 0) return 0f;
            float sum = 0f;
            foreach (var p in pts) sum += p.Y;
            return sum / pts.Count;
        }
    }
}
