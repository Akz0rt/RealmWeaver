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
            if (mode == "axes") { Preview.WriteAxes("axes.svg"); return 0; }
            if (mode == "time") { TimeThinning(); return 0; }

            MaskMeasuresToSegment();
            EraserSubtracts();
            EraserSplitsMass();
            BlobsAreTransitive();
            EraserDoesNotMerge();
            SeedComesFromOldestStroke();
            DistanceIsExactlyEuclidean();
            LevelsStepByTwoR();
            ShallowRingIsRejected();
            GridSizeIsCapped();

            ThinningKeepsTheLoop();
            SpurIsPruned();
            BubbleCollapsesButDonutSurvives();
            ForkStitchesIntoOneAxis();
            ObtuseBranchDoesNotStealTheAxis();
            RingCoverageSuppressesSkeleton();
            ForkRescuesRingOverThinArms();
            ErasedGapSplitsAxesToo();

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

        // ── §2 «От мазка к пятну» ───────────────────────────────────────────────────────────────

        /// <summary>Маска мерит расстояние до ОТРЕЗКА, а не до его концов. Фикстура нарочно длинная:
        /// середина мазка от обоих концов далеко, и мутант «мерить до ближайшей точки ломаной»
        /// оставит там пусто.</summary>
        static void MaskMeasuresToSegment()
        {
            var blob = Blob(Stroke(1, 10f, new Vector2(0, 0), new Vector2(200, 0)));
            var mask = MountainMask.Build(blob, 1f);
            Check("Маска: середина длинного мазка закрашена", Filled(mask, new Vector2(100, 5)), "середина пуста");
            Check("Маска: снаружи радиуса пусто", !Filled(mask, new Vector2(100, 25)), "закрашено вне кисти");
        }

        /// <summary>Стирающий мазок вычитается из маски, а не добавляется в неё. Мутант: класть
        /// ластик тем же значением — середина останется закрашенной.</summary>
        static void EraserSubtracts()
        {
            var paint = Stroke(1, 10f, new Vector2(0, 0), new Vector2(200, 0));
            var eraser = Stroke(2, 20f, new Vector2(100, 0), new Vector2(100, 0));
            eraser.Erase = true;
            var blob = BlobBuilder.Group(new List<MountainStroke> { paint, eraser })[0];
            var mask = MountainMask.Build(blob, 1f);

            Check("Ластик: под ним пусто", !Filled(mask, new Vector2(100, 0)), "ластик не стёр");
            Check("Ластик: остальное на месте", Filled(mask, new Vector2(10, 0)), "стёрлось лишнее");
        }

        /// <summary>Стирающий мазок поперёк массива оставляет ОДНО пятно из мазков, но ДВА куска
        /// массы, и дальше они живут каждый своей осью. Мутант (проверен: убивает эту проверку) —
        /// класть ластик в маску как рисующий мазок: масса останется единой.</summary>
        static void EraserSplitsMass()
        {
            var paint = Stroke(1, 10f, new Vector2(0, 0), new Vector2(200, 0));
            var eraser = Stroke(2, 25f, new Vector2(100, -40), new Vector2(100, 40));
            eraser.Erase = true;
            var blob = BlobBuilder.Group(new List<MountainStroke> { paint, eraser })[0];
            var mask = MountainMask.Build(blob, 1f);

            int parts = mask.Components().Count;
            Check("Ластик: масса распалась надвое", parts == 2, $"кусков {parts}, ждали 2");
        }

        /// <summary>A задевает B, B задевает C, A и C далеко друг от друга — всё равно одно пятно.
        /// Мутант: слияние парами без системы непересекающихся множеств — получится два пятна, и
        /// какие именно, будет зависеть от порядка рисования.</summary>
        static void BlobsAreTransitive()
        {
            var a = Stroke(1, 10f, new Vector2(0, 0), new Vector2(50, 0));
            var b = Stroke(2, 10f, new Vector2(45, 0), new Vector2(105, 0));
            var c = Stroke(3, 10f, new Vector2(100, 0), new Vector2(150, 0));
            // Порядок нарочно перевёрнут: сначала крайние, связующий последним.
            var blobs = BlobBuilder.Group(new List<MountainStroke> { a, c, b });
            Check("Пятна: транзитивность", blobs.Count == 1, $"пятен {blobs.Count}, ждали 1");
        }

        /// <summary>Ластик, задевший два массива, НЕ объявляет их одним. Мутант: считать стирающий
        /// мазок обычным при группировке — два массива слипнутся в одно пятно, и общая ось пойдёт
        /// через пустоту между ними.</summary>
        static void EraserDoesNotMerge()
        {
            var left = Stroke(1, 10f, new Vector2(0, 0), new Vector2(50, 0));
            var right = Stroke(2, 10f, new Vector2(200, 0), new Vector2(250, 0));
            var eraser = Stroke(3, 10f, new Vector2(40, 0), new Vector2(210, 0));
            eraser.Erase = true;

            var blobs = BlobBuilder.Group(new List<MountainStroke> { left, right, eraser });
            bool bothGotIt = blobs.Count == 2 && blobs[0].Erasers.Count == 1 && blobs[1].Erasers.Count == 1;
            Check("Ластик: не сливает пятна", blobs.Count == 2, $"пятен {blobs.Count}, ждали 2");
            Check("Ластик: достался обоим пятнам", bothGotIt, "ластик приписан не всем задетым пятнам");
        }

        /// <summary>Зерно берётся от САМОГО СТАРОГО мазка пятна: дорисовка не должна перетасовать уже
        /// нарисованный хребет. Мутант: зерно от последнего мазка или от их числа — сместится при
        /// любой дорисовке.</summary>
        static void SeedComesFromOldestStroke()
        {
            var blob = Blob(Stroke(7, 10f, new Vector2(0, 0), new Vector2(50, 0)),
                            Stroke(3, 10f, new Vector2(40, 0), new Vector2(90, 0)));
            uint before = blob.Seed;
            blob.Strokes.Add(Stroke(42, 10f, new Vector2(85, 0), new Vector2(140, 0)));
            uint after = blob.Seed;

            Check("Зерно: от старшего мазка", before == MountainBlob.Fnv(3), $"{before} вместо {MountainBlob.Fnv(3)}");
            Check("Зерно: дорисовка его не двигает", before == after, $"{before} → {after}");
        }

        // ── §3 «Поле расстояний» ────────────────────────────────────────────────────────────────

        /// <summary>Расстояние ТОЧНОЕ евклидово, а не приближение. Фикстура: единственная ячейка фона
        /// в углу, замер по катетам 3 и 4. Мутант-чамфер (или манхэттен) даст 7, шахматное — 4,
        /// и только честная евклидова метрика — 5.</summary>
        static void DistanceIsExactlyEuclidean()
        {
            var mask = new MountainMask { W = 24, H = 24, Cell = 1f, Ox = 0f, Oy = 0f, Cells = new byte[24 * 24] };
            for (int i = 0; i < mask.Cells.Length; i++) mask.Cells[i] = 1;
            mask.Cells[0] = 0;

            var field = DistanceField.Build(mask);
            float at = field[4 * 24 + 3];
            Check("Поле: точное евклидово (3,4) → 5", Near(at, 5f, 1e-3f), $"{at:0.####} вместо 5");
        }

        // ── §4 и §5, кольца ─────────────────────────────────────────────────────────────────────

        /// <summary>Уровни идут с шагом 2R от R: полосы, которые они накрывают, стыкуются встык.
        /// Мутант: шаг R — полосы наложатся вдвое, и колец станет вдвое больше.</summary>
        static void LevelsStepByTwoR()
        {
            var levels = RingSelection.Levels(10f, 2f);
            bool ok = levels.Count == 3 && Near(levels[0], 2f, 1e-4f) && Near(levels[1], 6f, 1e-4f) && Near(levels[2], 10f, 1e-4f);
            Check("Кольца: уровни (2k+1)·R", ok, string.Join(", ", levels));
        }

        /// <summary>§5, сердце отбора. Две фикстуры отличаются ТОЛЬКО толщиной, и правильное правило
        /// отвечает на них по-разному: под массой толщиной 1.1R глубины нет — кольцо там выродилось
        /// бы в дважды пройденную осевую линию и разрезало сквозную ось надвое; под массой толщиной
        /// 2R глубина есть, и кольцо законно. Мутант: убрать проверку глубины — примутся оба.</summary>
        static void ShallowRingIsRejected()
        {
            int thin = AcceptedRings(strokeRadius: 11f, mountainRadiusCells: 10f);
            int thick = AcceptedRings(strokeRadius: 20f, mountainRadiusCells: 10f);
            Check("§5: под тонкой массой кольца нет", thin == 0, $"принято колец {thin}, ждали 0");
            Check("§5: под толстой массой кольцо есть", thick >= 1, $"принято колец {thick}, ждали хотя бы 1");
        }

        static int AcceptedRings(float strokeRadius, float mountainRadiusCells)
        {
            var blob = Blob(Stroke(1, strokeRadius, new Vector2(0, 0), new Vector2(300, 0)));
            var mask = MountainMask.Build(blob, 1f);
            var field = DistanceField.Build(mask);
            return RingSelection.Select(field, mask.W, mask.H, mountainRadiusCells).Count;
        }

        /// <summary>Мазок через всю карту не должен рождать миллионы ячеек: шаг сетки огрубляется,
        /// пока не влезет. Мутант: убрать потолок — сетка вырастет на два порядка и подвесит кисть.</summary>
        static void GridSizeIsCapped()
        {
            var blob = Blob(Stroke(1, 5f, new Vector2(0, 0), new Vector2(20000, 20000)));
            var mask = MountainMask.Build(blob, 0.5f, maxCells: 200_000);
            long cells = (long)mask.W * mask.H;
            Check("Сетка: потолок соблюдён", cells <= 200_000, $"ячеек {cells}");
            Check("Сетка: шаг огрублён, а не обрезано пятно", mask.Cell > 0.5f, $"шаг остался {mask.Cell}");
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

        // ── §6–§7 «Скелет и доводка оси» ────────────────────────────────────────────────────────

        /// <summary>
        /// Утоньшение обязано сохранять связность и топологию: у кольцевого мазка ось остаётся ОДНОЙ
        /// замкнутой петлёй. Мутанты: снять проверку «ровно один переход 0→1» (a != 1) — петля
        /// перекусывается или съедается целиком; вернуть маску как есть — она вовсе не утоньшилась.
        /// </summary>
        static void ThinningKeepsTheLoop()
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 64; i++)
            {
                double t = i / 64.0 * Math.PI * 2.0;
                pts.Add(new Vector2(200f + 60f * (float)Math.Cos(t), 200f + 60f * (float)Math.Sin(t)));
            }
            var blob = Blob(Stroke(1, 15f, pts.ToArray()));
            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 15f));
            var sk = Skeleton.Thin(mask);

            int filled = 0, thinned = 0;
            for (int i = 0; i < mask.Cells.Length; i++) { if (mask.Cells[i] != 0) filled++; if (sk[i] != 0) thinned++; }

            Check("Скелет: кольцо утоньшилось", thinned > 0 && thinned * 5 < filled,
                  $"масса {filled} ячеек, скелет {thinned} — утоньшения не было");
            Check("Скелет: кольцо осталось одним куском", RasterComponents(sk, mask.W, mask.H) == 1,
                  $"кусков {RasterComponents(sk, mask.W, mask.H)}, ждали один");
        }

        /// <summary>
        /// Шип — короткая ветка со свободным концом — срезается. Фикстура нарисована прямо в растре
        /// скелета: так проверяется само правило, а не то, как неровность мазка превратилась в шип.
        /// Проверка обязана краснеть В ОБЕ СТОРОНЫ, поэтому длина порога перебирается: при нулевом
        /// пороге шип выживает (две оси вместо одной), при огромном срезается и сама ось (ни одной).
        /// </summary>
        static void SpurIsPruned()
        {
            Check("Скелет: шип срезан", SpurPaths(6) == 1, $"осей {SpurPaths(6)}, ждали одну");
            Check("Скелет: при нулевом пороге шип выживает", SpurPaths(0) > 1,
                  "шип исчез и без порога — значит, срезает его не обрезка");
            Check("Скелет: при огромном пороге не остаётся ничего", SpurPaths(1000) == 0,
                  "ось уцелела вопреки порогу — обрезка не смотрит на длину");
        }

        /// <summary>Сколько осей даёт «линия с коротким отростком» при заданном пороге обрезки.</summary>
        static int SpurPaths(int pruneLen)
        {
            const int w = 60, h = 40;
            var sk = new byte[w * h];
            HLine(sk, w, 5, 45, 20);
            VLine(sk, w, 25, 16, 20);           // отросток длиной 5 ячеек
            return AxisStitching.Stitch(Skeleton.Branches(sk, w, h, pruneLen)).Count;
        }

        /// <summary>
        /// Пузырь схлопывается, а настоящая дыра — нет. Обе половины обязательны: без первой выживает
        /// мутант «не схлопывать никогда», без второй — мутант «схлопывать всегда», а он страшнее:
        /// он съедает кольцевые массивы, ради которых весь §4 и написан.
        /// </summary>
        static void BubbleCollapsesButDonutSurvives()
        {
            const int w = 60, h = 40;

            // Пузырь: линия, в середине которой две ветки расходятся всего на две ячейки.
            var bubble = new byte[w * h];
            HLine(bubble, w, 5, 15, 20);
            HLine(bubble, w, 20, 45, 20);
            HLine(bubble, w, 16, 19, 19);
            HLine(bubble, w, 16, 19, 21);
            var paths = AxisStitching.Stitch(Skeleton.Branches(bubble, w, h, 4));
            Check("Скелет: пузырь схлопнулся", paths.Count == 1, $"осей {paths.Count}, ждали одну");
            Check("Скелет: после схлопывания ось идёт из конца в конец",
                  paths.Count == 1 && HasPoint(paths[0], 5, 20) && HasPoint(paths[0], 45, 20),
                  "ось потеряла конец");

            // Дыра: та же пара развилок, но ветки расходятся на двадцать ячеек.
            var donut = new byte[w * h];
            HLine(donut, w, 5, 55, 30);
            VLine(donut, w, 15, 10, 30);
            HLine(donut, w, 15, 35, 10);
            VLine(donut, w, 35, 10, 30);
            Skeleton.Branches(donut, w, h, 4);
            Check("Скелет: у настоящей дыры уцелели обе стороны",
                  donut[30 * w + 25] != 0 && donut[10 * w + 25] != 0,
                  "одну из сторон дыры стёрли как пузырь");
        }

        /// <summary>
        /// В развилке сшиваются те концы, что идут НАВСТРЕЧУ друг другу. Отросток тут строго
        /// поперечный, поэтому порог «прямизны» отбраковывает его в одиночку: мутант, которого ловит
        /// эта фикстура, — «не сшивать вовсе». За сортировку по прямизне отвечает следующая проверка.
        /// </summary>
        static void ForkStitchesIntoOneAxis()
        {
            const int w = 60, h = 40;
            var sk = new byte[w * h];
            HLine(sk, w, 5, 45, 20);
            VLine(sk, w, 25, 5, 20);

            var raw = Skeleton.Branches(sk, w, h, 4);
            var stitched = AxisStitching.Stitch(raw);

            Check("Сшивка: развилка даёт три ветки", raw.Count == 3, $"веток {raw.Count}");
            Check("Сшивка: остаются две оси", stitched.Count == 2, $"осей {stitched.Count}, ждали две");

            bool through = false;
            foreach (var p in stitched) if (HasPoint(p, 5, 20) && HasPoint(p, 45, 20)) through = true;
            Check("Сшивка: перекладина стала одной осью", through,
                  "сквозной оси нет — сшили не те концы");
        }

        /// <summary>
        /// Отросток под тупым углом: с одной из половин перекладины он тоже «идёт навстречу», порог
        /// проходят ОБЕ пары, и выбрать правильную может только сортировка по прямизне. Мутант «брать
        /// пары в том порядке, в каком они перечислились» приклеивает отросток к правой половине, а
        /// левую оставляет отдельной осью. Осей при этом всё равно две — поэтому проверяется не их
        /// число, а то, что сквозная ось действительно сквозная.
        /// </summary>
        static void ObtuseBranchDoesNotStealTheAxis()
        {
            const int w = 60, h = 40;
            var sk = new byte[w * h];
            HLine(sk, w, 5, 45, 25);
            Line(sk, w, 25, 24, 8, 15);          // отросток примерно под 150°

            var stitched = AxisStitching.Stitch(Skeleton.Branches(sk, w, h, 4));

            bool through = false;
            foreach (var p in stitched) if (HasPoint(p, 5, 25) && HasPoint(p, 45, 25)) through = true;
            Check("Сшивка: тупой отросток не перехватил ось", through,
                  "перекладина разорвана — пары выбраны не по прямизне");
        }

        /// <summary>
        /// Скелет режется покрытием колец: там, где кольцо уже проложило ось, второй оси быть не
        /// должно. Масса подобрана так, что кольцо ложится в восемнадцати ячейках от осевой линии —
        /// ближе предела R + 2. Мутант «ничего не покрыто» добавит вдоль той же массы вторую ось.
        /// </summary>
        static void RingCoverageSuppressesSkeleton()
        {
            var axes = CapsuleAxes(out _);
            int rings = 0, skel = 0;
            foreach (var a in axes) { if (a.FromRing) rings++; else skel++; }

            Check("Покрытие: кольцо принято", rings == 1, $"колец {rings}, ждали одно");
            Check("Покрытие: скелет под кольцом отброшен", skel == 0,
                  $"осей от скелета {skel} — покрытие не сработало");
        }

        /// <summary>
        /// Ластик режет не только маску, но и оси: у разорванного массива ось есть у каждой половины
        /// и ни одной поперёк разрыва. Проверка сквозная — она смотрит на итог всей цепочки, тогда
        /// как остальные проверяют по одному правилу. Мутант: ластик кладётся как краска.
        /// </summary>
        static void ErasedGapSplitsAxesToo()
        {
            var blob = Blob(Stroke(1, 40f, new Vector2(0, 0), new Vector2(400, 0)));
            blob.Erasers.Add(Stroke(2, 40f, new Vector2(200, -80), new Vector2(200, 80)));

            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 40f));
            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, 22f / mask.Cell);

            bool left = false, right = false, inside = false;
            foreach (var axis in axes)
            {
                foreach (var p in axis.Points)
                {
                    float x = mask.GridToWorld(p.X, p.Y).X;
                    if (x < 150f) left = true;
                    else if (x > 250f) right = true;
                    else inside = true;
                }
            }

            Check("Ластик: ось есть у обеих половин", left && right,
                  $"слева {(left ? "есть" : "нет")}, справа {(right ? "есть" : "нет")}");
            Check("Ластик: поперёк разрыва оси нет", !inside, "ось прошла по стёртому месту");
        }

        /// <summary>
        /// ИЗЪЯН, ЗАФИКСИРОВАННЫЙ НАРОЧНО (показать ДМ на чекпоинте задачи 5).
        ///
        /// Критерий §5 «под кольцом должна быть глубина» меряет глубину у КОМПОНЕНТЫ целиком. У
        /// развилки в месте схождения рукавов масса толще, чем в самих рукавах: вписанная окружность
        /// там больше. Одного этого утолщения хватает, чтобы кольцо было принято НА ВСЮ компоненту —
        /// и оно уходит гулять вдоль тонких рукавов той самой вырожденной петлёй, ради запрета
        /// которой §5 и написан. Скелет при этом срезается покрытием подчистую.
        ///
        /// Тот же изъян есть и в прототипе ДМ — правило там буквально то же. Лечится он тем, что
        /// глубину надо мерить МЕСТНО, вдоль контура, и резать кольцо на принятые и отвергнутые
        /// куски; но это правка самого алгоритма, а не переноса, и решать её ДМ.
        ///
        /// Проверка нарочно утверждает НЫНЕШНЕЕ поведение: если она однажды покраснеет, значит
        /// критерий сделали местным — тогда её надо переписать под новое правило, а не «починить».
        /// </summary>
        static void ForkRescuesRingOverThinArms()
        {
            var arm = 30f;                       // рукав тоньше 1.6·R — сам по себе кольца не заслужил
            var blob = Blob(Stroke(1, arm, new Vector2(0, 0), new Vector2(250, 0)),
                            Stroke(2, arm, new Vector2(250, 0), new Vector2(440, 110)),
                            Stroke(3, arm, new Vector2(250, 0), new Vector2(440, -110)));

            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, arm));
            var field = DistanceField.Build(mask);
            float radiusCells = 22f / mask.Cell;

            var midArm = mask.WorldToGrid(new Vector2(120, 0));
            float armDepth = DistanceField.Sample(field, mask.W, mask.H, midArm.X, midArm.Y);
            float need = radiusCells * (1f + RingSelection.DepthMargin);

            Check("Развилка: рукав сам по себе кольца не заслуживает", armDepth < need,
                  $"толщина рукава {armDepth:0.0} ≥ порога {need:0.0} — фикстура перестала быть тонкой");
            Check("Развилка: узел глубже порога", DistanceField.Max(field) >= need,
                  $"узел {DistanceField.Max(field):0.0} < порога {need:0.0} — фикстура перестала быть развилкой");

            int rings = 0, skel = 0;
            foreach (var axis in AxisBuilder.Build(mask, field, radiusCells))
                if (axis.FromRing) rings++; else skel++;

            Check("Развилка: узел вытягивает кольцо на всю развилку (известный изъян §5)",
                  rings == 1 && skel == 0,
                  $"колец {rings}, осей от скелета {skel} — похоже, критерий глубины сделали местным: " +
                  "перепиши проверку под новое правило");
        }

        /// <summary>Оси длинной толстой массы. Возвращает заодно маску — она нужна для перевода в мир.</summary>
        static List<MountainAxis> CapsuleAxes(out MountainMask mask)
        {
            var blob = Blob(Stroke(1, 40f, new Vector2(0, 0), new Vector2(300, 0)));
            mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 40f));
            var field = DistanceField.Build(mask);
            return AxisBuilder.Build(mask, field, 22f / mask.Cell);
        }

        /// <summary>Замер, а не проверка: во что обходится утоньшение на самой крупной массе стенда.
        /// План опасался, что оно съест сотни миллисекунд; число говорит само за себя.</summary>
        static void TimeThinning()
        {
            foreach (float ratio in new[] { 1f, 2f, 4f, 6f, 10f })
            {
                const float r = 22f;
                float brush = r * ratio;
                var blob = Blob(Stroke(1, brush, new Vector2(0, 0), new Vector2(900, 0)));
                var mask = MountainMask.Build(blob, MountainMask.ChooseCell(r, brush));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Skeleton.Thin(mask);
                watch.Stop();
                Console.WriteLine($"кисть = {ratio:F0}·R: сетка {mask.W}×{mask.H}, толщина {brush / mask.Cell:F0} ячеек, " +
                                  $"утоньшение {watch.Elapsed.TotalMilliseconds:F1} мс");
            }
        }

        // ── мелочь ──────────────────────────────────────────────────────────────────────────────

        static void HLine(byte[] raster, int w, int x0, int x1, int y)
        {
            for (int x = x0; x <= x1; x++) raster[y * w + x] = 1;
        }

        static void VLine(byte[] raster, int w, int x, int y0, int y1)
        {
            for (int y = y0; y <= y1; y++) raster[y * w + x] = 1;
        }

        /// <summary>Отрезок по Брезенхэму — нужен наклонным фикстурам.</summary>
        static void Line(byte[] raster, int w, int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                raster[y0 * w + x0] = 1;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static bool HasPoint(AxisPath path, float x, float y)
        {
            foreach (var p in path.Pts) if (Near(p.X, x, 0.01f) && Near(p.Y, y, 0.01f)) return true;
            return false;
        }

        /// <summary>Число связных кусков растра по ВОСЬМИ соседям: скелет идёт лесенкой, и по четырём
        /// наклонная линия распалась бы на отдельные ячейки.</summary>
        static int RasterComponents(byte[] raster, int w, int h)
        {
            var seen = new bool[w * h];
            var stack = new Stack<int>();
            int count = 0;

            for (int start = 0; start < raster.Length; start++)
            {
                if (raster[start] == 0 || seen[start]) continue;
                count++;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int k = ny * w + nx;
                            if (raster[k] == 0 || seen[k]) continue;
                            seen[k] = true;
                            stack.Push(k);
                        }
                }
            }
            return count;
        }


        static MountainStroke Stroke(int id, float radius, params Vector2[] points)
        {
            var stroke = new MountainStroke { Id = id, Radius = radius };
            stroke.Points.AddRange(points);
            return stroke;
        }

        static MountainBlob Blob(params MountainStroke[] strokes)
        {
            var blob = new MountainBlob();
            blob.Strokes.AddRange(strokes);
            return blob;
        }

        static bool Filled(MountainMask mask, Vector2 world)
        {
            var g = mask.WorldToGrid(world);
            return mask.At((int)Math.Round(g.X), (int)Math.Round(g.Y));
        }

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
