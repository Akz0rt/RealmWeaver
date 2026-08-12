using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Тело горы, разложенное на треугольники. Живёт в чистом слое, а не в сборке меша, по одной
    /// причине: это математика, и её обязан проверять стенд. Рендер только перекладывает индексы.
    ///
    /// Здесь стояла сшивка полосой: идём по гребню и по ближней дуге подошвы сразу, всякий раз
    /// продвигая ту цепь, что отстала. Дёшево и почти всегда верно — но НЕ всегда, и это замерено.
    /// На кольцевом мазке (тридцать гор) полоса кладёт треугольники, вылезающие за силуэт: перебор
    /// площади до 12 % у отдельной горы. Причина не в том, что подошва заворачивается назад по
    /// горизонтали, как считалось в разборе: сам силуэт всегда простой, самопересечений в нём нет, —
    /// а в том, что у сильно невыпуклой фигуры диагональ между цепями может выйти наружу. Мерить
    /// отставание горизонтальной координатой или долей пройденной длины одинаково не помогает:
    /// у обоих правил находится своя гора, на которой они врут.
    ///
    /// Поэтому — обычное отрезание ушей. Оно точно триангулирует любой простой многоугольник, стоит
    /// на нашем размере (полсотни точек на гору) сущие пустяки и не опирается ни на какие свойства
    /// формы. Проверка стенда требует от него ровной площади: сумма модулей площадей треугольников
    /// обязана совпасть с площадью силуэта — то есть ни перекрытий, ни вылетов, ни вывернутых.
    /// </summary>
    public static class MountainTriangulation
    {
        /// <summary>
        /// Треугольники тела горы. Индексы указывают в объединённый список «сначала все точки
        /// гребня, следом все точки подошвы» — рендер ровно в таком порядке их и складывает.
        /// </summary>
        public static int[] Fill(MountainShape shape)
        {
            if (shape == null) return Array.Empty<int>();
            var crest = shape.Crest;
            var front = shape.Front;
            if (crest == null || front == null || crest.Count < 2 || front.Count < 2)
                return Array.Empty<int>();

            // Силуэт: гребень слева направо, следом подошва справа налево. Концы у цепей общие, и
            // повторять их в контуре нельзя — совпадающие подряд точки ушей не дают.
            var loop = new List<Vector2>(crest.Count + front.Count);
            var origin = new List<int>(crest.Count + front.Count);
            for (int i = 0; i < crest.Count; i++) { loop.Add(crest[i]); origin.Add(i); }
            for (int i = front.Count - 2; i >= 1; i--) { loop.Add(front[i]); origin.Add(crest.Count + i); }
            if (loop.Count < 3) return Array.Empty<int>();

            // Обход выравниваем против часовой стрелки: дальше всё проверяется знаком площади.
            if (SignedArea(loop) < 0f)
            {
                loop.Reverse();
                origin.Reverse();
            }

            var tris = new List<int>((loop.Count - 2) * 3);
            var alive = new List<int>(loop.Count);
            for (int i = 0; i < loop.Count; i++) alive.Add(i);

            int guard = alive.Count * alive.Count + 8;
            int cursor = 0;
            while (alive.Count > 3 && guard-- > 0)
            {
                int n = alive.Count;
                int prev = alive[(cursor + n - 1) % n];
                int here = alive[cursor % n];
                int next = alive[(cursor + 1) % n];

                if (IsEar(loop, alive, prev, here, next))
                {
                    tris.Add(origin[prev]); tris.Add(origin[here]); tris.Add(origin[next]);
                    alive.RemoveAt(cursor % n);
                    cursor = cursor % alive.Count;
                    continue;
                }
                cursor = (cursor + 1) % n;
            }

            // Сторож на случай, если ушей не нашлось вовсе (например, точки легли на одну прямую):
            // остаток закрываем веером. Лучше слегка неверный низ горы, чем повисший цикл.
            for (int i = 1; i + 1 < alive.Count; i++)
            {
                tris.Add(origin[alive[0]]); tris.Add(origin[alive[i]]); tris.Add(origin[alive[i + 1]]);
            }
            return tris.ToArray();
        }

        /// <summary>Ухо — выпуклая вершина, в чей треугольник не попала ни одна другая вершина.</summary>
        static bool IsEar(List<Vector2> loop, List<int> alive, int prev, int here, int next)
        {
            Vector2 a = loop[prev], b = loop[here], c = loop[next];
            float area = Cross(a, b, c);
            if (area <= 1e-9f) return false;   // вершина вогнутая или вырожденная

            foreach (int index in alive)
            {
                if (index == prev || index == here || index == next) continue;
                if (Inside(a, b, c, loop[index])) return false;
            }
            return true;
        }

        static bool Inside(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
            => Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f;

        static float Cross(Vector2 a, Vector2 b, Vector2 c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        static float SignedArea(List<Vector2> loop)
        {
            float area = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
        }
    }
}
