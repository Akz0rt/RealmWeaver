using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// Рисует горы ТЕМИ ЖЕ правилами, что и меш приложения: тело ярусами, линия штрихами, крошка в
    /// тени, промоины на свету. Слой Rendering стенду не виден, но все решения, в которых можно
    /// ошибиться, вынесены в чистый слой (MountainInk, MountainTriangulation) — значит картинку
    /// можно собрать и здесь, и она будет той же.
    ///
    /// Нужна ровно затем, чтобы посмотреть глазами до того, как ДМ откроет Unity. Компилятор
    /// говорит, что код собирается; проверки говорят, что числа сходятся; а похоже ли это на
    /// нарисованную гору — не скажет ни тот, ни другой.
    /// </summary>
    static class PenPreview
    {
        const float W = 1100f, H = 820f;

        // Числа ДМ от 14 августа 2026.
        const float PenWidth = 0.15f, PenTaper = 1.1f, PenAlpha = 0.61f;
        const float Grit = 0.88f, GritFall = 0.4f, Gully = 0.32f;
        const float Light = 160f, TierInk = 0.5f, TierContrast = 1f;

        public static void Write(string path)
        {
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W)}' height='{F(H)}' ")
              .Append($"viewBox='0 0 {F(W)} {F(H)}'>");
            sb.Append("<rect width='100%' height='100%' fill='#cdbb92'/>");
            // Пятна биомов — видно, как тело берёт цвет карты и как граница проходит сквозь гору.
            sb.Append("<ellipse cx='300' cy='230' rx='260' ry='190' fill='#8d9d70'/>");
            sb.Append("<ellipse cx='820' cy='560' rx='250' ry='200' fill='#9fb4a6'/>");

            Draw(sb, 20f, "гряда", Band(new Vector2(120, 560), new Vector2(520, 180), 40f));
            Draw(sb, 20f, "массив", Disc(new Vector2(800, 300), 150f));

            sb.Append("</svg>");
            System.IO.File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"готово: {path}");
        }

        static void Draw(StringBuilder sb, float radius, string title,
                         List<IReadOnlyList<Vector2>> polys)
        {
            var settings = new MountainSettings { Radius = radius };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(radius, radius));
            if (mask == null) return;
            mask.Smooth((int)Math.Round(0.5f * radius / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out _);
            var profile = settings.Profile();

            var verts = new List<Vector2>();
            var tris = new List<int>();
            foreach (var shape in shapes)
            {
                float density = MountainInk.Density(shape.Tier, settings.Tiers, TierContrast, TierInk);
                Body(sb, shape, profile, verts, tris);
                GritMarks(sb, shape, profile, radius, density);
                Gullies(sb, shape, profile);
                Outline(sb, shape, profile, radius, density);
            }
            Console.WriteLine($"  {title}: гор {shapes.Count}");
        }

        /// <summary>Тело — та же раскладка, что уходит в меш. Цвет тела здесь один: снимка карты у
        /// стенда нет, а вопрос картинки — линия, а не заливка.</summary>
        static void Body(StringBuilder sb, MountainShape shape, LiftSamples profile,
                         List<Vector2> verts, List<int> tris)
        {
            MountainTriangulation.Fill(shape, profile, verts, tris);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                sb.Append("<polygon fill='#c6bb8e' points='");
                for (int k = 0; k < 3; k++)
                {
                    var p = verts[tris[i + k]];
                    sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                }
                sb.Append("'/>");
            }
        }

        /// <summary>Линия — короткие штрихи в точках границы, по касательной к своему ярусу.</summary>
        static void Outline(StringBuilder sb, MountainShape shape, LiftSamples profile,
                            float radius, float density)
        {
            for (int i = 0; i < shape.Silhouette.Length; i++)
            {
                float half = 0.5f * PenWidth * radius
                           * MountainInk.Taper(shape.SilhouetteR[i], PenTaper) * density;
                if (half < 1e-3f) continue;

                MountainTriangulation.Dash(shape, i, profile, out Vector2 dir, out float reach);
                if (reach <= 0f) continue;

                Vector2 side = new Vector2(-dir.Y, dir.X) * half;
                Vector2 along = dir * reach;
                Vector2 p = shape.Silhouette[i];
                Quad(sb, p - along - side, p - along + side, p + along + side, p + along - side, PenAlpha);
            }

            // Обводка склона на скачке границы — то же правило, что в меше.
            int n = shape.SilhouetteR.Length;
            for (int i = 0; i < n; i++)
            {
                if (MountainTriangulation.Jump(shape, i) < MountainTriangulation.JumpThreshold) continue;
                int j = (i + 1) % n;
                float ra = shape.SilhouetteR[i], rb = shape.SilhouetteR[j];
                float hi = Math.Max(ra, rb), lo = Math.Min(ra, rb);
                int edge = ra > rb ? i : j;

                var prev = MountainTriangulation.Meridian(shape, edge, hi, profile);
                float prevHalf = 0.5f * PenWidth * radius * MountainInk.Taper(hi, PenTaper) * density;
                for (int step = 1; step <= 6; step++)
                {
                    float r = hi + (lo - hi) * step / 6f;
                    var next = MountainTriangulation.Meridian(shape, edge, r, profile);
                    float half = 0.5f * PenWidth * radius * MountainInk.Taper(r, PenTaper) * density;
                    var d = Vector2.Normalize(next - prev);
                    var nr = new Vector2(-d.Y, d.X);
                    Quad(sb, prev - nr * prevHalf, prev + nr * prevHalf,
                             next + nr * half, next - nr * half, PenAlpha);
                    prev = next; prevHalf = half;
                }
            }
        }

        static void GritMarks(StringBuilder sb, MountainShape shape, LiftSamples profile,
                              float radius, float density)
        {
            int total = MountainInk.MarkCount(shape.FootArea, radius, Grit, density, out _);
            var light = LightVec();
            float size = 0.045f * radius;
            int n = shape.Base.Length;

            for (int id = 1; id <= total; id++)
            {
                int i = (int)(Rand(shape.Seed, 7u, id) * n);
                if (i >= n) i = n - 1;
                if (MountainInk.Shade(shape.Normal[i], light) <= 0.05f) continue;
                if (Rand(shape.Seed, 11u, id) > Math.Min(1f, MountainInk.Shade(shape.Normal[i], light) * 1.6f))
                    continue;

                float r = MountainInk.MarkR(Rand(shape.Seed, 3u, id), GritFall);
                var p = MountainTriangulation.Meridian(shape, i, r, profile);
                var q = MountainTriangulation.Meridian(shape, (i + 1) % n, r, profile);
                float u = Rand(shape.Seed, 211u, id);
                p = new Vector2(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u);

                float s = size * (0.6f + Rand(shape.Seed, 307u, id));
                sb.Append("<polygon fill='rgba(26,22,18,").Append(F(PenAlpha)).Append(")' points='")
                  .Append(F(p.X - s)).Append(',').Append(F(Flip(p.Y - s * 0.6f))).Append(' ')
                  .Append(F(p.X + s)).Append(',').Append(F(Flip(p.Y - s * 0.6f))).Append(' ')
                  .Append(F(p.X)).Append(',').Append(F(Flip(p.Y + s * 0.8f))).Append("'/>");
            }
        }

        static void Gullies(StringBuilder sb, MountainShape shape, LiftSamples profile)
        {
            var light = LightVec();
            int n = shape.Normal.Length;
            for (int i = 0; i < n; i++)
            {
                if (shape.Normal[i].X * light.X + shape.Normal[i].Y * light.Y <= 0.25f) continue;
                if (Rand(shape.Seed, 613u, i) > Gully * 0.3f) continue;

                float top = 0.25f + Rand(shape.Seed, 71u, i) * 0.35f;
                float bottom = Math.Min(0.95f, top + 0.3f + Rand(shape.Seed, 89u, i) * 0.3f);
                sb.Append("<polyline fill='none' stroke='rgba(26,22,18,0.37)' stroke-width='1' points='");
                for (int step = 0; step <= 4; step++)
                {
                    float r = top + (bottom - top) * step / 4f;
                    var p = MountainTriangulation.Meridian(shape, i, r, profile);
                    sb.Append(F(p.X)).Append(',').Append(F(Flip(p.Y))).Append(' ');
                }
                sb.Append("'/>");
            }
        }

        static void Quad(StringBuilder sb, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float alpha)
        {
            sb.Append("<polygon fill='rgba(26,22,18,").Append(F(alpha)).Append(")' points='")
              .Append(F(a.X)).Append(',').Append(F(Flip(a.Y))).Append(' ')
              .Append(F(b.X)).Append(',').Append(F(Flip(b.Y))).Append(' ')
              .Append(F(c.X)).Append(',').Append(F(Flip(c.Y))).Append(' ')
              .Append(F(d.X)).Append(',').Append(F(Flip(d.Y))).Append("'/>");
        }

        static Vector2 LightVec()
        {
            float a = Light * (float)Math.PI / 180f;
            return new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
        }

        static float Rand(uint seed, uint salt, int id)
        {
            unchecked
            {
                uint h = seed + salt * 668265263u + (uint)id * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) >> 8) / 16777216f;
            }
        }

        static List<IReadOnlyList<Vector2>> Band(Vector2 a, Vector2 b, float half)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            var d = Vector2.Normalize(b - a);
            var nrm = new Vector2(-d.Y, d.X);
            float length = (b - a).Length();
            for (float s = 0f; s <= length; s += 15f)
                for (float o = -half; o <= half; o += 15f)
                    polys.Add(Cell(a + d * s + nrm * o));
            return polys;
        }

        static List<IReadOnlyList<Vector2>> Disc(Vector2 centre, float radius)
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            for (float x = -radius; x <= radius; x += 15f)
                for (float y = -radius; y <= radius; y += 15f)
                {
                    var q = new Vector2(centre.X + x, centre.Y + y);
                    if ((q - centre).Length() > radius) continue;
                    polys.Add(Cell(q));
                }
            return polys;
        }

        static IReadOnlyList<Vector2> Cell(Vector2 c) => new List<Vector2>
        {
            new Vector2(c.X - 7.5f, c.Y - 7.5f), new Vector2(c.X + 7.5f, c.Y - 7.5f),
            new Vector2(c.X + 7.5f, c.Y + 7.5f), new Vector2(c.X - 7.5f, c.Y + 7.5f),
        };

        static float Flip(float y) => H - y;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
