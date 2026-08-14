using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// §10 «Гора как стопка ярусов»: доля-гармошка — это план, вид сверху; гора строится над ней как
    /// объём. Вся геометрия задана одним соглашением о взгляде: смотрим горизонтально, земля видна
    /// под углом, высота откладывается прямо вверх по экрану (у нас — по +Y в Site-координатах).
    ///
    /// Подошва — та же доля, с двумя преобразованиями относительно середины звена: ×k вдоль оси и
    /// ×1/a по вертикали. Сжатие по вертикали — тот же угол взгляда, из-за которого позвонки чаще
    /// идут по вертикали; без него гора сидит на круглом блине и читается как палатка. Растяжение
    /// вдоль оси заставляет подошвы соседей перекрываться: склоны пересекаются ВЫШЕ земли, и это
    /// перевал — цепь читается как гряда, а не как ряд кучек.
    ///
    /// Ярус r — та же подошва, стянутая к середине звена в r раз и поднятая на H·lift(r). Здесь не
    /// строится ни одного яруса: сразу считается ВНЕШНЯЯ ГРАНИЦА их объединения — см. Silhouette.
    ///
    /// Ушли вместе с прежним силуэтом: гребень и дуга подошвы, MakeMonotone, ClampUnderCrest,
    /// показатель склона и весь класс самопересечений. Полоса между двумя ярусами сшивается зипом,
    /// самопересечься ей нечем.
    /// </summary>
    public static class MoundBuilder
    {
        /// <summary>Сколько значений r перебирается при поиске границы. Двух десятков хватает: сама
        /// граница потом уточняется параболой по лучшей тройке.</summary>
        public const int ProfileSamples = 24;

        /// <summary>
        /// Гора над звеном. outline — замкнутый силуэт доли (LinkOutline.Build).
        /// stretchBack/stretchFwd — растяжение подошвы вдоль оси назад и вперёд (k из §10); у
        /// свободного конца ставится 1. minSpan — ширина подошвы, ниже которой горы нет.
        /// profile — заранее посчитанная выборка кривой подъёма (одна на весь пересчёт).
        /// Возвращает null, если строить нечего.
        /// </summary>
        public static MountainShape Build(IReadOnlyList<Vector2> outline, AxisLink link,
                                          float heightFactor, float squash,
                                          float stretchBack, float stretchFwd, float minSpan,
                                          LiftSamples profile, float jag = 0f, float tolerance = 0.2f)
        {
            if (outline == null || outline.Count < 6 || link == null) return null;
            if (profile == null || profile.Count < 2) return null;

            int n = outline.Count;
            Vector2 c = link.Mid, u = link.Tan;
            Vector2 across = new Vector2(-u.Y, u.X);
            uint seed = SeedAt(c);

            // Подошва: вдоль оси — растяжение (каждая сторона своим множителем), поперёк — ничего,
            // поэтому ширина горы остаётся ровно той, что дало поле расстояний, и силуэт не теряет
            // связи с формой мазка. Затем всё сплющивается по вертикали относительно середины.
            var foot = new Vector2[n];
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = outline[i] - c;
                float a = d.X * u.X + d.Y * u.Y;
                a *= a >= 0f ? stretchFwd : stretchBack;
                float t = d.X * across.X + d.Y * across.Y;

                // Выщербина множит отступ, а не сдвигает точку: ярус стягивает подошву к середине
                // звена ЦЕЛИКОМ, значит выщербина стягивается вместе с ним и вложенность цела.
                float k = Jag(seed, i, n, jag);
                Vector2 p = c + u * (a * k) + across * (t * k);
                p = new Vector2(p.X, c.Y + (p.Y - c.Y) * squash);
                foot[i] = p;

                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (maxX - minX < minSpan) return null;

            float height = heightFactor * link.MidW * link.HeightJitter * link.TierScale;

            var silhouette = new Vector2[n];
            var silhouetteR = new float[n];
            var normals = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 d = foot[i] - c;
                Vector2 normal = OutwardNormal(foot, i, c);
                normals[i] = normal;
                float r = BoundaryR(d, normal, height, profile);
                silhouetteR[i] = r;
                silhouette[i] = new Vector2(c.X + d.X * r,
                                            c.Y + d.Y * r + height * profile.LiftAt(r));
            }

            return new MountainShape
            {
                LevelR = Levels(height, profile, tolerance),
                Base = foot,
                Normal = normals,
                Centre = c,
                Height = height,
                Sharp = profile.Sharp,
                Silhouette = silhouette,
                SilhouetteR = silhouetteR,
                Depth = minY,
                Tier = link.Tier,
                Seed = seed,
                FootArea = Math.Max(1e-4f, (maxX - minX) * (maxY - minY)),
            };
        }


        /// <summary>
        /// Сколько ярусов нужно заливке — и каких.
        ///
        /// Ярусы существуют затем, чтобы огибающая стопки вышла гладкой. Насколько она гладкая,
        /// решает ровно одна величина: отклонение прямой полосы между двумя ярусами от настоящего
        /// меридиана. А оно, что приятно, считается в одну строчку и НЕ ЗАВИСИТ ОТ ЛУЧА:
        ///
        ///     точка(r) = c + r·d + (0, H·lift(r))
        ///
        /// у середины отрезка [a, b] горизонтальная часть совпадает с серединой хорды тождественно
        /// (она линейна по r), и разница остаётся только по вертикали: H·|lift(m) − (lift(a)+lift(b))/2|.
        ///
        /// Отсюда: делим пополам, пока отклонение не станет меньше допуска. У конуса меридиан
        /// ПРЯМОЙ, отклонение нулевое, и хватает двух ярусов — подошвы и вершины. У купола нужен
        /// десяток. Прежде число ярусов было ползунком на 18, и восемнадцать колец платились за
        /// каждую гору независимо от того, нужны они или нет.
        /// </summary>
        public static float[] Levels(float height, LiftSamples profile, float tolerance)
        {
            var list = new List<float> { 1f };
            Subdivide(list, 1f, MountainProfile.ApexRadius, height, profile,
                      Math.Max(1e-4f, tolerance), 0);
            list.Add(MountainProfile.ApexRadius);
            return list.ToArray();
        }

        const int MaxLevelDepth = 6;

        static void Subdivide(List<float> list, float hi, float lo, float height,
                              LiftSamples profile, float tolerance, int depth)
        {
            float mid = 0.5f * (hi + lo);
            float chord = 0.5f * (profile.LiftAt(hi) + profile.LiftAt(lo));
            float error = Math.Abs(height * (profile.LiftAt(mid) - chord));
            if (error <= tolerance || depth >= MaxLevelDepth) return;

            Subdivide(list, hi, mid, height, profile, tolerance, depth + 1);
            list.Add(mid);
            Subdivide(list, mid, lo, height, profile, tolerance, depth + 1);
        }

        /// <summary>
        /// На каком r внешняя граница стопки касается луча i.
        ///
        /// Ярус r кладёт точку в c + r·d + (0, H·lift(r)). Её вынос за край стопки меряется
        /// проекцией на нормаль подошвы в этой точке:
        ///
        ///     g(r) = r·(d·n) + H·lift(r)·n_y
        ///
        /// Максимум g и есть граница. Это не приближение: производная g даёт
        /// lift'(r) = −(d·n)/(H·n_y), а это в точности условие огибающей семейства ярусов
        /// (∂Q/∂i × ∂Q/∂r = 0) — проверяется подстановкой n = (d'_y, −d'_x)/|d'|.
        ///
        /// У ближнего края n_y &lt; 0, второе слагаемое растёт с r, максимум всегда на r = 1 — граница
        /// садится на саму подошву, и линия туши там нулевой толщины. Поэтому подошва не обводится
        /// сама собой, без единой проверки: ровно то, чего требовал образец ДМ.
        /// </summary>
        static float BoundaryR(Vector2 d, Vector2 normal, float height, LiftSamples profile)
        {
            float along = d.X * normal.X + d.Y * normal.Y;
            float up = height * normal.Y;

            int best = profile.Count - 1;          // r = 1: подошва, всегда допустимый ответ
            float bestValue = float.NegativeInfinity;
            for (int k = 0; k < profile.Count; k++)
            {
                float value = profile.R[k] * along + profile.Lift[k] * up;
                if (value > bestValue) { bestValue = value; best = k; }
            }

            // Уточнение параболой по лучшей тройке: без него граница ступенчато скачет между
            // выборками, и на пологой горе это видно гранёностью силуэта.
            if (best > 0 && best < profile.Count - 1)
            {
                float y0 = profile.R[best - 1] * along + profile.Lift[best - 1] * up;
                float y1 = bestValue;
                float y2 = profile.R[best + 1] * along + profile.Lift[best + 1] * up;
                float denom = y0 - 2f * y1 + y2;
                if (Math.Abs(denom) > 1e-9f)
                {
                    float shift = 0.5f * (y0 - y2) / denom;
                    if (shift > -1f && shift < 1f)
                    {
                        float step = shift < 0f
                            ? profile.R[best] - profile.R[best - 1]
                            : profile.R[best + 1] - profile.R[best];
                        return Clamp(profile.R[best] + shift * Math.Abs(step),
                                     MountainProfile.ApexRadius, 1f);
                    }
                }
            }
            return profile.R[best];
        }

        /// <summary>Наружу глядящая нормаль подошвы в точке i. Направление сверяется с лучом из
        /// середины звена: у доли-гармошки обход может оказаться любым, а нормаль внутрь дала бы
        /// границу, вывернутую наизнанку.</summary>
        static Vector2 OutwardNormal(Vector2[] loop, int i, Vector2 centre)
        {
            int n = loop.Length;
            Vector2 a = loop[(i - 1 + n) % n], b = loop[(i + 1) % n];
            Vector2 t = b - a;
            if (t.LengthSquared() < 1e-12f) t = loop[i] - centre;
            if (t.LengthSquared() < 1e-12f) return new Vector2(0f, 1f);

            Vector2 normal = Vector2.Normalize(new Vector2(t.Y, -t.X));
            Vector2 outward = loop[i] - centre;
            if (normal.X * outward.X + normal.Y * outward.Y < 0f) normal = -normal;
            return normal;
        }

        /// <summary>
        /// Выщербина в точке i: множитель отступа от середины звена.
        ///
        /// Волна длиной в семь точек плюс мелкая дрожь. Чистый шум по каждой точке даёт не камень, а
        /// мех — это видно сразу. Длина волны задана ЧИСЛОМ точек, а не долей кольца, и это не
        /// описка: шаг выборки силуэта равен R/15, а длина звена растёт с R, поэтому точек на
        /// кольцо выходит одинаково при любом радиусе (замерено 64 при R = 10 и 66 при R = 26).
        /// Значит «зубчатость 0,03» означает одно и то же на мелких и крупных горах.
        /// </summary>
        public static float Jag(uint seed, int i, int count, float amount)
        {
            if (amount <= 0f || count <= 0) return 1f;

            const float Wave = 7f;
            float g = i / Wave;
            int i0 = (int)Math.Floor(g);
            float t = g - i0;
            float a = Noise(seed, i0), b = Noise(seed, i0 + 1);
            float f = t * t * (3f - 2f * t);
            float wave = a + (b - a) * f - 0.5f;
            float fine = Noise(seed ^ 0x9E3779B9u, i) - 0.5f;
            float k = 1f + amount * (wave * 2.2f + fine * 0.5f);
            return k < 0.15f ? 0.15f : k;
        }

        static float Noise(uint seed, int i)
        {
            unchecked
            {
                uint h = seed + (uint)i * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) >> 8) / 16777216f;
            }
        }

        /// <summary>Зерно рисунка горы — от её МЕСТА на карте, а не от порядкового номера. Иначе
        /// крошка пересеивается при каждой перерисовке и картинка дрожит, а соседний массив,
        /// нарисованный позже, молча перетасовывает уже нарисованный.</summary>
        public static uint SeedAt(Vector2 p)
        {
            unchecked
            {
                uint x = (uint)(int)Math.Round(p.X * 8f);
                uint y = (uint)(int)Math.Round(p.Y * 8f);
                uint h = x * 374761393u + y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }

    /// <summary>
    /// Выборка кривой подъёма для одной остроты. Считается ОДИН раз на пересчёт и переиспользуется
    /// всеми горами: сама кривая от горы не зависит, а Acos и Pow внутри неё стоят заметно дороже
    /// умножения. Без этого поиск границы звал бы их под миллион раз за мазок.
    /// </summary>
    public sealed class LiftSamples
    {
        public readonly float Sharp;
        public readonly float[] R;
        public readonly float[] Lift;

        public int Count => R.Length;

        public LiftSamples(float sharp, int count)
        {
            Sharp = sharp;
            int n = Math.Max(2, count);
            R = new float[n];
            Lift = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float r = MountainProfile.ApexRadius + (1f - MountainProfile.ApexRadius) * t;
                R[i] = r;
                Lift[i] = MountainProfile.Lift(sharp, r);
            }
        }

        /// <summary>Подъём в произвольной точке — по той же выборке, линейно между узлами. Считать
        /// заново нельзя: уточнённое параболой r обязано лечь на ту же кривую, по которой искали
        /// максимум, иначе точка съезжает с границы.</summary>
        public float LiftAt(float r)
        {
            float lo = R[0], hi = R[Count - 1];
            if (r <= lo) return Lift[0];
            if (r >= hi) return Lift[Count - 1];

            float t = (r - lo) / (hi - lo) * (Count - 1);
            int i = (int)t;
            if (i >= Count - 1) return Lift[Count - 1];
            float f = t - i;
            return Lift[i] + (Lift[i + 1] - Lift[i]) * f;
        }
    }
}
