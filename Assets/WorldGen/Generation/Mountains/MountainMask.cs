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

        /// <summary>Мир по координате сетки. Целая координата — ЦЕНТР ячейки: именно в центре
        /// растеризация решает, закрашена ли ячейка, и именно там поле расстояний считает своё
        /// значение. Без этих полклетки нарисованный массив съезжал бы от мазка на полшага сетки.</summary>
        public Vector2 GridToWorld(float gx, float gy)
            => new Vector2(Ox + (gx + 0.5f) * Cell, Oy + (gy + 0.5f) * Cell);

        public Vector2 WorldToGrid(Vector2 p)
            => new Vector2((p.X - Ox) / Cell - 0.5f, (p.Y - Oy) / Cell - 0.5f);

        public bool At(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && Cells[y * W + x] != 0;

        /// <summary>
        /// Растеризует пятно из МАЗКОВ. desiredCell — желаемый шаг (ChooseCell), maxCells — потолок
        /// на число ячеек: если не влезает, шаг увеличивается, пока не влезет.
        ///
        /// ВНИМАНИЕ: с 2026-08-14 это путь СТЕНДА, а не приложения. Источником гор стал рельеф, и
        /// приложение строит маску из многоугольников клеток карты (FromPolygons). Мазок остался
        /// удобной заготовкой для проверок — «масса в форме проведённой линии» описывается одной
        /// строкой, — но кисть в него больше не пишет и в файле его нет. Не подключай обратно как
        /// живой путь, не прочитав раздел «Разворот» в плане арки.
        /// </summary>
        public static MountainMask Build(MountainBlob blob, float desiredCell, int maxCells = 4_000_000)
            => Build(blob, desiredCell, null, maxCells);

        /// <summary>
        /// Тот же расчёт с ограничением по суше: ячейка, чей ЦЕНТР не на суше, гаснет. Считается
        /// после всех мазков и ластиков — вода тут ровно такой же вычитающий штамп, только чужой.
        /// isLand = null означает «ограничения нет».
        /// </summary>
        public static MountainMask Build(MountainBlob blob, float desiredCell,
                                         Func<Vector2, bool> isLand, int maxCells = 4_000_000)
        {
            if (blob == null || blob.Strokes.Count == 0) return null;

            StrokeGeometry.Bounds(blob.Strokes, out float minX, out float minY, out float maxX, out float maxY);
            if (float.IsInfinity(minX)) return null;

            var mask = Allocate(minX, minY, maxX, maxY, desiredCell, maxCells);

            foreach (var stroke in blob.Strokes) mask.Stamp(stroke, 1);
            foreach (var eraser in blob.Erasers) mask.Stamp(eraser, 0);
            if (isLand != null) mask.ClipToLand(isLand);
            return mask;
        }

        /// <summary>
        /// Растеризует массив, заданный МНОГОУГОЛЬНИКАМИ КЛЕТОК КАРТЫ. Это главный путь с 2026-08-14:
        /// источник гор — рельеф, а рельеф живёт в клетках, поэтому масса приходит сюда не мазком, а
        /// списком клеток, у которых высота дотянула до «горы».
        ///
        /// Заливка идёт по каждому многоугольнику отдельно (правило чётности), а не «по объединению»:
        /// клетки Вороного стыкуются ребро в ребро, общие вершины у них ОДНИ И ТЕ ЖЕ, поэтому
        /// пересечения строки с общим ребром считаются у соседей одинаково и щели по шву не
        /// возникает. Волосяные щели от округления добирает сглаживание (Smooth), которое всё равно
        /// нужно: контур из клеток без него — мозаика с углами по 15 единиц при горе в 10.
        /// </summary>
        public static MountainMask FromPolygons(IReadOnlyList<IReadOnlyList<Vector2>> polygons,
                                                float desiredCell, int maxCells = 4_000_000)
        {
            if (polygons == null || polygons.Count == 0) return null;

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (var poly in polygons)
            {
                if (poly == null) continue;
                foreach (var p in poly)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
            if (float.IsInfinity(minX)) return null;

            var mask = Allocate(minX, minY, maxX, maxY, desiredCell, maxCells);
            foreach (var poly in polygons) mask.FillPolygon(poly);
            return mask;
        }

        /// <summary>Сетка по габаритам с полем и потолком на число ячеек. Общая для обоих входов —
        /// мазка и клеток карты.</summary>
        static MountainMask Allocate(float minX, float minY, float maxX, float maxY,
                                     float desiredCell, int maxCells)
        {
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
            return new MountainMask
            {
                W = w,
                H = h,
                Cell = cell,
                Ox = minX - PadCells * cell,
                Oy = minY - PadCells * cell,
                Cells = new byte[w * h],
            };
        }

        /// <summary>Заливка одного выпуклого или невыпуклого многоугольника по строкам сетки.</summary>
        void FillPolygon(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return;

            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (var p in poly) { if (p.Y < lo) lo = p.Y; if (p.Y > hi) hi = p.Y; }

            int gy0 = Math.Max(0, (int)Math.Ceiling((lo - Oy) / Cell - 0.5f));
            int gy1 = Math.Min(H - 1, (int)Math.Floor((hi - Oy) / Cell - 0.5f));
            var xs = new List<float>(8);

            for (int gy = gy0; gy <= gy1; gy++)
            {
                float py = Oy + (gy + 0.5f) * Cell;
                xs.Clear();
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Vector2 a = poly[j], b = poly[i];
                    // Полуоткрытое правило [min, max): вершина, ровно попавшая на строку, считается
                    // ОДИН раз, иначе чётность у соседних клеток разъедется и по шву пойдёт щель.
                    if ((a.Y <= py) == (b.Y <= py)) continue;
                    float t = (py - a.Y) / (b.Y - a.Y);
                    xs.Add(a.X + t * (b.X - a.X));
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int gx0 = Math.Max(0, (int)Math.Ceiling((xs[k] - Ox) / Cell - 0.5f));
                    int gx1 = Math.Min(W - 1, (int)Math.Floor((xs[k + 1] - Ox) / Cell - 0.5f));
                    for (int gx = gx0; gx <= gx1; gx++) Cells[gy * W + gx] = 1;
                }
            }
        }

        /// <summary>
        /// Сглаживание контура: ячейка остаётся в массе, если масса занимает больше половины окна
        /// радиуса radiusCells вокруг неё. Это скругляет углы мозаики из клеток карты и заодно
        /// затягивает волосяные щели по швам.
        ///
        /// Считается через суммы по прямоугольникам (интегральное изображение), поэтому цена не
        /// зависит от радиуса окна: иначе широкое окно на крупном массиве стоило бы больше, чем весь
        /// остальной конвейер.
        /// </summary>
        public void Smooth(int radiusCells)
        {
            if (radiusCells <= 0 || Cells == null) return;

            var sum = new int[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    sum[(y + 1) * (W + 1) + x + 1] = Cells[y * W + x]
                        + sum[y * (W + 1) + x + 1] + sum[(y + 1) * (W + 1) + x] - sum[y * (W + 1) + x];

            var next = new byte[W * H];
            for (int y = 0; y < H; y++)
            {
                int y0 = Math.Max(0, y - radiusCells), y1 = Math.Min(H - 1, y + radiusCells);
                for (int x = 0; x < W; x++)
                {
                    int x0 = Math.Max(0, x - radiusCells), x1 = Math.Min(W - 1, x + radiusCells);
                    int filled = sum[(y1 + 1) * (W + 1) + x1 + 1] - sum[y0 * (W + 1) + x1 + 1]
                               - sum[(y1 + 1) * (W + 1) + x0] + sum[y0 * (W + 1) + x0];
                    int total = (y1 - y0 + 1) * (x1 - x0 + 1);
                    next[y * W + x] = (byte)(filled * 2 > total ? 1 : 0);
                }
            }
            Cells = next;
        }

        /// <summary>Гасит всё, что не на суше. Спрашиваем только у закрашенных ячеек: пустых в
        /// маске большинство, а запрос стоит поиска ближайшей клетки карты.</summary>
        public void ClipToLand(Func<Vector2, bool> isLand)
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (Cells[i] == 0) continue;
                    if (!isLand(GridToWorld(x, y))) Cells[i] = 0;
                }
            }
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
