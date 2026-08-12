using System;
using System.Collections.Generic;
using System.Numerics;
using WorldGen.Generation.Mountains;

namespace MountainHarness
{
    /// <summary>
    /// Инварианты слоя гор. Каждая проверка построена так, чтобы НЕПРАВИЛЬНОЕ правило дало другой
    /// ответ, а не тот же самый: фикстура выбрана под конкретного мутанта, и мутант назван в
    /// комментарии. Проверка, которую нельзя провалить порчей кода, здесь не нужна.
    /// </summary>
    static class Program
    {
        static int failures;

        static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "checks";
            if (mode == "svg") { Preview.Write("peek.svg"); return 0; }

            WaistProfile();
            SharedWidthAtVertebra();
            ChainsShareEnds();
            MonotoneAlongGentleLink();
            ApexHeight();
            FreeEndStretch();
            NearVerticalLinkIsKnownArtifact();

            Console.WriteLine(failures == 0 ? "NO ERRORS" : $"{failures} ERROR(S)");
            return failures == 0 ? 0 : 1;
        }

        // ── §9 «Гармошка» ───────────────────────────────────────────────────────────────────────

        /// <summary>На позвонке доля перетянута до t·w, в середине звена раздута до полной w.
        /// Мутант: убрать талию (профиль = 1) — концы станут шириной w вместо 0.55·w.</summary>
        static void WaistProfile()
        {
            var link = Horizontal(100f, 20f);
            var outline = LinkOutline.Build(link, 0.55f, 20f / 15f);
            if (outline == null) { Fail("Гармошка", "силуэт не построен"); return; }

            int half = outline.Count / 2;
            float atStart = Math.Abs(outline[0].Y - link.Pts[0].Y);
            float atMiddle = Math.Abs(outline[half / 2].Y - link.Mid.Y);

            Check("Гармошка: талия на позвонке", Near(atStart, 0.55f * 20f, 0.2f), $"полуширина на конце {atStart:0.##}, ждали {0.55f * 20f:0.##}");
            Check("Гармошка: полная ширина в середине", Near(atMiddle, 20f, 0.4f), $"полуширина в середине {atMiddle:0.##}, ждали 20");
        }

        /// <summary>Соседние доли на общем позвонке имеют одинаковую полуширину — иначе силуэт рвётся
        /// ступенькой. Мутант: сделать профиль несимметричным (например sin(π·u/2)) — конец звена
        /// перестанет совпадать с началом следующего.</summary>
        static void SharedWidthAtVertebra()
        {
            var a = Horizontal(100f, 20f);
            var b = Horizontal(100f, 20f);
            b.Pts = new List<Vector2> { new Vector2(100, 0), new Vector2(200, 0) };
            b.Mid = new Vector2(150, 0);

            var oa = LinkOutline.Build(a, 0.55f, 20f / 15f);
            var ob = LinkOutline.Build(b, 0.55f, 20f / 15f);
            if (oa == null || ob == null) { Fail("Стык звеньев", "силуэт не построен"); return; }

            float endOfA = Math.Abs(oa[oa.Count / 2 - 1].Y);
            float startOfB = Math.Abs(ob[0].Y);
            Check("Стык звеньев: ширина совпадает", Near(endOfA, startOfB, 0.05f), $"{endOfA:0.###} против {startOfB:0.###}");
        }

        // ── §10 «Горы над звеньями» ─────────────────────────────────────────────────────────────

        /// <summary>Гребень и ближняя дуга подошвы обязаны начинаться и кончаться в одних точках:
        /// на них держится сшивка треугольников. Мутант: развернуть одну из цепей — концы разойдутся
        /// на всю ширину подошвы.</summary>
        static void ChainsShareEnds()
        {
            var shape = Mound(Horizontal(100f, 20f), 1.4f, 1.4f);
            if (shape == null) { Fail("Цепи", "гора не построена"); return; }

            bool left = (shape.Crest[0] - shape.Front[0]).Length() < 0.01f;
            bool right = (shape.Crest[shape.Crest.Count - 1] - shape.Front[shape.Front.Count - 1]).Length() < 0.01f;
            Check("Цепи: общий левый конец", left, $"{shape.Crest[0]} против {shape.Front[0]}");
            Check("Цепи: общий правый конец", right, $"{shape.Crest[shape.Crest.Count - 1]} против {shape.Front[shape.Front.Count - 1]}");
        }

        /// <summary>У пологого звена обе цепи монотонны по X — на этом стоит сшивка полосой в
        /// MountainMeshBuilder. Мутант: выбрать дальнюю дугу вместо ближней — она пойдёт по X
        /// в обратную сторону.</summary>
        static void MonotoneAlongGentleLink()
        {
            var shape = Mound(Horizontal(100f, 20f), 1.4f, 1.4f);
            if (shape == null) { Fail("Монотонность", "гора не построена"); return; }
            Check("Монотонность: гребень по X", IsMonotone(shape.Crest), "гребень виляет по X");
            Check("Монотонность: подошва по X", IsMonotone(shape.Front), "ближняя дуга виляет по X");
        }

