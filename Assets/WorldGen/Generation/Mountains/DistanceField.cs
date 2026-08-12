using System;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §3 «Поле расстояний»: для каждой ячейки маски — расстояние до ближайшей ячейки фона, В
    /// ЯЧЕЙКАХ. Это центральный объект всего алгоритма: из него выводятся и оси, и ширина массы, и
    /// критерий «кольцо здесь уместно или скелет», и ярусы.
    ///
    /// Преобразование Фельзенцвальба–Хаттенлохера: два прохода (по столбцам, затем по строкам), в
    /// каждом одномерная задача решается как поиск нижней огибающей семейства парабол за линейное
    /// время. Итог — ТОЧНОЕ евклидово расстояние за O(N), без приближений вроде чамфер-масок.
    /// Приближение тут стоило бы дорого: на нём стоит и толщина массы, и ярусы, и вся геометрия
    /// поехала бы по диагоналям.
    /// </summary>
    public static class DistanceField
    {
        const double Inf = 1e20;

        /// <summary>Расстояние до фона в ячейках. Для ячеек фона — ноль.</summary>
        public static float[] Build(MountainMask mask)
        {
            int w = mask.W, h = mask.H;
            var d = new double[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = mask.Cells[i] != 0 ? Inf : 0.0;

            int n = Math.Max(w, h);
            var f = new double[n];
            var outp = new double[n];
            var v = new int[n];
            var z = new double[n + 1];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = d[y * w + x];
                Transform1D(f, outp, v, z, h);
                for (int y = 0; y < h; y++) d[y * w + x] = outp[y];
            }

            var result = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) f[x] = d[y * w + x];
                Transform1D(f, outp, v, z, w);
                for (int x = 0; x < w; x++) result[y * w + x] = (float)Math.Sqrt(outp[x]);
            }
            return result;
        }

        /// <summary>Одномерное преобразование: нижняя огибающая парабол f(q) + (x−q)².</summary>
        static void Transform1D(double[] f, double[] outp, int[] v, double[] z, int len)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Inf;
            z[1] = Inf;

            for (int q = 1; q < len; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Inf;
            }

            k = 0;
            for (int q = 0; q < len; q++)
            {
                while (z[k + 1] < q) k++;
                double dx = q - v[k];
                outp[q] = dx * dx + f[v[k]];
            }
        }

        /// <summary>Наибольшее значение поля — «толщина» пятна в ячейках.</summary>
        public static float Max(float[] field)
        {
            float max = 0f;
            for (int i = 0; i < field.Length; i++) if (field[i] > max) max = field[i];
            return max;
        }

        /// <summary>Сглаживание поля усреднением по 3×3. Нужно доводке оси: подтяжка к гребню ищет
        /// максимум поля, а на сыром поле максимум дрожит от ячейки к ячейке, и ось обрастает
        /// шипами.</summary>
        public static float[] Blur(float[] field, int w, int h, int passes)
        {
            var a = (float[])field.Clone();
            var b = new float[w * h];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= w) continue;
                                sum += a[yy * w + xx];
                                count++;
                            }
                        }
                        b[y * w + x] = sum / count;
                    }
                }
                var t = a; a = b; b = t;
            }
            return a;
        }

        /// <summary>Билинейная выборка поля в дробных координатах сетки.</summary>
        public static float Sample(float[] field, int w, int h, float gx, float gy)
        {
            if (gx < 0f) gx = 0f;
            if (gy < 0f) gy = 0f;
            if (gx > w - 1) gx = w - 1;
            if (gy > h - 1) gy = h - 1;

            int x0 = (int)gx, y0 = (int)gy;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = gx - x0, ty = gy - y0;

            float a = field[y0 * w + x0] + (field[y0 * w + x1] - field[y0 * w + x0]) * tx;
            float b = field[y1 * w + x0] + (field[y1 * w + x1] - field[y1 * w + x0]) * tx;
            return a + (b - a) * ty;
        }
    }
}
