using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Растровое представление пятна (§2): ячейка принадлежит маске, если её центр лежит не дальше
    /// радиуса кисти от какого-нибудь отрезка какого-нибудь РИСУЮЩЕГО мазка и при этом не накрыт
    /// стирающим. Сетка берётся по габаритам пятна с полем в несколько ячеек, чтобы граница
    /// гарантированно была окружена фоном — иначе поле расстояний у самого края решало бы, что там
    /// продолжается масса, и вершины худели бы вдоль невидимой границы кадра.
    ///
    /// Отличие от прототипа — в шаге сетки. Там CELL = 2 px, потому что холст один и зума нет; здесь
    /// шаг ПРИВЯЗАН К МАСШТАБУ гор (см. ChooseCell), и все пороги алгоритма, записанные в ячейках,
    /// остаются верными при любом размере кисти. Плюс потолок на размер сетки: мазок через всю карту
    /// иначе рождает миллионы ячеек и морозит приложение. Переполнение огрубляет шаг, а не обрезает
    /// пятно — расстояние операция не местная, и обрезка занизила бы толщину у края рамки.
    /// </summary>
    public sealed class MountainMask
    {
        /// <summary>1 — масса, 0 — фон. Индекс = y*W + x.</summary>
        public byte[] Cells;
        public int W;
        public int H;

        /// <summary>Мировая координата центра ячейки (0,0) минус полклетки: мир = O + (g+0.5)·Cell.</summary>
        public float Ox;
        public float Oy;

        /// <summary>Шаг сетки в мировых единицах — тот, что получился после огрубления.</summary>
        public float Cell;

        /// <summary>Поле в ячейках вокруг габаритов пятна.</summary>
        public const int PadCells = 6;

        /// <summary>Сколько ячеек приходится на радиус горы. В прототипе R = 44 px при CELL = 2 px,
        /// то есть ровно 22; на этом отношении подобраны все пороги, записанные в ячейках.</summary>
        public const float CellsPerR = 22f;

        /// <summary>Шаг сетки: столько, чтобы и гора, и кисть были разрешены. По кисти разрешение
        /// нужнее, чем по горе: тонкий мазок иначе распадётся на клетки ещё до всякого расчёта.</summary>
        public static float ChooseCell(float mountainRadius, float brushRadius)
        {
            float byMountain = Math.Max(0.0001f, mountainRadius) / CellsPerR;
            float byBrush = Math.Max(0.0001f, brushRadius) / 15f;
            return Math.Min(byMountain, byBrush);
        }

        public Vector2 GridToWorld(float gx, float gy) => new Vector2(Ox + gx * Cell, Oy + gy * Cell);

        public Vector2 WorldToGrid(Vector2 p) => new Vector2((p.X - Ox) / Cell, (p.Y - Oy) / Cell);

        public bool At(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && Cells[y * W + x] != 0;

        /// <summary>
        /// Растеризует пятно. desiredCell — желаемый шаг (ChooseCell), maxCells — потолок на число
        /// ячеек: если не влезает, шаг увеличивается, пока не влезет.
        /// </summary>
        public static MountainMask Build(MountainBlob blob, float desiredCell, int maxCells = 4_000_000)
        {
            if (blob == null || blob.Strokes.Count == 0) return null;

            StrokeGeometry.Bounds(blob.Strokes, out float minX, out float minY, out float maxX, out float maxY);
            if (float.IsInfinity(minX)) return null;

            float cell = Math.Max(1e-4f, desiredCell);
            int w, h;
            while (true)
            {
                float pad = PadCells * cell;
                w = (int)Math.Ceiling((maxX + pad - (minX - pad)) / cell) + 1;
                h = (int)Math.Ceiling((maxY + pad - (minY - pad)) / cell) + 1;
                if ((long)w * h <= maxCells || w <= 4 || h <= 4) break;
                cell *= 1.5f;
            }

            var mask = new MountainMask
            {
                W = w,
                H = h,
                Cell = cell,
                Ox = minX - PadCells * cell,
                Oy = minY - PadCells * cell,
                Cells = new byte[w * h],
            };

            foreach (var stroke in blob.Strokes) mask.Stamp(stroke, 1);
            foreach (var eraser in blob.Erasers) mask.Stamp(eraser, 0);
            return mask;
        }

        /// <summary>Кладёт мазок в маску: value = 1 рисует, 0 стирает.</summary>
        void Stamp(MountainStroke stroke, byte value)
        {
            var pts = stroke.Points;
            if (pts.Count == 0) return;
            if (pts.Count == 1) { StampSegment(pts[0], pts[0], stroke.Radius, value); return; }
            for (int i = 1; i < pts.Count; i++) StampSegment(pts[i - 1], pts[i], stroke.Radius, value);
        }

        void StampSegment(Vector2 a, Vector2 b, float r, byte value)
        {
            int gx0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - r - Ox) / Cell));
            int gx1 = Math.Min(W - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + r - Ox) / Cell));
            int gy0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, b.Y) - r - Oy) / Cell));
            int gy1 = Math.Min(H - 1, (int)Math.Ceiling((Math.Max(a.Y, b.Y) + r - Oy) / Cell));

            Vector2 d = b - a;
            float len2 = d.LengthSquared();
            float r2 = r * r;

            for (int gy = gy0; gy <= gy1; gy++)
            {
                float py = Oy + (gy + 0.5f) * Cell;
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    float px = Ox + (gx + 0.5f) * Cell;
                    float t = len2 > 0f ? ((px - a.X) * d.X + (py - a.Y) * d.Y) / len2 : 0f;
                    t = t < 0f ? 0f : (t > 1f ? 1f : t);
                    float qx = px - (a.X + d.X * t), qy = py - (a.Y + d.Y * t);
                    if (qx * qx + qy * qy <= r2) Cells[gy * W + gx] = value;
                }
            }
        }

        /// <summary>Связные куски маски по четырём соседям. Нужно ластику: стирающий мазок поперёк
        /// массива оставляет одно ПЯТНО из мазков, но ДВА куска массы, и дальше они обязаны жить
        /// каждый своей осью.</summary>
        public List<List<int>> Components()
        {
            var result = new List<List<int>>();
            var seen = new bool[W * H];
            var stack = new Stack<int>();

            for (int start = 0; start < Cells.Length; start++)
            {
                if (Cells[start] == 0 || seen[start]) continue;
                var component = new List<int>();
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    component.Add(i);
                    int x = i % W, y = i / W;
                    Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
                }
                result.Add(component);

                void Push(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) return;
                    int k = ny * W + nx;
                    if (Cells[k] == 0 || seen[k]) return;
                    seen[k] = true;
                    stack.Push(k);
                }
            }
            return result;
        }
    }
}
