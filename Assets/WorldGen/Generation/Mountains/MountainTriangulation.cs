using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Тело горы в треугольники.
    ///
    /// Раньше здесь сшивались две цепи — гребень и ближняя дуга подошвы, — и половина файла уходила
    /// на случаи, когда дуга заворачивалась назад и полоса пересекала сама себя. Ни цепей, ни
    /// заворотов больше нет: тело выкладывается ярусами.
    ///
    /// На ярус приходится веер (его собственный круг) и полоса до соседнего яруса. Полоса сшивается
    /// ЗИПОМ по номеру точки — кольца получены из одной и той же подошвы, значит точек у них поровну
    /// и соответствие однозначно. Самопересечься такой полосе нечем, и весь класс прежних изъянов —
    /// чешуя, угловатые прикусы, зазубрина снизу — стал невозможен по построению.
    ///
    /// Ярусов ровно столько, сколько нужно гладкой огибающей (MoundBuilder.Levels): у конуса два,
    /// у купола около десятка. Восемнадцать колец на каждую гору, как было в браузерном прототипе,
    /// в меше стоили бы миллионов треугольников.
    /// </summary>
    public static class MountainTriangulation
    {
        /// <summary>Точка яруса r на луче i.</summary>
        public static Vector2 Meridian(MountainShape shape, int index, float r, LiftSamples profile)
        {
            Vector2 c = shape.Centre;
            Vector2 d = shape.Base[index] - c;
            return new Vector2(c.X + d.X * r, c.Y + d.Y * r + shape.Height * profile.LiftAt(r));
        }

        /// <summary>
        /// ГДЕ ЭТА ТОЧКА СТОИТ НА ЗЕМЛЕ — тот же ярус r на том же луче, но без подъёма.
        ///
        /// Нужна ровно для одного вопроса: суша ли под этим куском горы. Спрашивать про
        /// нарисованное место нельзя — гора нарисована приподнятой, и вершина прибрежной горы
        /// попадает на воду, которая лежит ЗА ней. По нарисованному месту такой горе отрубило бы
        /// макушку; по месту на земле отсекается только юбка, заехавшая в озеро, а силуэт остаётся
        /// целым. Это разные вопросы, и их нельзя мерить одной точкой.
        /// </summary>
        public static Vector2 Foot(MountainShape shape, int index, float r)
        {
            Vector2 c = shape.Centre;
            Vector2 d = shape.Base[index] - c;
            return new Vector2(c.X + d.X * r, c.Y + d.Y * r);
        }

        /// <summary>
        /// Раскладка тела: вершины и треугольники.
        ///
        /// Вершины идут ярусами: сперва центр яруса, потом его кольцо, потом следующий ярус. Так
        /// вызывающему остаётся только перевести точки в свои координаты и спросить цвет земли — а
        /// раскладка, в которой можно ошибиться, лежит здесь, где её проверяет стенд.
        /// </summary>
        public static void Fill(MountainShape shape, LiftSamples profile,
                                List<Vector2> verts, List<int> tris)
        {
            verts.Clear();
            tris.Clear();
            if (shape?.Base == null || shape.LevelR == null || shape.LevelR.Length < 2) return;

            int n = shape.Base.Length;
            int levels = shape.LevelR.Length;
            int stride = n + 1;                       // центр яруса плюс его кольцо

            for (int j = 0; j < levels; j++)
            {
                float r = shape.LevelR[j];
                float lift = shape.Height * profile.LiftAt(r);
                verts.Add(new Vector2(shape.Centre.X, shape.Centre.Y + lift));
                for (int i = 0; i < n; i++)
                {
                    Vector2 d = shape.Base[i] - shape.Centre;
                    verts.Add(new Vector2(shape.Centre.X + d.X * r, shape.Centre.Y + d.Y * r + lift));
                }
            }

            for (int j = 0; j < levels; j++)
            {
                int b = j * stride;
                for (int i = 0; i < n; i++)           // веер яруса
                {
                    tris.Add(b);
                    tris.Add(b + 1 + i);
                    tris.Add(b + 1 + (i + 1) % n);
                }
                if (j + 1 >= levels) continue;

                int u = (j + 1) * stride;             // полоса до следующего яруса
                for (int i = 0; i < n; i++)
                {
                    int i2 = (i + 1) % n;
                    tris.Add(b + 1 + i); tris.Add(b + 1 + i2); tris.Add(u + 1 + i2);
                    tris.Add(b + 1 + i); tris.Add(u + 1 + i2); tris.Add(u + 1 + i);
                }
            }
        }

        /// <summary>
        /// ТОН КАЖДОЙ ВЕРШИНЫ ТЕЛА — доля вдоль шкалы «светлый → тёмный» (см. MountainFill).
        ///
        /// Живёт вплотную к Fill и повторяет её обход один в один: порядок вершин знает ровно один
        /// файл, и добавить ярус, забыв про краску, здесь нельзя. Считать тон на стороне рендера
        /// значило бы вывести наружу и шаг раскладки (центр плюс кольцо), и правило цвета — а
        /// Rendering не проверяет никто, кроме компилятора.
        ///
        /// Тон постоянен ВНУТРИ ЯРУСА: подъём у центра кольца и у самого кольца один и тот же.
        /// Отсюда же берётся дешевизна — краску достаточно перевести в цвет один раз на ярус, а не
        /// на каждую из сотен тысяч вершин (перевод в линейное пространство стоит трёх Pow).
        /// Градиент между ярусами дорисовывает видеокарта, растягивая цвет по треугольнику.
        /// </summary>
        public static void FillTones(MountainShape shape, LiftSamples profile, int tierCount,
                                     List<float> tones)
        {
            tones.Clear();
            if (shape?.Base == null || shape.LevelR == null || shape.LevelR.Length < 2) return;

            int n = shape.Base.Length;
            for (int j = 0; j < shape.LevelR.Length; j++)
            {
                float tone = MountainFill.Tone(shape.Tier, tierCount, profile.LiftAt(shape.LevelR[j]));
                for (int i = 0; i <= n; i++) tones.Add(tone);      // центр яруса плюс его кольцо
            }
        }

        /// <summary>Сколько вершин и треугольников выйдет у тела — чтобы можно было отвести буфер
        /// заранее и не растить списки на каждую гору.</summary>
        public static void FillSize(MountainShape shape, out int verts, out int tris)
        {
            verts = tris = 0;
            if (shape?.Base == null || shape.LevelR == null || shape.LevelR.Length < 2) return;
            int n = shape.Base.Length, levels = shape.LevelR.Length;
            verts = levels * (n + 1);
            tris = (levels * n + (levels - 1) * n * 2) * 3;
        }

    }
}
