using System;
using System.Collections.Generic;

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
    /// цепь гор рвётся. Поэтому кольцо принимается, только если ПОД НИМ ЕСТЬ ГЛУБИНА: у компоненты
    /// связности множества {D ≥ L} максимум поля обязан быть не меньше L + 0.6R. Отвергнутая
    /// компонента заведомо не толще 1.6R — ровно того предела, до которого раздувается скелетная ось
    /// (§9), поэтому она ничего не теряет, уходя к скелету.
    /// </summary>
    public static class RingSelection
    {
        /// <summary>Запас глубины, ниже которого кольцо отвергается. Связан с потолком ширины
        /// скелетной оси: 1 + 0.6 = 1.6R.</summary>
        public const float DepthMargin = 0.6f;

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

        /// <summary>Кольца, прошедшие отбор по глубине, от края вглубь.</summary>
        public static List<RingAxis> Select(float[] field, int w, int h, float radiusCells)
        {
            var accepted = new List<RingAxis>();
            float maxDistance = DistanceField.Max(field);

            foreach (float level in Levels(maxDistance, radiusCells))
            {
                var labels = new int[w * h];
                for (int i = 0; i < labels.Length; i++) labels[i] = -1;
                var depth = ComponentDepths(field, w, h, level, labels);

                foreach (var contour in IsoContours.Trace(field, w, h, level))
                {
                    if (contour.Points.Count < 4) continue;
                    int id = LabelAt(labels, w, h, contour.Points[0].X, contour.Points[0].Y);
                    if (id < 0) continue;
                    if (depth[id] < level + DepthMargin * radiusCells) continue;
                    accepted.Add(new RingAxis { Contour = contour, Level = level });
                }
            }
            return accepted;
        }

        /// <summary>Размечает компоненты связности множества {D ≥ level} и возвращает максимум поля
        /// в каждой. Обход по четырём соседям: по восьми две массы, соприкасающиеся только углом,
        /// считались бы одной, и мелкая заусеница делилась бы глубиной соседа.</summary>
        static List<float> ComponentDepths(float[] field, int w, int h, float level, int[] labels)
        {
            var depths = new List<float>();
            var stack = new Stack<int>();

            for (int start = 0; start < field.Length; start++)
            {
                if (field[start] < level || labels[start] >= 0) continue;

                int id = depths.Count;
                float max = 0f;
                labels[start] = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (field[i] > max) max = field[i];
                    int x = i % w, y = i / w;
                    Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
                }
                depths.Add(max);

                void Push(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    int k = ny * w + nx;
                    if (field[k] < level || labels[k] >= 0) return;
                    labels[k] = id;
                    stack.Push(k);
                }
            }
            return depths;
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
