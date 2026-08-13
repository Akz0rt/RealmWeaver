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
            if (mode == "look") { Preview.WriteLook("look.svg"); return 0; }
            if (mode == "stack") { Preview.WriteStack("stack.svg"); return 0; }
            if (mode == "json") { Export.WriteJson("links.json"); return 0; }
            if (mode == "tiers") { Preview.WriteTiers("tiers.svg"); return 0; }
            if (mode == "sharp") { Preview.WriteSharp("sharp.svg"); return 0; }
            if (mode == "profiles") { Preview.WriteProfiles("profiles.svg"); return 0; }
            if (mode == "cells") { Preview.WriteCells("cells.svg"); return 0; }
            if (mode == "app") { Preview.WriteAppLike("app.svg"); return 0; }
            if (mode == "massif") { Preview.WriteMassif("massif.svg"); return 0; }
            if (mode == "time") { TimeThinning(); return 0; }

            MaskMeasuresToSegment();
            EraserSubtracts();
            EraserSplitsMass();
            BlobsAreTransitive();
            EraserDoesNotMerge();
            MassifsDoNotDisturbEachOther();
            PolygonMaskFillsAndSmooths();
            MassifsSplitByCellNeighbourhood();
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
            ForkKeepsRingOffThinArms();
            RingOverThickMassStaysWhole();
            EveryMaskCellHasAnAxisNearby();
            CoverageCutEndsAreNotExtended();
            RingStrokeKeepsClosedAxis();
            MaskBorderIsAlwaysBackground();
            AxesAreReproducible();
            ErasedGapSplitsAxesToo();
            MountainsStayOnLand();
            UndoingABridgeSplitsTheMassifBack();

            GridPointIsTheCellCentre();
            LinkCountIsChosenByRatio();
            AnisotropySpreadsVerticalLinks();
            JitterSpreadsLengthsButKeepsThemClose();
            ClosedAxisSeamMoves();
            ShortBranchGetsALinkNotAHole();
            FreeEndsOnlyAtTips();
            LinksTileTheAxis();
            TiersFollowDepthBands();
            TierHeightIsNormalizedToThisBlob();
            PainterOrderIsFarToNear();
            GeometryIsReproducible();

            WaistProfile();
            SharedWidthAtVertebra();
            ChainsShareEnds();
            MonotoneAlongGentleLink();
            ApexHeight();
            FreeEndStretch();
            LobeCapKeepsTheSkirtShort();
            CurvedVerticalLinkKeepsAStraightFoot();
            CellMaskSilhouettesAreSimple();
            TriangulationCoversTheSilhouetteExactly();
            TierRampSpreadsTiers();

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

        // ── Разворот 2026-08-14: масса приходит из клеток карты ─────────────────────────────────

        /// <summary>
        /// Появление второй массы не двигает уже нарисованную первую.
        ///
        /// Это ЕДИНСТВЕННОЕ, что после разворота 2026-08-14 гарантируется про устойчивость рисунка:
        /// прежнее «зерно от старшего мазка» потеряло предмет (у массива из клеток старшего нет), а
        /// полной устойчивости не бывает — ось пересчитывается вместе со своей массой.
        ///
        /// Фикстура выстроена так, чтобы правильное и неправильное РАСХОДИЛИСЬ. Вторая масса лежит
        /// ЮГО-ЗАПАДНЕЕ первой и в той же маске, поэтому её ось обходится раньше и забирает нулевой
        /// номер, сдвигая номер первой. Мутант «зерно от номера оси» на этом и валится. Первая
        /// попытка этой проверки ставила массы в РАЗНЫЕ маски и была пустой: там номера и так
        /// начинаются с нуля у каждой, и мутант её пережил.
        ///
        /// Координаты целые, а шаг сетки при этих числах ровно 1: тогда расширение рамки сдвигает
        /// начало сетки на целое число ячеек, и первая масса ложится в те же точки замера. Иначе
        /// проверка падала бы от подвижки полуклетки, а не от зерна.
        /// </summary>
        static void MassifsDoNotDisturbEachOther()
        {
            var settings = new MountainSettings { Radius = 22f };
            var east = Stroke(1, 40f, new Vector2(0, 0), new Vector2(300, 40));
            var southWest = Stroke(2, 40f, new Vector2(-600, -600), new Vector2(-400, -560));

            var alone = MountainGeometry.Build(Blob(east), settings);
            var together = MountainGeometry.Build(Blob(east, southWest), settings);

            // Из общего счёта берём только восточные горы: западные — новые, им и положено появиться.
            var eastAlone = EastOf(alone, -200f);
            var eastTogether = EastOf(together, -200f);

            bool same = eastAlone.Count == eastTogether.Count && eastAlone.Count > 0;
            for (int i = 0; same && i < eastAlone.Count; i++)
                if (Vector2.Distance(eastAlone[i], eastTogether[i]) > 1e-3f) same = false;

            Check("Массивы: вторая масса не сдвинула первую", same,
                  $"вершин было {eastAlone.Count}, стало {eastTogether.Count} — первая масса поехала");
            Check("Массивы: вторая масса вообще появилась", together.Count > alone.Count,
                  $"гор было {alone.Count}, стало {together.Count}");
        }

        static List<Vector2> EastOf(List<MountainShape> shapes, float x)
        {
            var result = new List<Vector2>();
            foreach (var shape in shapes) if (shape.Apex.X > x) result.Add(shape.Apex);
            result.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            return result;
        }

        /// <summary>
        /// Маска из многоугольников клеток: заливка попадает внутрь, шов между соседними клетками не
        /// оставляет щели, а сглаживание срезает угол мозаики.
        ///
        /// Мутанты: заливка по правилу «строго внутри» с обеих сторон шва (щель в одну ячейку — а
        /// она разрезала бы массив надвое); сглаживание, которое ничего не делает (порог «больше
        /// нуля» вместо «больше половины») — тогда угол останется прямым.
        /// </summary>
        static void PolygonMaskFillsAndSmooths()
        {
            // Два квадрата встык по x = 40: общий шов, общие вершины.
            var left = new List<Vector2> { new(0, 0), new(40, 0), new(40, 40), new(0, 40) };
            var right = new List<Vector2> { new(40, 0), new(80, 0), new(80, 40), new(40, 40) };
            var mask = MountainMask.FromPolygons(new List<IReadOnlyList<Vector2>> { left, right }, 1f);

            Check("Клетки: заливка попала внутрь", mask != null && Filled(mask, new Vector2(20, 20)) && Filled(mask, new Vector2(60, 20)),
                  "середины клеток не залиты");
            Check("Клетки: шов не оставил щели", mask != null && mask.Components().Count == 1,
                  $"кусков {(mask == null ? -1 : mask.Components().Count)}, ждали 1");

            // Угол квадрата: до сглаживания залит, после — срезан.
            bool cornerBefore = Filled(mask, new Vector2(79f, 39f));
            mask.Smooth(6);
            bool cornerAfter = Filled(mask, new Vector2(79f, 39f));
            Check("Клетки: сглаживание срезает угол мозаики", cornerBefore && !cornerAfter,
                  $"угол до {cornerBefore}, после {cornerAfter}");
            Check("Клетки: сглаживание не съело середину", Filled(mask, new Vector2(40f, 20f)), "середина массы погасла");
        }

        /// <summary>
        /// Массивы — это связные куски по соседству клеток. Мутант: обход без проверки
        /// принадлежности (тогда всё сольётся в один кусок) или без пометки посещённых (кусков
        /// станет столько же, сколько клеток).
        /// </summary>
        static void MassifsSplitByCellNeighbourhood()
        {
            //  1—2—3   5—6 , клетка 4 равнина и связи через неё нет
            var adjacency = new Dictionary<int, IReadOnlyList<int>>
            {
                [1] = new[] { 2 }, [2] = new[] { 1, 3 }, [3] = new[] { 2, 4 },
                [4] = new[] { 3, 5 }, [5] = new[] { 4, 6 }, [6] = new[] { 5 },
            };
            var pieces = ReliefMassifs.Split(new[] { 1, 2, 3, 5, 6 },
                                             id => adjacency.TryGetValue(id, out var n) ? n : null);
            pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
            Check("Массивы: равнина посередине разделила надвое",
                  pieces.Count == 2 && pieces[0].Count == 3 && pieces[1].Count == 2,
                  $"кусков {pieces.Count} размерами {string.Join("+", pieces.ConvertAll(p => p.Count))}");
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
            // Звено берём соразмерное горе (длина ≈ 1.6·полуширины, как её режет §8), а не в пять
            // раз длиннее: у длинного срабатывает потолок на подошву (LobeCapFactor), и проверка
            // мерила бы уже его, а не растяжение.
            var link = Horizontal(32f, 20f);
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

        /// <summary>
        /// Потолок на длину подошвы: у звена, вышедшего много длиннее самой горы, юбка не растёт
        /// вместе с ним. Подошва — это план, вид сверху, и на гряде, уходящей ВВЕРХ ПО КАРТИНКЕ, вся
        /// её длина ложится вниз по экрану; без потолка гора превращается в приземистую чешуйку с
        /// рюшем вместо склонов.
        ///
        /// Звено обязано быть ВЕРТИКАЛЬНЫМ, иначе проверка пуста: у горизонтального длина ложится
        /// вбок и юбки не касается вовсе — там потолок нечему ловить (на этом первая редакция
        /// проверки и попалась).
        ///
        /// Юбка длинного звена обязана упереться в потолок: 1.2·w сплющенные вдвое с небольшим.
        /// Мутант — убрать потолок: юбка станет по полудлине звена, то есть впятеро больше.
        /// </summary>
        static void LobeCapKeepsTheSkirtShort()
        {
            const float w = 20f, squash = 1f / 1.6f;
            var link = Vertical(200f, w);
            var mound = Mound(link, 1f, 1f);
            if (mound == null) { Fail("Потолок подошвы", "гора не построена"); return; }

            float skirt = link.Mid.Y - mound.Depth;
            float capped = 1.2f * w * squash;      // потолок LobeCapFactor, сплющенный
            float uncapped = 100f * squash;        // полудлина звена, сплющенная — это без потолка
            Check("Потолок подошвы: длинное вертикальное звено не отращивает юбку",
                  Near(skirt, capped, 1.5f) && skirt < uncapped * 0.5f,
                  $"юбка {skirt:0.##}, потолок ждали {capped:0.##}, без потолка было бы {uncapped:0.##}");
        }

        /// <summary>
        /// Изогнутое почти вертикальное звено — самая трудная фикстура: сырая подошва у него
        /// заворачивается назад по X. ДО 2026-08-14 это считалось безобидным и держалось проверкой
        /// «подошва завернулась»; ДМ показал скриншотами, что не безобидно — силуэт при этом сам
        /// себя пересекает, и в горе появляется угловатый ВЫКУС, сквозь который видно карту.
        /// Теперь подошва выпрямляется (MoundBuilder.MakeMonotone), и проверка требует обратного.
        ///
        /// Мутант: не выпрямлять — подошва этой фикстуры завернётся, проверка упадёт.
        /// </summary>
        static void CurvedVerticalLinkKeepsAStraightFoot()
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

            var loop = new List<Vector2>(shape.Crest);
            for (int i = shape.Front.Count - 1; i >= 0; i--) loop.Add(shape.Front[i]);

            Check("Изогнутое вертикальное звено: подошва идёт слева направо", IsMonotone(shape.Front),
                  "подошва завернулась назад по X — в горе будет выкус");
            Check("Изогнутое вертикальное звено: силуэт не пересекает сам себя", IsSimple(loop),
                  "силуэт самопересекается");
        }

        /// <summary>
        /// Силуэты на маске ПРИЛОЖЕНИЯ, а не на мазке. Маска здесь строится из многоугольников
        /// клеток и сглаживается — ровно как в игре; её контур бугрист на шаге клетки (15 единиц
        /// при горе 10), оси от этого куда извилистее, и подошвы заворачиваются вчетверо чаще.
        /// Замер до выпрямления: самопересекались 14 силуэтов из 115, у 48 подошва завёрнута,
        /// худшая ошибка площади 5.9 % — это и были «выкусы» на скриншотах ДМ. На мазке той же
        /// толщины изъян почти не виден (1–2 из 156), поэтому фикстура обязана быть КЛЕТОЧНОЙ.
        ///
        /// Мутант: не выпрямлять подошву — счётчики вернутся к 14 и 48.
        /// </summary>
        static void CellMaskSilhouettesAreSimple()
        {
            var polys = new List<IReadOnlyList<Vector2>>();
            var centre = new Vector2(300, 300);
            for (float x = centre.X - 120f; x <= centre.X + 120f; x += 15f)
                for (float y = centre.Y - 120f; y <= centre.Y + 120f; y += 15f)
                {
                    var q = new Vector2(x, y);
                    if ((q - centre).Length() > 120f) continue;
                    polys.Add(new List<Vector2>
                    {
                        new Vector2(q.X - 7.5f, q.Y - 7.5f), new Vector2(q.X + 7.5f, q.Y - 7.5f),
                        new Vector2(q.X + 7.5f, q.Y + 7.5f), new Vector2(q.X - 7.5f, q.Y + 7.5f),
                    });
                }

            var settings = new MountainSettings { Radius = 10f };
            var mask = MountainMask.FromPolygons(polys, MountainMask.ChooseCell(10f, 10f));
            mask.Smooth((int)Math.Round(0.5f * 10f / mask.Cell));
            var shapes = MountainGeometry.BuildFromMask(mask, settings, out _);
            if (shapes.Count < 50) { Fail("Клеточная маска", $"гор всего {shapes.Count}"); return; }

            int folded = 0, notSimple = 0;
            foreach (var shape in shapes)
            {
                if (!IsMonotone(shape.Front)) folded++;
                // Контур строим ТАК ЖЕ, как триангуляция: концы у цепей общие, и повторять их
                // нельзя — иначе в контуре появляются нулевые отрезки и он «самопересекается» на
                // ровном месте.
                var loop = new List<Vector2>(shape.Crest);
                for (int i = shape.Front.Count - 2; i >= 1; i--) loop.Add(shape.Front[i]);
                if (!IsSimple(loop)) notSimple++;
            }

            Check("Клеточная маска: ни одна подошва не завернулась", folded == 0,
                  $"завёрнутых {folded} из {shapes.Count}");
            // Порог ЕДИНИЦА, и это честно измеренный остаток, а не запас «на всякий случай»: было
            // 14 из 115, после выпрямления подошвы и прижатия её под гребень остался ровно один
            // силуэт. Ноль сюда ставить нельзя — проверка покраснеет на ровном месте; поднимать
            // выше единицы тоже нельзя — тогда возврат изъяна пройдёт незамеченным.
            Check("Клеточная маска: самопересечений не больше одного", notSimple <= 1,
                  $"самопересекающихся {notSimple} из {shapes.Count}");
        }

        /// <summary>
        /// Триангуляция обязана выложить силуэт горы РОВНО один раз: без налезающих друг на друга
        /// треугольников, без вылезающих за силуэт и без вывернутых. Фикстура — то самое изогнутое
        /// почти вертикальное звено, у которого подошва заворачивается назад по X (проверка выше
        /// как раз это и утверждает): сшивка «продвигай отставшего по X» на нём и ломалась.
        ///
        /// Два утверждения, и оба нужны. Сумма МОДУЛЕЙ площадей ловит перекрытие и вылет: лишний
        /// треугольник добавляет площади. Модуль СУММЫ ловит вывернутый: он вычитается вместо того,
        /// чтобы прибавляться. Одной суммы модулей мало — вывернутый треугольник она считает
        /// добросовестным, а на экране его не видно вовсе (грани не отсекаются).
        /// Мутант: вернуть сравнение по X вместо доли пройденной длины.
        /// </summary>
        static void TriangulationCoversTheSilhouetteExactly()
        {
            // Кольцевой мазок нарочно: у кольца полно звеньев, которые одновременно изогнуты и идут
            // близко к вертикали, — то есть ровно тех, у кого подошва заворачивается назад по X.
            var ring = new List<Vector2>();
            for (int i = 0; i <= 72; i++)
            {
                double a = i / 72.0 * Math.PI * 2.0;
                ring.Add(new Vector2(300f + 130f * (float)Math.Cos(a), 300f + 130f * (float)Math.Sin(a)));
            }
            var shapes = MountainGeometry.Build(Blob(Stroke(1, 30f, ring.ToArray())),
                                                new MountainSettings { Radius = 22f });
            if (shapes.Count < 8) { Fail("Триангуляция", $"гор всего {shapes.Count}"); return; }

            float worstOverlap = 0f, worstFlip = 0f;
            int folded = 0;
            bool simple = true;
            foreach (var shape in shapes)
            {
                if (!IsMonotone(shape.Front)) folded++;

                // Силуэт: гребень слева направо, следом подошва справа налево.
                var loop = new List<Vector2>(shape.Crest);
                for (int i = shape.Front.Count - 1; i >= 0; i--) loop.Add(shape.Front[i]);
                float silhouette = Math.Abs(SignedArea(loop));
                if (silhouette <= 0f) continue;
                if (!IsSimple(loop)) simple = false;

                var verts = new List<Vector2>(shape.Crest);
                verts.AddRange(shape.Front);
                int[] tris = MountainTriangulation.Fill(shape);

                float sum = 0f, absSum = 0f;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    float area = SignedArea(new List<Vector2>
                        { verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]] });
                    sum += area;
                    absSum += Math.Abs(area);
                }

                worstOverlap = Math.Max(worstOverlap, Math.Abs(absSum - silhouette) / silhouette);
                worstFlip = Math.Max(worstFlip, Math.Abs(Math.Abs(sum) - silhouette) / silhouette);
            }

            // Прежде здесь стояло «фикстура трудная — подошвы заворачиваются». С 2026-08-14 подошвы
            // выпрямляются на выходе из MoundBuilder, поэтому трудность фикстуры держится иначе: у
            // кольца заведомо есть звенья, изогнутые и близкие к вертикали, — это те самые, на
            // которых сшивка полосой и врала.
            Check("Триангуляция: фикстура трудная — подошвы выпрямлены все до одной", folded == 0,
                  $"завернувшихся подошв {folded} из {shapes.Count}");
            Check("Триангуляция: силуэт горы простой", simple,
                  "силуэт сам себя пересекает — на этом отрезание ушей уже не стоит");
            Check("Триангуляция: треугольники не налезают и не вылезают", worstOverlap < 0.001f,
                  $"худший перебор площади {worstOverlap * 100f:0.###} %");
            Check("Триангуляция: ни один треугольник не вывернут", worstFlip < 0.001f,
                  $"худший недобор площади {worstFlip * 100f:0.###} %");
        }

        /// <summary>
        /// Шкала красок по ярусам (решение ДМ 2026-08-14): внешнему слою один цвет, среднему другой,
        /// внутреннему третий. Здесь только ДОЛЯ вдоль шкалы — краски берёт рендер из палитры карты,
        /// и Unity стенду не виден.
        ///
        /// Мутанты, ради которых написано:
        /// — делить на tierCount вместо tierCount−1: внутренний ярус недотягивает до тёмного конца;
        /// — нормировать по самому глубокому ярусу ЭТОЙ массы: узкая гряда докрашивается до ядра,
        ///   которого у неё нет (у ВЫСОТЫ нормировка как раз есть — см. TierHeightIsNormalized…,
        ///   и путать эти два правила нельзя);
        /// — считать долю как step·contrast: нулевой контраст даёт не средний цвет, а светлый;
        /// — брать контраст по модулю: отрицательный перестаёт переворачивать шкалу.
        /// </summary>
        static void TierRampSpreadsTiers()
        {
            Check("Ярусы-цвет: внешний светлый, средний посередине, внутренний тёмный",
                  Near(MountainTierRamp.Mix(0, 3, 1f), 0f, 1e-4f)
                  && Near(MountainTierRamp.Mix(1, 3, 1f), 0.5f, 1e-4f)
                  && Near(MountainTierRamp.Mix(2, 3, 1f), 1f, 1e-4f),
                  $"{MountainTierRamp.Mix(0, 3, 1f):0.###} / {MountainTierRamp.Mix(1, 3, 1f):0.###}"
                  + $" / {MountainTierRamp.Mix(2, 3, 1f):0.###}");

            // Узкая масса добралась лишь до яруса 1 из трёх. Её сердцевина обязана взять СРЕДНЮЮ
            // краску, а не тёмную: цвет говорит «какой это слой», а не «самое глубокое, что тут есть».
            Check("Ярусы-цвет: узкая гряда не докрашивается до сердцевины",
                  Near(MountainTierRamp.Mix(1, 3, 1f), 0.5f, 1e-4f),
                  $"{MountainTierRamp.Mix(1, 3, 1f):0.###}");

            Check("Ярусы-цвет: нулевой контраст — вся масса одного СРЕДНЕГО цвета",
                  Near(MountainTierRamp.Mix(0, 3, 0f), 0.5f, 1e-4f)
                  && Near(MountainTierRamp.Mix(2, 3, 0f), 0.5f, 1e-4f),
                  $"{MountainTierRamp.Mix(0, 3, 0f):0.###} / {MountainTierRamp.Mix(2, 3, 0f):0.###}");

            Check("Ярусы-цвет: отрицательный контраст переворачивает шкалу",
                  Near(MountainTierRamp.Mix(0, 3, -1f), 1f, 1e-4f)
                  && Near(MountainTierRamp.Mix(2, 3, -1f), 0f, 1e-4f),
                  $"{MountainTierRamp.Mix(0, 3, -1f):0.###} / {MountainTierRamp.Mix(2, 3, -1f):0.###}");

            Check("Ярусы-цвет: ярус за пределом настройки зажимается",
                  Near(MountainTierRamp.Mix(9, 3, 1f), 1f, 1e-4f)
                  && Near(MountainTierRamp.Mix(-4, 3, 1f), 0f, 1e-4f),
                  $"{MountainTierRamp.Mix(9, 3, 1f):0.###} / {MountainTierRamp.Mix(-4, 3, 1f):0.###}");
        }

        /// <summary>Площадь замкнутого многоугольника со знаком (формула шнурков).</summary>
        static float SignedArea(List<Vector2> loop)
        {
            float area = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
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
        /// Обещание кисти, ради которого в файле лежат ТОЛЬКО мазки: отмена мазка, слившего два
        /// массива, обязана разлить их обратно. Отменять тут нечего и незачем — мазок просто
        /// выкидывается из списка, и всё производное считается заново по оставшимся.
        ///
        /// Проверяется именно то, что меняет правило: число ПЯТЕН до перемычки, с ней и после её
        /// снятия — 2, 1, 2. Мутант «считать пятна один раз и запоминать» на такой фикстуре виден
        /// сразу, а на проверке «после отмены гор столько же, сколько было» — нет: чисел там поровну
        /// и при слипшихся пятнах тоже.
        /// </summary>
        static void UndoingABridgeSplitsTheMassifBack()
        {
            var left = Stroke(1, 30f, new Vector2(0, 0), new Vector2(120, 0));
            var right = Stroke(2, 30f, new Vector2(400, 0), new Vector2(520, 0));
            var bridge = Stroke(3, 30f, new Vector2(100, 0), new Vector2(420, 0));

            var apart = new List<MountainStroke> { left, right };
            Check("Кисть: врозь — два пятна", BlobBuilder.Group(apart).Count == 2,
                  $"пятен {BlobBuilder.Group(apart).Count}");

            var bridged = new List<MountainStroke> { left, right, bridge };
            Check("Кисть: перемычка слила в одно", BlobBuilder.Group(bridged).Count == 1,
                  $"пятен {BlobBuilder.Group(bridged).Count}");

            bridged.Remove(bridge);   // ровно это и делает отмена мазка
            Check("Кисть: отмена перемычки разлила обратно", BlobBuilder.Group(bridged).Count == 2,
                  $"пятен {BlobBuilder.Group(bridged).Count} — слипание пережило отмену");
        }

        /// <summary>
        /// Решение ДМ 2026-08-13: горы идут исключительно по суше. Подрезка сделана ОДНИМ местом —
        /// маской, до поля расстояний, — поэтому проверяется тоже одним: где прошли оси и звенья.
        /// Мутант: не звать подрезку — звенья уйдут в море на полторы сотни единиц.
        ///
        /// Оговорка, которую проверка утверждает намеренно и которую надо знать: подрезана МАССА, а
        /// не рисунок. Гора растёт вверх по экрану, поэтому у северного берега её гребень всё равно
        /// свесится над водой — подошва останется на суше. Иначе пришлось бы отодвигать горы от
        /// берега на две их высоты, и берег оголился бы.
        /// </summary>
        static void MountainsStayOnLand()
        {
            const float coast = 200f;                       // суша слева от берега, море справа
            Func<Vector2, bool> land = p => p.X < coast;

            var settings = new MountainSettings { Radius = 22f, IsLand = land };
            var acrossCoast = Blob(Stroke(1, 30f, new Vector2(60, 0), new Vector2(400, 0)));
            var shapes = MountainGeometry.Build(acrossCoast, settings, out var mask, out var links);

            Check("Суша: горы на суше всё-таки выросли", shapes.Count > 0, "гор не выросло вовсе");

            float worst = float.NegativeInfinity;
            foreach (var link in links)
                foreach (var p in link.Pts)
                    if (p.X > worst) worst = p.X;

            // Допуск — ячейка: подрезка судит по ЦЕНТРУ ячейки, значит закрашенная ячейка законно
            // выступает за берег на полшага сетки, а ось по ней — ещё на столько же.
            float limit = coast + (mask?.Cell ?? 1f);
            Check("Суша: звенья не уходят в море", worst <= limit,
                  $"звено дошло до X = {worst:0.0} при берегe {coast} и допуске {limit:0.0}");

            var inTheSea = Blob(Stroke(1, 30f, new Vector2(280, 0), new Vector2(430, 0)));
            var drowned = MountainGeometry.Build(inTheSea, settings);
            Check("Суша: мазок по морю не даёт ничего", drowned.Count == 0,
                  $"в море выросло {drowned.Count} гор");
        }

        /// <summary>
        /// §5 после правки «глубина меряется местно» (решение ДМ, 2026-08-12).
        ///
        /// Раньше глубина мерилась у КОМПОНЕНТЫ целиком, и развилка протекала: в месте схождения
        /// рукавов масса толще самих рукавов, одного этого утолщения хватало, чтобы кольцо приняли
        /// на всю компоненту — и оно уходило вдоль тонких рукавов той самой вырожденной петлёй, ради
        /// запрета которой §5 и написан. Рукав получал две цепи гор по бокам вместо одной посередине.
        ///
        /// Утверждается ровно то, что меняет правило, и утверждается ГЕОМЕТРИЕЙ, а не счётом осей:
        /// счёт «колец 1, скелетных 3» сошёлся бы и по неверной причине. Над серединой рукава обязан
        /// идти скелет, и кольца там не должно быть даже близко: если бы оно текло вдоль рукава, оно
        /// прошло бы в 0.36R от осевой (рукав толщиной 1.36R, кольцо на уровне R).
        /// </summary>
        static void ForkKeepsRingOffThinArms()
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

            var node = mask.WorldToGrid(new Vector2(250, 0));
            float nearestRing = float.MaxValue, nearestSkeleton = float.MaxValue, ringAtNode = float.MaxValue;
            foreach (var axis in AxisBuilder.Build(mask, field, radiusCells))
            {
                foreach (var p in axis.Points)
                {
                    float d = Vector2.Distance(p, midArm);
                    if (axis.FromRing)
                    {
                        nearestRing = Math.Min(nearestRing, d);
                        ringAtNode = Math.Min(ringAtNode, Vector2.Distance(p, node));
                    }
                    else nearestSkeleton = Math.Min(nearestSkeleton, d);
                }
            }

            // Кольцо обязано остаться НАД УЗЛОМ: узел толст, глубина под ним есть, он кольцо заслужил.
            // Без этой половины проверки правило «резать» неотличимо от правила «выбросить целиком» —
            // мутант «минимальный кусок = 20R» выкинул бы все дуги, и остальные проверки промолчали.
            Check("Развилка: над узлом кольцо осталось", ringAtNode <= 2f * radiusCells,
                  $"ближайшая дуга кольца в {ringAtNode:0.0} ячейках от узла — куски кольца не доживают");

            Check("Развилка: над серединой рукава идёт скелетная ось",
                  nearestSkeleton <= radiusCells,
                  $"ближайшая скелетная ось в {nearestSkeleton:0.0} ячейках при допуске {radiusCells:0.0}");
            Check("Развилка: кольцо на рукав не заходит",
                  nearestRing > 2f * radiusCells,
                  $"кольцо прошло в {nearestRing:0.0} ячейках от осевой рукава — глубину снова меряют " +
                  "не местно");
        }

        /// <summary>
        /// Парная к предыдущей, и без неё та зелёная по неверной причине. Мутант «окно замера 0.3R»
        /// (меньше липшицева порога 0.6R) не примет НИКОГДА и НИГДЕ: колец не станет вовсе — а
        /// проверка развилки этому только обрадуется, она ровно отсутствия кольца и требует.
        ///
        /// Здесь наоборот: под ровной толстой массой кольцо обязано остаться ОДНИМ ЦЕЛЫМ ЗАМКНУТЫМ.
        /// Режется — значит замер дрожит у порога и провалы не зарастают; исчезло — значит окно
        /// слишком мало или минимальный кусок слишком длинен.
        /// </summary>
        static void RingOverThickMassStaysWhole()
        {
            var blob = Blob(Stroke(1, 20f, new Vector2(0, 0), new Vector2(300, 0)));
            var mask = MountainMask.Build(blob, 1f);
            var field = DistanceField.Build(mask);
            var rings = RingSelection.Select(field, mask.W, mask.H, 10f);

            Check("§5: под толстой массой кольцо одно", rings.Count == 1, $"кусков {rings.Count}");
            if (rings.Count != 1) return;
            Check("§5: и оно замкнуто", rings[0].Contour.Closed, "кольцо порезали на дуги");
        }

        /// <summary>
        /// §14: массив не зияет. Каждая ячейка маски обязана лежать близко к какой-нибудь оси —
        /// иначе там просто не встанет гора, и в массиве будет дыра.
        ///
        /// Проверка добавлена вместе с резкой колец (§5), потому что резка — ровно та правка, которой
        /// легче всего дыру и проделать: слишком строгий замер или слишком длинный минимальный кусок
        /// съедят кольцо, а скелет на освободившееся место встать не обязан. Порог 2R взят с запасом
        /// к устройству покрытия: кольца идут через 2R по глубине, значит от любой точки до своего
        /// кольца не больше R, и ещё R остаётся на кривизну и на стыки со скелетом.
        /// </summary>
        static void EveryMaskCellHasAnAxisNearby()
        {
            NoHoles("Развилка", Blob(Stroke(1, 30f, new Vector2(0, 0), new Vector2(250, 0)),
                                     Stroke(2, 30f, new Vector2(250, 0), new Vector2(440, 110)),
                                     Stroke(3, 30f, new Vector2(250, 0), new Vector2(440, -110))), 30f);
            NoHoles("Клякса", Blob(Stroke(1, 90f, new Vector2(150, 0), new Vector2(330, 60))), 90f);
        }

        static void NoHoles(string name, MountainBlob blob, float brushRadius)
        {
            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, brushRadius));
            var field = DistanceField.Build(mask);
            float radiusCells = 22f / mask.Cell;
            var axes = AxisBuilder.Build(mask, field, radiusCells);

            // Оси в растр, растр обратить, посчитать расстояние — тем же инструментом, что и толщину.
            var stamped = new byte[mask.W * mask.H];
            foreach (var axis in axes)
            {
                for (int i = 1; i < axis.Points.Count; i++)
                {
                    Vector2 a = axis.Points[i - 1], b = axis.Points[i];
                    int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(a, b) * 2f));
                    for (int t = 0; t <= steps; t++)
                    {
                        Vector2 p = a + (b - a) * ((float)t / steps);
                        int x = (int)Math.Round(p.X), y = (int)Math.Round(p.Y);
                        if (x >= 0 && y >= 0 && x < mask.W && y < mask.H) stamped[y * mask.W + x] = 1;
                    }
                }
            }
            var inverted = new byte[stamped.Length];
            for (int i = 0; i < inverted.Length; i++) inverted[i] = stamped[i] != 0 ? (byte)0 : (byte)1;
            var toAxis = DistanceField.Build(inverted, mask.W, mask.H);

            float worst = 0f;
            for (int i = 0; i < mask.Cells.Length; i++)
                if (mask.Cells[i] != 0 && toAxis[i] > worst) worst = toAxis[i];

            Check($"§14 «{name}»: дыр в массиве нет", worst <= 2f * radiusCells,
                  $"самая дальняя ячейка в {worst:0.0} ячейках от оси при допуске {2f * radiusCells:0.0}");
        }

        /// <summary>
        /// Продлеваются только СВОБОДНЫЕ концы. Конец, появившийся от резки покрытием, продлевать
        /// нельзя: он уйдёт внутрь массы, где кольцо уже проложило ось, и посадит там вторую цепь гор
        /// поперёк первой.
        ///
        /// Фикстура — гантель: два толстых блина, соединённых тонкой перемычкой. Под блинами кольца
        /// принимаются и покрывают их целиком, перемычка для кольца слишком тонка и достаётся
        /// скелету. Оба конца этой оси — срезы покрытием, значит она обязана остаться в перемычке.
        /// Мутант: продлевать, не глядя на Tip0/Tip1, — ось уползёт в блины на восемь десятков ячеек.
        /// </summary>
        static void CoverageCutEndsAreNotExtended()
        {
            var left = Stroke(1, 40f, new Vector2(0, 0), new Vector2(0, 0));
            var right = Stroke(2, 40f, new Vector2(200, 0), new Vector2(200, 0));
            var neck = Stroke(3, 15f, new Vector2(0, 0), new Vector2(200, 0));

            var blob = Blob(left, right, neck);
            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 15f));
            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, 22f / mask.Cell);

            int rings = 0, skel = 0;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var axis in axes)
            {
                if (axis.FromRing) { rings++; continue; }
                skel++;
                foreach (var p in axis.Points)
                {
                    float x = mask.GridToWorld(p.X, p.Y).X;
                    if (x < min) min = x;
                    if (x > max) max = x;
                }
            }

            Check("Гантель: у каждого блина своё кольцо", rings == 2, $"колец {rings}, ждали 2");
            Check("Гантель: перемычка досталась скелету", skel >= 1, "оси в перемычке нет вовсе");
            Check("Гантель: срезанные покрытием концы не продлеваются",
                  skel >= 1 && min > 30f && max < 170f,
                  $"ось ушла за перемычку: X от {min:0} до {max:0}, ждали внутри 30…170");
        }

        /// <summary>
        /// Замкнутая ось обязана дожить до конца доводки замкнутой. Шов тут хрупок по устройству: у
        /// цикла первая и последняя точки — одна и та же ячейка, но подтяжка к гребню считает им
        /// РАЗНЫЕ касательные (у первой сосед справа, у последней — слева) и может развести их до
        /// пяти ячеек, а замкнутой ось признаётся при зазоре меньше двух. Разъехались — ось сочтут
        /// открытой и выпустят из шва два отростка внутрь массы. Дальше это важно и нарезке (§8
        /// режет замкнутую ось иначе).
        /// </summary>
        static void RingStrokeKeepsClosedAxis()
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 72; i++)
            {
                double t = i / 72.0 * Math.PI * 2.0;
                pts.Add(new Vector2(300f + 130f * (float)Math.Cos(t), 300f + 130f * (float)Math.Sin(t)));
            }
            var blob = Blob(Stroke(1, 30f, pts.ToArray()));
            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 30f));
            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, 22f / mask.Cell);

            Check("Кольцевой мазок: ровно одна ось", axes.Count == 1, $"осей {axes.Count}");
            if (axes.Count != 1) return;

            var axis = axes[0];
            float seam = Vector2.Distance(axis.Points[0], axis.Points[axis.Points.Count - 1]);
            Check("Кольцевой мазок: ось замкнута", axis.Closed && !axis.FromRing,
                  $"замкнута = {axis.Closed}, от кольца = {axis.FromRing}");
            Check("Кольцевой мазок: шов не разошёлся", seam < AxisBuilder.ClosedGap,
                  $"зазор {seam:0.00} ячейки при пороге {AxisBuilder.ClosedGap}");
        }

        /// <summary>
        /// Утоньшение отказалось от проверки границ в горячем цикле, потому что активными бывают
        /// только внутренние ячейки. Держится это на том, что габариты пятна считаются С УЧЁТОМ
        /// радиуса кисти, и поле в шесть ячеек кладётся уже поверх них. Если это когда-нибудь
        /// перестанет быть правдой, масса вылезет на кромку и уцелеет там толстым ободом — молча.
        /// Мутант: убрать из StrokeGeometry.Bounds расширение на радиус.
        /// </summary>
        static void MaskBorderIsAlwaysBackground()
        {
            var blob = Blob(Stroke(1, 120f, new Vector2(0, 0), new Vector2(300, 200)));
            var mask = MountainMask.Build(blob, MountainMask.ChooseCell(22f, 120f));

            bool clean = true;
            for (int x = 0; x < mask.W; x++) if (mask.At(x, 0) || mask.At(x, mask.H - 1)) clean = false;
            for (int y = 0; y < mask.H; y++) if (mask.At(0, y) || mask.At(mask.W - 1, y)) clean = false;

            Check("Маска: кромка всегда фон", clean, "масса дошла до края сетки");
        }

        /// <summary>Два прогона — один результат. Порядок обхода словарей на пути к осям задан явно
        /// (ячейки скелета по возрастанию индекса, сортировки с разводом ничьих), но правило это
        /// негласное и ломается незаметно. Мутант: любой обход по Dictionary без явного порядка.</summary>
        static void AxesAreReproducible()
        {
            var first = CapsuleAxes(out var mask);
            var second = AxisBuilder.Build(mask, DistanceField.Build(mask), 22f / mask.Cell);

            bool same = first.Count == second.Count;
            for (int i = 0; same && i < first.Count; i++)
            {
                if (first[i].Points.Count != second[i].Points.Count) { same = false; break; }
                for (int k = 0; k < first[i].Points.Count; k++)
                    if (first[i].Points[k] != second[i].Points[k]) { same = false; break; }
            }
            Check("Повторяемость: два прогона дают те же оси", same, "результат разъехался между прогонами");
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

                // Отбор колец меряется рядом: замер местной глубины (§5) стоит окна на точку контура,
                // и это единственное место конвейера, где цена растёт от длины контура, а не площади.
                var field = DistanceField.Build(mask);
                var ringWatch = System.Diagnostics.Stopwatch.StartNew();
                int rings = RingSelection.Select(field, mask.W, mask.H, r / mask.Cell).Count;
                ringWatch.Stop();

                Console.WriteLine($"кисть = {ratio:F0}·R: сетка {mask.W}×{mask.H}, толщина {brush / mask.Cell:F0} ячеек, " +
                                  $"утоньшение {watch.Elapsed.TotalMilliseconds:F1} мс, " +
                                  $"кольца ({rings} шт.) {ringWatch.Elapsed.TotalMilliseconds:F1} мс");
            }
        }

        // ── §8 «Нарезка на звенья», §10 порядок, §11 ярусы ──────────────────────────────────────

        /// <summary>
        /// Целая координата сетки — ЦЕНТР ячейки, а не её угол. Растеризация решает судьбу ячейки по
        /// центру, поле расстояний считает значение там же — значит и обратный перевод в мир обязан
        /// давать ту же точку. Иначе весь нарисованный массив съезжает от мазка на полклетки: мало,
        /// но систематически и во все стороны сразу.
        /// Мутант: убрать полклетки из GridToWorld — закрашенные ячейки окажутся за краем кисти.
        /// </summary>
        static void GridPointIsTheCellCentre()
        {
            var stroke = Stroke(1, 40f, new Vector2(0, 0), new Vector2(120, 60));
            var mask = MountainMask.Build(Blob(stroke), MountainMask.ChooseCell(22f, 40f));

            float worst = 0f;
            for (int y = 0; y < mask.H; y++)
                for (int x = 0; x < mask.W; x++)
                {
                    if (!mask.At(x, y)) continue;
                    var world = mask.GridToWorld(x, y);
                    float over = StrokeGeometry.DistanceToStroke(world, stroke) - stroke.Radius;
                    if (over > worst) worst = over;
                }

            Check("Сетка: целая координата — центр ячейки", worst < 0.05f * mask.Cell,
                  $"закрашенная ячейка уехала за кисть на {worst / mask.Cell:0.###} ячейки");
        }

        /// <summary>
        /// Число звеньев подбирается по ОТНОШЕНИЮ шага к цели, а не по разности и не округлением.
        /// Ось в 1.45·T: и «сколько целых поместилось», и «округлить» дают одно звено, а правильное
        /// правило — два, потому что 0.725·T ближе к цели В РАЗАХ, чем 1.45·T (порог переключения —
        /// среднее геометрическое √2, а не середина 1.5).
        /// Мутанты: убрать доводку count+1; заменить критерий на округление.
        /// </summary>
        static void LinkCountIsChosenByRatio()
        {
            var links = SplitLine(145f, false, 100f, 0f, 1f, 1);
            Check("Нарезка: 1.45·T режется надвое", links.Count == 2, $"звеньев {links.Count}, ждали 2");

            var one = SplitLine(120f, false, 100f, 0f, 1f, 1);
            Check("Нарезка: 1.2·T остаётся одним звеном", one.Count == 1, $"звеньев {one.Count}, ждали 1");
        }

        /// <summary>
        /// Метрика анизотропна, и с 2026-08-14 в ОБРАТНУЮ §8 сторону: вертикаль дешевле горизонтали
        /// в a раз, поэтому вертикальный участок оси набирает целевую длину медленнее и получает
        /// МЕНЬШЕ звеньев — горы на нём стоят реже. Причина в комментарии к MetricLength: гора от
        /// наклона звена не меняется, меняется только то, как соседи закрывают друг друга, и на
        /// вертикали прежний частый шаг хоронил дальнюю гору под ближней.
        ///
        /// Фикстура — две оси одной геометрической длины, вдоль и поперёк.
        /// Мутанты: считать длину обычным гипотенузным способом (числа сравняются); вернуть §8
        /// как было (знак перевернётся, и проверка поймает именно это).
        /// </summary>
        static void AnisotropySpreadsVerticalLinks()
        {
            var flat = SplitLine(200f, false, 100f, 0f, 1.6f, 1);
            var tall = SplitLine(200f, true, 100f, 0f, 1.6f, 1);
            Check("Анизотропия: вертикаль режется РЕЖЕ", tall.Count < flat.Count,
                  $"поперёк {flat.Count}, вдоль {tall.Count} — ждали, что вдоль меньше");
        }

        static void JitterSpreadsLengthsButKeepsThemClose()
        {
            var even = SplitLine(1000f, false, 100f, 0f, 1f, 7);
            var mixed = SplitLine(1000f, false, 100f, 0.06f, 1f, 7);

            Check("Разброс: без него звенья равны", Spread(even) < 1.001f, $"разброс {Spread(even):0.###}");
            Check("Разброс: с ним звенья разные", Spread(mixed) > 1.02f, $"разброс {Spread(mixed):0.###}");
            Check("Разброс: но не больше 1.22 раза", Spread(mixed) < 1.22f, $"разброс {Spread(mixed):0.###}");

            // Двадцать звеньев при разбросе в четверть — то место, где видно, нормированы ли веса на
            // свою сумму. Ненормированные дают правильные длины всем, кроме ПОСЛЕДНЕГО: он получает
            // остаток, в который стекается вся накопленная ошибка, и при большом разбросе вырождается.
            // Предел (1+j)/(1−j) выполняется по построению, поэтому проверка не на глазок.
            var wild = SplitLine(2000f, false, 100f, 0.25f, 1f, 7);
            Check("Разброс: даже большой остаётся в своих пределах", Spread(wild) < 1.25f / 0.75f,
                  $"разброс {Spread(wild):0.###} при пределе {1.25f / 0.75f:0.###}, звеньев {wild.Count}");
        }

        /// <summary>
        /// У замкнутой оси шов ставится в случайное место: иначе на всех кольцах массива стык звеньев
        /// приходился бы на одну сторону, и правильность видна глазом. Разброс длин выключен нарочно —
        /// иначе оси разъехались бы и без сдвига шва, и проверка стала бы пустой.
        /// Мутант: убрать прокрутку точек при closed.
        /// </summary>
        static void ClosedAxisSeamMoves()
        {
            var first = SplitRing(100f, 100f, 0f, 3);
            var second = SplitRing(100f, 100f, 0f, 11);

            Check("Кольцо: режется минимум надвое", first.Count >= 2, $"звеньев {first.Count}");
            if (first.Count == 0 || second.Count == 0) return;

            float moved = Vector2.Distance(first[0].Pts[0], second[0].Pts[0]);
            Check("Кольцо: шов стоит не всегда на одном месте", moved > 1f,
                  $"шов сдвинулся на {moved:0.##} — ждали заметного сдвига");
        }

        /// <summary>
        /// §14 «короткое звено между развилками». Прототип выбрасывает ось короче 0.15·T целиком, и
        /// на месте короткой ветки между двумя развилками остаётся ДЫРА в массиве. Здесь ось по длине
        /// не выбрасывается: она становится одним звеном, а строить ли над ним гору, решает уже
        /// геометрия доли и подошвы.
        /// Мутант: вернуть отброс «total &lt; 0.15·T» — звеньев станет ноль.
        /// </summary>
        static void ShortBranchGetsALinkNotAHole()
        {
            var links = SplitLine(12f, false, 100f, 0f, 1f, 5, tip0: false, tip1: false);
            Check("Коротышка: получает звено, а не дырку", links.Count == 1, $"звеньев {links.Count}, ждали 1");
            if (links.Count == 1)
                Check("Коротышка: звено накрывает её целиком", Near(links[0].Length, 12f, 0.01f),
                      $"длина звена {links[0].Length:0.##} вместо 12");
        }

        /// <summary>
        /// §14 «вылет за мазок»: подошва растягивается только туда, где есть сосед. Сосед есть у
        /// каждого стыка звеньев, а ещё — за концом оси, упёршимся в развилку или срезанным покрытием
        /// кольца; свободен лишь настоящий конец гряды.
        /// Мутант: определять свободный конец по номеру звена, как во временной резке шипа, — тогда
        /// у оси, продолжающейся другой осью, крайняя гора недосчитается растяжения и в массиве
        /// появится шов.
        /// </summary>
        static void FreeEndsOnlyAtTips()
        {
            var half = SplitLine(300f, false, 100f, 0f, 1f, 2, tip0: true, tip1: false);
            Check("Концы: свободный конец узнан", half.Count >= 2 && half[0].FreeStart,
                  "начало оси не отмечено свободным");
            Check("Концы: конец у развилки свободным не считается",
                  half.Count >= 2 && !half[half.Count - 1].FreeEnd,
                  "конец у развилки отмечен свободным — подошву туда не растянут");

            var ring = SplitRing(100f, 100f, 0f, 4);
            bool anyFree = false;
            foreach (var link in ring) if (link.FreeStart || link.FreeEnd) anyFree = true;
            Check("Концы: у замкнутой оси свободных нет", !anyFree, "у кольца нашёлся свободный конец");
        }

        /// <summary>
        /// Звенья обязаны выстилать ось без дыр и без нахлёстов: конец одного — ровно начало
        /// следующего, а первое и последнее упираются в концы самой оси. Дыра здесь — это разрыв в
        /// цепи гор, который никакой заливкой не закрыть.
        ///
        /// Фикстур две, и они нарочно разные. При T = 100 метки ложатся ТОЧНО на изломы оси, и это
        /// ловит мутанта «относить промежуточную точку по &lt;= вместо &lt;» — точка досталась бы и
        /// звену, и его соседу, и в звене оказалась бы пара совпадающих подряд. При T = 130 метки
        /// падают между изломами, и излом обязан попасть ВНУТРЬ звена: это ловит мутанта «не
        /// переносить промежуточные точки вовсе», от которого звено превращается в хорду, а гряда на
        /// повороте срезает угол и вылезает за мазок.
        /// </summary>
        static void LinksTileTheAxis()
        {
            var pts = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(200, 100),
            };
            var widths = new List<float> { 20, 20, 20, 20 };
            var links = LinkSplitter.Split(pts, widths, widths, false, true, true, 100f, 0f, 1f,
                                           new Mulberry32(9));

            bool chained = links.Count == 3;
            for (int i = 1; i < links.Count; i++)
            {
                var end = links[i - 1].Pts[links[i - 1].Pts.Count - 1];
                if (Vector2.Distance(end, links[i].Pts[0]) > 1e-4f) chained = false;
            }
            Check("Звенья: цепь без разрывов", chained, $"звеньев {links.Count}, стык разошёлся");

            if (links.Count == 0) return;
            var last = links[links.Count - 1].Pts;
            Check("Звенья: цепь начинается и кончается концами оси",
                  Vector2.Distance(links[0].Pts[0], pts[0]) < 1e-4f &&
                  Vector2.Distance(last[last.Count - 1], pts[pts.Count - 1]) < 1e-4f,
                  "крайние звенья не дотянулись до концов оси");

            float sum = 0f;
            foreach (var link in links) sum += link.Length;
            Check("Звенья: сумма длин — вся ось", Near(sum, 300f, 0.01f), $"сумма {sum:0.##} вместо 300");

            bool doubled = false;
            foreach (var link in links)
                for (int i = 1; i < link.Pts.Count; i++)
                    if (Vector2.Distance(link.Pts[i - 1], link.Pts[i]) < 1e-4f) doubled = true;
            Check("Звенья: точка на самом позвонке не задваивается", !doubled,
                  "в звене нашлись две совпадающие точки подряд");

            var shifted = LinkSplitter.Split(pts, widths, widths, false, true, true, 130f, 0f, 1f,
                                             new Mulberry32(9));
            bool keptBend = shifted.Count == 2 &&
                            HasVertex(shifted[0], new Vector2(100, 0)) &&
                            HasVertex(shifted[1], new Vector2(100, 100));
            Check("Звенья: излом оси попадает внутрь звена, а не срезается хордой", keptBend,
                  $"звеньев {shifted.Count}, изломы внутрь не попали");
        }

        static bool HasVertex(AxisLink link, Vector2 p)
        {
            foreach (var q in link.Pts) if (Vector2.Distance(p, q) < 1e-3f) return true;
            return false;
        }

        /// <summary>
        /// §11: ярус — это номер слоя 2R, в котором стоит середина звена. Номер ограничен сверху
        /// числом ярусов: у толстой массы глубина уезжает далеко, и без ограничения появился бы ярус,
        /// которому не назначено ни высоты, ни (в будущем) прорисовки.
        /// Мутант: убрать ограничение — глубокое звено получит ярус 4 вместо 2.
        /// </summary>
        static void TiersFollowDepthBands()
        {
            var settings = new MountainSettings { Radius = 10f, Tiers = 3, EdgeHeight = 0.55f };
            var links = new List<AxisLink>
            {
                new AxisLink { MidDepth = 5f },     // слой 0
                new AxisLink { MidDepth = 25f },    // слой 1
                new AxisLink { MidDepth = 90f },    // слой 4, но ярусов всего три
            };
            MountainGeometry.AssignTiers(links, settings);

            Check("Ярусы: край мазка — нулевой", links[0].Tier == 0, $"ярус {links[0].Tier}");
            Check("Ярусы: следующий слой — первый", links[1].Tier == 1, $"ярус {links[1].Tier}");
            Check("Ярусы: глубина обрезана числом ярусов", links[2].Tier == 2, $"ярус {links[2].Tier}");
            Check("Ярусы: у края высота ниже, чем в сердцевине",
                  Near(links[0].TierScale, 0.55f, 1e-4f) && Near(links[2].TierScale, 1f, 1e-4f),
                  $"край {links[0].TierScale:0.###}, сердцевина {links[2].TierScale:0.###}");
        }

        /// <summary>
        /// Высота нормируется на самый глубокий ярус ЭТОГО пятна, а не на предельный номер. У узкого
        /// мазка, где слоёв всего два, самые глубокие горы обязаны выйти полной высоты — иначе весь
        /// массив окажется приземистым без всякой причины.
        /// Мутант: делить на (Tiers − 1) — глубокое звено получит 0.775 вместо единицы.
        /// </summary>
        static void TierHeightIsNormalizedToThisBlob()
        {
            var settings = new MountainSettings { Radius = 10f, Tiers = 3, EdgeHeight = 0.55f };
            var links = new List<AxisLink>
            {
                new AxisLink { MidDepth = 5f },
                new AxisLink { MidDepth = 25f },
            };
            MountainGeometry.AssignTiers(links, settings);

            Check("Ярусы: самый глубокий ярус пятна даёт полную высоту",
                  Near(links[1].TierScale, 1f, 1e-4f), $"множитель {links[1].TierScale:0.###}");
        }

        /// <summary>
        /// §10 порядок маляра: дальняя гора кладётся раньше, ближняя закрывает её собой. Ближе — это
        /// НИЖЕ по экрану, то есть меньше Y. При равной глубине разбираем по ярусу (§14 «глубина
        /// между слоями»): вложенное кольцо стоит глубже в массе, значит оно дальше.
        /// Мутанты: сортировать по возрастанию (массив вывернется наизнанку); убрать разбор по ярусу.
        /// </summary>
        static void PainterOrderIsFarToNear()
        {
            var near = new MountainShape { Depth = 10f, Tier = 0, Apex = new Vector2(1, 0) };
            var far = new MountainShape { Depth = 30f, Tier = 0, Apex = new Vector2(2, 0) };
            var deep = new MountainShape { Depth = 10f, Tier = 2, Apex = new Vector2(3, 0) };
            var shapes = new List<MountainShape> { near, far, deep };

            MountainGeometry.SortForPainting(shapes);
            Check("Маляр: дальняя гора первая", ReferenceEquals(shapes[0], far),
                  $"первой легла гора с вершиной {shapes[0].Apex.X}");
            Check("Маляр: при равной глубине глубокий ярус дальше",
                  ReferenceEquals(shapes[1], deep) && ReferenceEquals(shapes[2], near),
                  $"порядок {shapes[1].Apex.X} → {shapes[2].Apex.X}, ждали 3 → 1");
        }

        /// <summary>
        /// Сквозная проверка всего конвейера: мазок → горы. Она ловит то, чего не ловит ни одна
        /// проверка по частям: что цепочка «маска → поле → оси → звенья → доли → горы» вообще
        /// проходится до конца и даёт гряду, а не пустоту.
        ///
        /// Мутант: зерно, взятое не от старшего мазка, а от чего-нибудь текучего. Оговорка: РАЗВОД
        /// НИЧЬИХ по исходному номеру в порядке маляра этой проверкой не подтверждается — на
        /// одинаковом входе сортировка повторяема и без него. Этот ключ поставлен на будущее, когда
        /// счёт уедет в фон и порядок сборки перестанет быть предсказуемым; проверить его нечем.
        /// </summary>
        static void GeometryIsReproducible()
        {
            var settings = new MountainSettings { Radius = 22f };
            var first = MountainGeometry.Build(Blob(Stroke(1, 40f, new Vector2(0, 0), new Vector2(300, 40))), settings);
            var second = MountainGeometry.Build(Blob(Stroke(1, 40f, new Vector2(0, 0), new Vector2(300, 40))), settings);

            Check("Конвейер: из мазка выросли горы", first.Count >= 3, $"гор {first.Count}");
            bool same = first.Count == second.Count;
            for (int i = 0; same && i < first.Count; i++)
                if (Vector2.Distance(first[i].Apex, second[i].Apex) > 1e-4f ||
                    !Near(first[i].Depth, second[i].Depth, 1e-4f)) same = false;
            Check("Конвейер: два прогона дают тот же рисунок", same, "горы разъехались между прогонами");
        }

        /// <summary>Во сколько раз самое длинное звено длиннее самого короткого.</summary>
        static float Spread(List<AxisLink> links)
        {
            if (links.Count == 0) return 1f;
            float min = float.PositiveInfinity, max = 0f;
            foreach (var link in links)
            {
                if (link.Length < min) min = link.Length;
                if (link.Length > max) max = link.Length;
            }
            return min > 0f ? max / min : float.PositiveInfinity;
        }

        /// <summary>Прямая ось заданной длины, вдоль или поперёк экрана, с постоянной шириной.</summary>
        static List<AxisLink> SplitLine(float length, bool vertical, float target, float jitter,
                                        float aniso, uint seed, bool tip0 = true, bool tip1 = true)
        {
            var pts = new List<Vector2>();
            var widths = new List<float>();
            for (int i = 0; i <= 20; i++)
            {
                float t = length * i / 20f;
                pts.Add(vertical ? new Vector2(0, t) : new Vector2(t, 0));
                widths.Add(20f);
            }
            return LinkSplitter.Split(pts, widths, widths, false, tip0, tip1, target, jitter, aniso,
                                      new Mulberry32(seed));
        }

        /// <summary>Замкнутая ось — окружность заданного радиуса.</summary>
        static List<AxisLink> SplitRing(float radius, float target, float jitter, uint seed)
        {
            var pts = new List<Vector2>();
            var widths = new List<float>();
            for (int i = 0; i < 64; i++)
            {
                double a = i / 64.0 * Math.PI * 2.0;
                pts.Add(new Vector2(radius * (float)Math.Cos(a), radius * (float)Math.Sin(a)));
                widths.Add(15f);
            }
            return LinkSplitter.Split(pts, widths, widths, true, false, false, target, jitter, 1f,
                                      new Mulberry32(seed));
        }

        static bool IsSimple(List<Vector2> loop)
        {
            int n = loop.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (Crosses(loop[i], loop[i + 1], loop[j], loop[(j + 1) % n])) return false;
                }
            return true;
        }

        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float s1 = Side(a, b, c), s2 = Side(a, b, d), s3 = Side(c, d, a), s4 = Side(c, d, b);
            return ((s1 > 0 && s2 < 0) || (s1 < 0 && s2 > 0)) && ((s3 > 0 && s4 < 0) || (s3 < 0 && s4 > 0));
        }

        static float Side(Vector2 a, Vector2 b, Vector2 p)
            => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

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

        static AxisLink Vertical(float length, float halfWidth)
        {
            var link = new AxisLink
            {
                Pts = new List<Vector2> { new Vector2(0, 0), new Vector2(0, length) },
                Mid = new Vector2(0, length * 0.5f),
                Tan = new Vector2(0, 1),
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
