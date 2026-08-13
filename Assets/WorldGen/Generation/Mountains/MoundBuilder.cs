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

        /// <summary>Показатель склона по умолчанию — тот, что в §10. Живое значение приходит
        /// настройкой: им и правится «остриё против взгляда сверху».</summary>
        public const float DefaultSlopeExponent = 1.6f;

        /// <summary>Доля полуразмаха подошвы, ближе которой к краю вершина не ставится: иначе у
        /// косого звена вершина съезжает на самый угол и склон вырождается в вертикаль.</summary>
        const float ApexInset = 0.15f;

        /// <summary>Потолок на полудлину подошвы вдоль оси, в полуширинах горы. См. подробности в
        /// Build: он держит юбку горы соразмерной её высоте, когда звено вышло длинным.</summary>
        const float LobeCapFactor = 1.2f;

        /// <summary>Зазор, на который подошву держат ниже гребня, в мировых единицах.</summary>
        const float ClampGap = 1e-3f;

        /// <summary>
        /// Гора над звеном. outline — замкнутый силуэт доли (LinkOutline.Build).
        /// stretchBack/stretchFwd — растяжение подошвы вдоль оси назад и вперёд (k из §10); у
        /// свободного конца ставится 1. minSpan — ширина подошвы, ниже которой горы нет.
        /// Возвращает null, если строить нечего.
        /// </summary>
        public static MountainShape Build(IReadOnlyList<Vector2> outline, AxisLink link,
                                          float heightFactor, float squash,
                                          float stretchBack, float stretchFwd, float minSpan,
                                          float slopeExponent = DefaultSlopeExponent)
        {
            if (outline == null || outline.Count < 6 || link == null) return null;

            int n = outline.Count;
            Vector2 c = link.Mid, u = link.Tan;
            Vector2 across = new Vector2(-u.Y, u.X);

            // Подошва: вдоль оси — растяжение (каждая сторона своим множителем), поперёк — ничего,
            // поэтому ширина горы остаётся ровно той, что дало поле расстояний, и силуэт не теряет
            // связи с формой мазка. Затем всё сплющивается по вертикали относительно середины.
            var along = new float[n];
            var side = new float[n];
            float longest = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = outline[i] - c;
                float a = d.X * u.X + d.Y * u.Y;
                a *= a >= 0f ? stretchFwd : stretchBack;
                along[i] = a;
                side[i] = d.X * across.X + d.Y * across.Y;
                float abs = Math.Abs(a);
                if (abs > longest) longest = abs;
            }

            // Потолок на длину подошвы вдоль оси. Без него длинное звено отращивает длинную юбку:
            // подошва — это план, вид сверху, и у гряды, уходящей ВВЕРХ по картинке, вся её длина
            // ложится вниз по экрану. Гора тогда превращается в приземистую «чешуйку» с рюшем
            // вместо склонов — это и был изъян, найденный ДМ 2026-08-14.
            //
            // Мера — полуширина самой горы. При 1.2·w юбка выходит той же доли высоты, что у горы
            // ПОПЕРЁК экрана (замер: ширина 22, высота 22, юбка 7), а горы поперёк потолка не
            // замечают вовсе: растяжение даёт им 1.12·w, и перекрытие соседей, из которого выходят
            // перевалы, остаётся нетронутым.
            float cap = LobeCapFactor * link.MidW;
            float shrink = longest > cap && cap > 0f ? cap / longest : 1f;

            var baseline = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p = c + u * (along[i] * shrink) + across * side[i];
                baseline[i] = new Vector2(p.X, c.Y + (p.Y - c.Y) * squash);
            }

            int iLeft = 0, iRight = 0;
            float nearest = float.PositiveInfinity;   // самая НИЗКАЯ точка подошвы — ближняя к зрителю
            for (int i = 0; i < n; i++)
            {
                // При равном X берём НИЖНЮЮ точку: она и есть видимый край подошвы, а верхняя
                // закрыта телом горы. Без этого выпрямление подошвы упиралось в свой же левый конец.
                if (baseline[i].X < baseline[iLeft].X
                    || (baseline[i].X == baseline[iLeft].X && baseline[i].Y < baseline[iLeft].Y)) iLeft = i;
                if (baseline[i].X > baseline[iRight].X
                    || (baseline[i].X == baseline[iRight].X && baseline[i].Y < baseline[iRight].Y)) iRight = i;
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
            front = MakeMonotone(front);

            float height = heightFactor * link.MidW * link.HeightJitter * link.TierScale;
            float apexX = Math.Max(pl.X + span * ApexInset, Math.Min(pr.X - span * ApexInset, link.Mid.X));
            var apex = new Vector2(apexX, link.Mid.Y + height);

            var crest = new List<Vector2>(SlopeSamples * 2 + 1) { pl };
            for (int i = 1; i <= SlopeSamples; i++)
            {
                float t = i / (float)SlopeSamples;
                float g = (float)Math.Pow(t, slopeExponent);
                crest.Add(new Vector2(pl.X + (apex.X - pl.X) * t, pl.Y + (apex.Y - pl.Y) * g));
            }
            for (int i = 1; i <= SlopeSamples; i++)
            {
                float t = i / (float)SlopeSamples;
                float g = (float)Math.Pow(1f - t, slopeExponent);
                crest.Add(new Vector2(apex.X + (pr.X - apex.X) * t, pr.Y + (apex.Y - pr.Y) * g));
            }

            ClampUnderCrest(front, crest);

            return new MountainShape
            {
                Crest = crest,
                Front = front,
                Apex = apex,
                Depth = nearest,
                Tier = link.Tier,
            };
        }

        /// <summary>
        /// Выпрямляет ближнюю дугу подошвы: ход остаётся только СЛЕВА НАПРАВО, а заворот схлопывается
        /// в свою нижнюю точку.
        ///
        /// Зачем. У звена, которое одновременно изогнуто и идёт близко к вертикали, подошва
        /// заворачивается назад по X. Силуэт (гребень + подошва) тогда сам себя пересекает, и в
        /// заливке появляется ВЫКУС — угловатая дыра, сквозь которую видно карту. Это тот самый
        /// «известный изъян», записанный в задаче 5 как безобидный: на мазках он и правда редок
        /// (2 горы из 156), но маска приложения строится по многоугольникам КЛЕТОК, её контур
        /// бугрист на своём шаге, оси от этого куда извилистее — и на ней самопересекается уже
        /// 14 силуэтов из 115. Изъян нашёл ДМ 2026-08-14 по скриншотам.
        ///
        /// Почему заворот СХЛОПЫВАЕТСЯ, а не выбрасывается. Первая редакция просто выбрасывала
        /// завернувшиеся точки — и вместе с ними уходил кусок низа горы: в подошве появлялся вырез,
        /// который ДМ увидел на первом же скриншоте после правки. Теперь глубина подошвы цела:
        /// текущая точка опускается до самой низкой в завороте. Нижняя, а не верхняя, потому что
        /// подошва — это ВИДИМЫЙ низ горы: из двух уровней над одним X зритель видит нижний,
        /// верхний закрыт телом самой горы.
        /// </summary>
        static List<Vector2> MakeMonotone(List<Vector2> chain)
        {
            var result = new List<Vector2>(chain.Count);
            foreach (var p in chain)
            {
                if (result.Count == 0) { result.Add(p); continue; }

                var last = result[result.Count - 1];
                if (p.X > last.X) { result.Add(p); continue; }
                if (result.Count == 1) continue;   // левый конец — общий с гребнем, его не трогаем

                // Ход НАЗАД. Точку не выбрасываем — вместе с ней ушёл бы кусок низа горы, и в
                // подошве появлялся вырез (это ДМ и увидел 2026-08-14 после первой редакции
                // выпрямления). Вместо этого опускаем текущую точку до самой низкой в завороте:
                // заворот схлопывается в одну точку, глубина подошвы цела, X больше не пятится.
                if (p.Y < last.Y) result[result.Count - 1] = new Vector2(last.X, p.Y);
            }
            // Правый конец обязан остаться на месте: гребень приходит ровно в него, и потерянный
            // конец разорвал бы силуэт. Левый неприкосновенен по той же причине — он и не теряется,
            // первая точка кладётся безусловно и может только опуститься.
            var tail = chain[chain.Count - 1];
            if (result.Count == 0 || result[result.Count - 1] != tail) result.Add(tail);
            return result.Count >= 2 ? result : chain;
        }

        /// <summary>
        /// Прижимает подошву к гребню: видимый НИЗ горы не может оказаться выше её видимого ВЕРХА.
        ///
        /// После выпрямления обе цепи идут слева направо и делят концы, поэтому пересечься они могут
        /// только одним способом — если подошва где-то поднялась над гребнем. Случается это у самых
        /// концов, где склон выходит из подножия почти горизонтально (показатель 1.6), а подошва там
        /// же выгибается вверх. Оставшиеся после выпрямления самопересечения — ровно эти: 2 силуэта
        /// из 115 на клеточной маске. Прижатие закрывает их все.
        /// </summary>
        static void ClampUnderCrest(List<Vector2> front, List<Vector2> crest)
        {
            int j = 0;
            for (int i = 1; i < front.Count - 1; i++)
            {
                float x = front[i].X;
                while (j + 2 < crest.Count && crest[j + 1].X < x) j++;
                Vector2 a = crest[j], b = crest[j + 1];
                float t = Math.Abs(b.X - a.X) < 1e-6f ? 0f : (x - a.X) / (b.X - a.X);
                float top = a.Y + (b.Y - a.Y) * t;
                // Строго НИЖЕ, а не вровень: точка ровно на гребне — это касание силуэта самого
                // себя, а для сшивки оно не лучше пересечения.
                if (front[i].Y > top - ClampGap) front[i] = new Vector2(x, top - ClampGap);
            }
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