        /// <summary>Вершина стоит ровно на h·w·разброс выше середины звена. Мутант: взять полуширину
        /// на конце звена вместо середины — высота уедет в 0.55 раза.</summary>
        static void ApexHeight()
        {
            var link = Horizontal(100f, 20f);
            link.HeightJitter = 1f;
            link.TierScale = 1f;
            var shape = Mound(link, 1.4f, 1.4f);
            if (shape == null) { Fail("Высота", "гора не построена"); return; }

            float expected = 2.2f * 20f;
            float actual = shape.Apex.Y - link.Mid.Y;
            Check("Высота: H = h·w", Near(actual, expected, 0.01f), $"{actual:0.##} вместо {expected:0.##}");
        }

        /// <summary>У свободного конца подошва НЕ растягивается: соседа с этой стороны нет, и
        /// растяжение только вылезло бы за нарисованный мазок (§14). Мутант: растягивать обе стороны
        /// одинаково — левый вылет станет равен правому.</summary>
        static void FreeEndStretch()
        {
            var link = Horizontal(100f, 20f);
            var plain = Mound(link, 1f, 1f);
            var oneSided = Mound(link, 1f, 1.4f);
            if (plain == null || oneSided == null) { Fail("Растяжение", "гора не построена"); return; }

            float leftPlain = link.Mid.X - plain.Crest[0].X;
            float leftOne = link.Mid.X - oneSided.Crest[0].X;
            float rightPlain = plain.Crest[plain.Crest.Count - 1].X - link.Mid.X;
            float rightOne = oneSided.Crest[oneSided.Crest.Count - 1].X - link.Mid.X;

            Check("Растяжение: свободный конец не растянут", Near(leftOne, leftPlain, 0.01f), $"{leftOne:0.##} вместо {leftPlain:0.##}");
            Check("Растяжение: внутренний конец растянут в k", Near(rightOne, rightPlain * 1.4f, 0.01f), $"{rightOne:0.##} вместо {rightPlain * 1.4f:0.##}");
        }

        /// <summary>ИЗВЕСТНЫЙ ИЗЪЯН, зафиксированный замером, а не проверка правила.
        ///
        /// Если звено одновременно ИЗОГНУТО и идёт близко к вертикали, ближняя дуга его подошвы
        /// заворачивается назад по X — перестаёт быть монотонной. На кольце такими выходят 9 гор из
        /// 15 (замер: `dotnet run [SEP] svg`). Прямая вертикальная ось этим не страдает, изогнутая
        /// пологая — тоже; нужно именно сочетание.
        ///
        /// Чем это грозит: MountainMeshBuilder сшивает тело горы полосой между двумя цепями и
        /// опирается на монотонность. На такой подошве он выдаёт налезающие треугольники. Сейчас
        /// они не видны (заливка сплошная, Cull Off), но проступят, как только появится штриховка,
        /// боковой свет или полупрозрачность. Фикс — в задаче 5, вместе с триангуляцией.
        ///
        /// Проверка держит факт под замком: перестанет проходить — значит изъян починили или он
        /// изменил природу, и то и другое надо заметить, а не пропустить.</summary>
        static void NearVerticalLinkIsKnownArtifact()
        {
            // Звено-дуга у «бока» кольца: касательная почти вертикальна, сама дуга заметно изогнута.
            var pts = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                double a = Math.PI * (0.5 - 0.16 * i / 8.0);   // около верхней точки окружности
                pts.Add(new Vector2(150f * (float)Math.Sin(a), 150f * (float)Math.Cos(a)));
            }
            var link = new AxisLink { Pts = pts, MidW = 20f };
            for (int i = 0; i < pts.Count; i++) link.Ws.Add(20f);
            link.Mid = pts[pts.Count / 2];
            Vector2 dir = pts[pts.Count - 1] - pts[0];
            link.Tan = Vector2.Normalize(dir);

            var shape = Mound(link, 1.4f, 1.4f);
            if (shape == null) { Fail("Изогнутое вертикальное звено", "гора не построена"); return; }

            Check("Изогнутое вертикальное звено: подошва заворачивается (ждём фикса в задаче 5)",
                  !IsMonotone(shape.Front),
                  "подошва стала монотонной — похоже, изъян починили: перепиши проверку под новое правило");
        }

        // ── мелочь ──────────────────────────────────────────────────────────────────────────────

        static AxisLink Horizontal(float length, float halfWidth)
        {
            var link = new AxisLink
            {
                Pts = new List<Vector2> { new Vector2(0, 0), new Vector2(length, 0) },
                Mid = new Vector2(length * 0.5f, 0),
                Tan = new Vector2(1, 0),
                MidW = halfWidth,
            };
            for (int i = 0; i < 2; i++) link.Ws.Add(halfWidth);
            return link;
        }

        static MountainShape Mound(AxisLink link, float back, float fwd)
        {
            var outline = LinkOutline.Build(link, 0.55f, link.MidW / 15f);
            return outline == null ? null
                 : MoundBuilder.Build(outline, link, 2.2f, 1f / 1.6f, back, fwd, link.MidW * 0.1f);
        }

        static bool IsMonotone(List<Vector2> pts)
        {
            for (int i = 1; i < pts.Count; i++)
                if (pts[i].X < pts[i - 1].X - 1e-3f) return false;
            return true;
        }

        static bool Near(float a, float b, float eps) => Math.Abs(a - b) <= eps;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) Console.WriteLine($"  PASS  {name}");
            else Fail(name, detail);
        }

        static void Fail(string name, string detail)
        {
            failures++;
            Console.WriteLine($"  FAIL  {name}: {detail}");
        }
    }
}
