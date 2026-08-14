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

        /// <summary>
        /// Насколько велик скачок границы между соседними лучами.
        ///
        /// Зачем это нужно. У КОНУСА подъём линеен, и вынос яруса за край растёт (или падает) по r
        /// строго — значит максимум всегда сидит на конце: либо на подошве, либо на вершине. У
        /// соседних лучей он скачет между ними, и на месте скачка проходит СКЛОН: прямая от точки
        /// подошвы до вершины. Точек границы там нет вовсе — они на концах, — и склон остался бы
        /// необведённым. Это и было видно на первом же снимке переноса: вершины обведены, а бока
        /// голые.
        ///
        /// У купола скачка нет: максимум едет по r плавно, и склон обводится обычными штрихами.
        /// Поэтому правило одно и включается само там, где нужно.
        /// </summary>
        public static float Jump(MountainShape shape, int i)
        {
            int n = shape.SilhouetteR.Length;
            return Math.Abs(shape.SilhouetteR[i] - shape.SilhouetteR[(i + 1) % n]);
        }

        /// <summary>Порог, выше которого разрыв границы считается скачком и обводится по меридиану.
        /// Ниже — соседние точки и так рядом, штрихи перекрываются сами.</summary>
        public const float JumpThreshold = 0.15f;

        /// <summary>Длина штриха линии в точке границы: половина расстояния между соседними точками
        /// того же яруса, с небольшим нахлёстом. Штрихи обязаны перекрываться — иначе линия
        /// рассыпается в пунктир.</summary>
        public const float DashOverlap = 1.25f;

        /// <summary>Направление и длина штриха линии на луче i. Касательная берётся по СВОЕМУ ярусу
        /// (r точки границы), а не по подошве: у вершины кольцо мельче, и штрих обязан быть короче,
        /// иначе он вылезает за гору.</summary>
        public static void Dash(MountainShape shape, int i, LiftSamples profile,
                                out Vector2 dir, out float half)
        {
            int n = shape.Base.Length;
            float r = shape.SilhouetteR[i];
            Vector2 a = Meridian(shape, (i - 1 + n) % n, r, profile);
            Vector2 b = Meridian(shape, (i + 1) % n, r, profile);
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 1e-6f) { dir = new Vector2(1f, 0f); half = 0f; return; }
            dir = d / len;
            half = len * 0.5f * DashOverlap;
        }
    }
}
