using System.Collections.Generic;
using UnityEngine;
// Короткое имя Vector2 занято дважды (UnityEngine и System.Numerics) — карта живёт в System.Numerics,
// поэтому здесь оно и закреплено за ним, а из UnityEngine нужен только Mathf/Debug.
using Vector2 = System.Numerics.Vector2;

namespace WorldGen.Generation
{
    /// <summary>Editor-only [ContextMenu] self-tests для кисти рек. Повесить на любой объект сцены
    /// и запускать пункты из контекстного меню компонента (в проекте нет CLI-раннера — самотесты
    /// печатают PASS/FAIL в консоль).</summary>
    public class RiverPaintSelfTests : MonoBehaviour
    {
        static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.001f;

        // Мутанты, которых валит этот тест:
        //   • «оставить клетки воды на концах»  → первая точка станет (0,0), а не берегом у (5,0);
        //   • «начать прямо с клетки суши»      → первая точка станет (10,0);
        //   • «резать только концы, как раньше» → мазок через залив останется ОДНОЙ рекой, идущей
        //     поверх воды, — ровно то, что ДМ забраковал;
        //   • «мазок по воде — тоже река»       → непустой результат там, где суши не было;
        //   • «перепутать устья местами»        → море и озеро поменяются концами, а значит цвет
        //     устья и скругление достанутся не тому концу. Ради него концы РАЗНЫЕ: на симметричной
        //     заготовке подмена была бы не видна.
        [ContextMenu("Self-Test: River Split At Water")]
        public void SelfTestSplitAtWater()
        {
            bool ok = true;

            // Море → суша → суша → озеро: одна река, оба конца режутся по кромке и заходят за неё на 2.
            var sites = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0) };
            var kind = new List<RiverMouth> { RiverMouth.Sea, RiverMouth.None, RiverMouth.None, RiverMouth.Lake };
            var split = RiverPaintOps.SplitAtWater(sites, kind, overshoot: 2f);
            if (split.Count != 1)
            { Debug.LogError($"FAIL split: кусков {split.Count}, ждали 1 — вода была только по краям"); ok = false; }
            else
            {
                var points = split[0].Points;
                if (points.Count != 4)
                { Debug.LogError($"FAIL split: точек {points.Count}, ждали 4 (берег + две клетки суши + берег)"); ok = false; }
                else
                {
                    if (!Near(points[0], new Vector2(3, 0)))
                    { Debug.LogError($"FAIL split: начало {points[0]}, ждали (3,0) — берег на (5,0) (середина между центрами суши и воды, она же общее ребро клеток Вороного) плюс заход 2 в сторону воды"); ok = false; }
                    if (!Near(points[1], new Vector2(10, 0)) || !Near(points[2], new Vector2(20, 0)))
                    { Debug.LogError("FAIL split: клетки суши внутри русла должны остаться как есть"); ok = false; }
                    if (!Near(points[3], new Vector2(27, 0)))
                    { Debug.LogError($"FAIL split: устье {points[3]}, ждали (27,0) — берег на (25,0) плюс заход 2"); ok = false; }
                }
                if (split[0].StartMouth != RiverMouth.Sea || split[0].EndMouth != RiverMouth.Lake)
                { Debug.LogError($"FAIL split: устья определились как {split[0].StartMouth}/{split[0].EndMouth}, ждали Sea/Lake — от этого зависит и цвет конца, и скругление"); ok = false; }
            }

            // Тот же мазок задом наперёд обязан поменять устья местами.
            var reversedSites = new List<Vector2>(sites); reversedSites.Reverse();
            var reversedKind = new List<RiverMouth>(kind); reversedKind.Reverse();
            var reversed = RiverPaintOps.SplitAtWater(reversedSites, reversedKind, 2f);
            if (reversed.Count != 1 || reversed[0].StartMouth != RiverMouth.Lake || reversed[0].EndMouth != RiverMouth.Sea)
            { Debug.LogError("FAIL split: у перевёрнутого мазка устья обязаны поменяться местами (ждали Lake/Sea)"); ok = false; }

            // ГЛАВНОЕ правило: озеро ПОСРЕДИ мазка режет его на две реки, обе в это озеро впадают.
            var through = RiverPaintOps.SplitAtWater(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0) },
                new List<RiverMouth> { RiverMouth.None, RiverMouth.None, RiverMouth.Lake, RiverMouth.None }, 2f);
            if (through.Count != 2)
            { Debug.LogError($"FAIL split: кусков {through.Count}, ждали 2 — через водоём река не течёт, она в нём кончается"); ok = false; }
            else
            {
                if (through[0].StartMouth != RiverMouth.None || through[0].EndMouth != RiverMouth.Lake)
                { Debug.LogError($"FAIL split: у первого куска концы {through[0].StartMouth}/{through[0].EndMouth}, ждали None/Lake"); ok = false; }
                if (through[1].StartMouth != RiverMouth.Lake || through[1].EndMouth != RiverMouth.None)
                { Debug.LogError($"FAIL split: у второго куска концы {through[1].StartMouth}/{through[1].EndMouth}, ждали Lake/None"); ok = false; }
                // Ни одна точка не должна лежать дальше кромки вглубь озера, чем заход.
                foreach (var p in through[0].Points)
                    if (p.X > 17.001f)
                    { Debug.LogError($"FAIL split: точка {p} залезла в озеро глубже кромки (15,0) плюс заход 2 — река рисуется поверх воды"); ok = false; break; }
            }

            // Мазок целиком по воде — не река.
            var allWater = RiverPaintOps.SplitAtWater(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0) },
                new List<RiverMouth> { RiverMouth.Sea, RiverMouth.Sea }, 2f);
            if (allWater.Count != 0)
            { Debug.LogError("FAIL split: мазок, не задевший сушу, обязан выбрасываться"); ok = false; }

            // Одна клетка суши без соседей-воды — точка, а не река.
            var single = RiverPaintOps.SplitAtWater(new List<Vector2> { new Vector2(0, 0) },
                new List<RiverMouth> { RiverMouth.None }, 2f);
            if (single.Count != 0)
            { Debug.LogError("FAIL split: из одной точки река не получается"); ok = false; }

            Debug.Log(ok ? "Self-Test River Split: PASS" : "Self-Test River Split: FAIL");
        }

        // Мутант, которого валит этот тест: «сглаживание = ломаная» (линейная интерполяция).
        // Излом специально несимметричный — на прямой линии кривая и ломаная совпали бы, и тест
        // был бы пустым.
        [ContextMenu("Self-Test: River Smooth")]
        public void SelfTestSmooth()
        {
            bool ok = true;
            var anchors = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 10), new Vector2(20, 0), new Vector2(30, 10) };
            var curve = RiverPaintOps.Smooth(anchors, subdivisions: 8);

            if (curve.Count <= anchors.Count)
            { Debug.LogError($"FAIL smooth: точек {curve.Count} — сглаживание обязано УПЛОТНЯТЬ ломаную"); ok = false; }

            // Кривая проходит ЧЕРЕЗ опорные точки (Catmull-Rom интерполирует, а не аппроксимирует).
            foreach (var a in anchors)
            {
                bool hit = false;
                foreach (var p in curve) if (Near(p, a)) { hit = true; break; }
                if (!hit) { Debug.LogError($"FAIL smooth: опорная точка {a} потерялась — русло пройдёт мимо клетки, по которой вели кистью"); ok = false; }
            }

            // Между опорными точками кривая ОТКЛОНЯЕТСЯ от хорды — иначе это та же ломаная.
            float maxDev = 0f;
            for (int i = 0; i < curve.Count; i++)
            {
                float d = RiverPaintOps.DistanceToPolyline(anchors, curve[i]);
                if (d > maxDev) maxDev = d;
            }
            if (maxDev < 0.2f)
            { Debug.LogError($"FAIL smooth: максимальное отклонение от ломаной {maxDev:F3} — кривая совпала с ломаной, сглаживания нет"); ok = false; }

            // Две точки сглаживать нечем — отдаём как есть (а не пустоту).
            var pair = RiverPaintOps.Smooth(new List<Vector2> { new Vector2(0, 0), new Vector2(5, 0) });
            if (pair.Count != 2)
            { Debug.LogError("FAIL smooth: русло из двух точек должно возвращаться нетронутым"); ok = false; }

            Debug.Log(ok ? "Self-Test River Smooth: PASS" : "Self-Test River Smooth: FAIL");
        }

        // Мутант, которого валит этот тест: «убрать сглаживание углов (Relax) из BuildCurve» —
        // русло осталось бы дёрганым, ровно как на карте ДМ. Заготовка — «пила» с амплитудой 3,
        // какую и дают неровно стоящие центры клеток; сплайн сам по себе её НЕ убирает (он честно
        // проходит через каждую точку), поэтому проверка не пустая.
        [ContextMenu("Self-Test: River Curve Tames Zigzag")]
        public void SelfTestCurveTamesZigzag()
        {
            bool ok = true;
            var anchors = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(10, 3), new Vector2(20, -3),
                new Vector2(30, 3), new Vector2(40, -3), new Vector2(50, 0)
            };

            float rawAmplitude = 0f;
            foreach (var a in anchors) rawAmplitude = Mathf.Max(rawAmplitude, Mathf.Abs(a.Y));
            if (rawAmplitude < 2.5f)
            { Debug.LogError("FAIL zigzag: заготовка без «пилы» — проверять нечего"); ok = false; }

            var curve = RiverPaintOps.BuildCurve(anchors, width: 6f);
            float amplitude = 0f;
            foreach (var p in curve)
                if (p.X > 12f && p.X < 38f)   // концы не трогаем: их держит устье, они и не должны спрямляться
                    amplitude = Mathf.Max(amplitude, Mathf.Abs(p.Y));

            if (amplitude > 1.5f)
            { Debug.LogError($"FAIL zigzag: размах «пилы» в русле {amplitude:F2} против {rawAmplitude:F2} у ломаной — сглаживание углов не работает, река будет вилять как на карте"); ok = false; }

            if (curve.Count <= anchors.Count)
            { Debug.LogError("FAIL zigzag: кривая обязана быть плотнее ломаной"); ok = false; }

            Debug.Log(ok ? "Self-Test River Curve: PASS" : "Self-Test River Curve: FAIL");
        }


        // Река — это ВОДА в маске суша/вода, из которой карта берёт цвет, ореол, обводку и песок.
        // Мутанты, которых валит этот тест:
        //   • «мерить расстояние до ближайшей ВЕРШИНЫ русла» → середина длинного прямого участка
        //     осталась бы сушей, и река вышла бы пунктиром из точек;
        //   • «чистить маску под каждую реку» → вторая река стирала бы первую, и о слиянии
        //     пересекающихся рек можно забыть;
        //   • «топить всю рамку русла» → залило бы прямоугольник вместо ленты.
        [ContextMenu("Self-Test: River Mask Union")]
        public void SelfTestMaskUnion()
        {
            bool ok = true;
            const int w = 100, h = 100;
            const float mapW = 100f, mapH = 100f;

            var across = new PaintedRiver
            {
                Id = 1, Width = 6f,
                Points = new List<Vector2> { new Vector2(10, 50), new Vector2(90, 50) }
            };
            var down = new PaintedRiver
            {
                Id = 2, Width = 6f,
                Points = new List<Vector2> { new Vector2(50, 10), new Vector2(50, 90) }
            };

            var alone = new bool[w * h];
            RiverMask.StampAll(alone, w, h, mapW, mapH, new List<PaintedRiver> { across });
            if (alone[20 * w + 50])
            { Debug.LogError("FAIL mask: поперечная река одна накрыла точку продольной — заготовка не проверяет объединение"); ok = false; }

            var both = new bool[w * h];
            RiverMask.StampAll(both, w, h, mapW, mapH, new List<PaintedRiver> { across, down });

            if (!both[50 * w + 50])
            { Debug.LogError("FAIL mask: перекрестье двух рек не стало водой"); ok = false; }
            if (!both[50 * w + 20])
            { Debug.LogError("FAIL mask: середина длинного прямого участка осталась сушей — расстояние считается до вершин, а не до отрезка, и река выйдет пунктиром"); ok = false; }
            if (!both[20 * w + 50])
            { Debug.LogError("FAIL mask: вторая река потеряла своё русло — маска объединяет реки, а не заменяет"); ok = false; }

            // Сужение (просьба ДМ): ползунок задаёт ширину У КОНЦОВ, тело реки заметно уже.
            // Река идёт по y=50 от x=10 до x=90, ширина 6 → полуширина 3 у концов и 1,35 в теле,
            // расширение занимает 12 (две ширины). Пиксель y=52 отстоит от оси на 2:
            //   • у конца (x=11) он ещё внутри русла,
            //   • в теле (x=30) уже снаружи.
            // Мутанты: «ширина постоянна» валит вторую проверку, «река тонкая везде» — первую.
            if (!both[52 * w + 11])
            { Debug.LogError("FAIL mask: у самого конца русло не полной ширины — устье воткнётся в водоём ниткой"); ok = false; }
            if (both[52 * w + 30])
            { Debug.LogError("FAIL mask: в теле реки русло такое же широкое, как у концов — сужения нет"); ok = false; }
            if (!both[50 * w + 30])
            { Debug.LogError("FAIL mask: ось реки в теле пересохла — сужение съело русло целиком"); ok = false; }
            if (both[54 * w + 11])
            { Debug.LogError("FAIL mask: в 4 от оси при полуширине 3 мокро — русло шире заданного (топится рамка, а не лента)"); ok = false; }
            if (both[20 * w + 20])
            { Debug.LogError("FAIL mask: угол карты, где рек нет, стал водой"); ok = false; }

            // Приток: конец, впадающий в ЧУЖУЮ реку, не раздаётся — иначе он вышел бы шире ствола,
            // в который впадает. Свободный исток того же притока раздаётся как обычно.
            // Приток идёт по x=50 от y=10 (свободный исток) до y=49 (упирается в ствол на y=50).
            // Мутанты: «раздавать оба конца» валит вторую проверку, «не раздавать ни одного» — первую.
            var trunk = new PaintedRiver
            {
                Id = 1, Width = 6f,
                Points = new List<Vector2> { new Vector2(10, 50), new Vector2(90, 50) }
            };
            var tributary = new PaintedRiver
            {
                Id = 2, Width = 6f,
                Points = new List<Vector2> { new Vector2(50, 10), new Vector2(50, 49) }
            };
            var joined = new bool[w * h];
            RiverMask.StampAll(joined, w, h, mapW, mapH, new List<PaintedRiver> { trunk, tributary });

            if (!joined[11 * w + 52])
            { Debug.LogError("FAIL mask: свободный исток притока не раздался — он должен вести себя как обычный конец"); ok = false; }
            if (joined[47 * w + 52])
            { Debug.LogError("FAIL mask: конец притока раздался у слияния — приток вышел шире ствола, в который впадает"); ok = false; }
            if (!joined[47 * w + 50])
            { Debug.LogError("FAIL mask: ось притока у слияния пересохла — сужение съело русло целиком"); ok = false; }

            // Тот же приток, но НЕ ДОВЕДЁННЫЙ до ствола на 6 (опоры мазка — центры клеток, и конец
            // мазка почти всегда чуть не дотягивается: ДМ отпускает кнопку, когда курсор коснулся
            // ствола на глаз). Такой конец обязан и сузиться, и ДОТЯНУТЬСЯ до чужой оси.
            // Мутанты: «мерить только точное касание» валит обе проверки сразу; «сузить, но не
            // дотягивать» валит проверку щели — приток повис бы рядом со стволом, не сливаясь.
            var shortOfTrunk = new PaintedRiver
            {
                Id = 2, Width = 6f,
                Points = new List<Vector2> { new Vector2(50, 10), new Vector2(50, 44) }
            };
            var snapped = new bool[w * h];
            RiverMask.StampAll(snapped, w, h, mapW, mapH, new List<PaintedRiver> { trunk, shortOfTrunk });

            if (!snapped[46 * w + 50] || !snapped[48 * w + 50])
            { Debug.LogError("FAIL mask: между недоведённым притоком и стволом осталась щель — конец не дотянули до чужой оси"); ok = false; }
            if (snapped[46 * w + 52])
            { Debug.LogError("FAIL mask: недоведённый приток раздался у слияния — сужение узнаёт только точное касание"); ok = false; }
            if (!snapped[11 * w + 52])
            { Debug.LogError("FAIL mask: у недоведённого притока пропал и свободный исток — сузили оба конца вместо одного"); ok = false; }

            Debug.Log(ok ? "Self-Test River Mask: PASS" : "Self-Test River Mask: FAIL");
        }

        // Мутант, которого валит этот тест: «расстояние до ближайшей ВЕРШИНЫ» вместо расстояния до
        // отрезка — клик по середине длинного участка перестал бы попадать по реке.
        [ContextMenu("Self-Test: River Hit Test")]
        public void SelfTestHitTest()
        {
            bool ok = true;
            var line = new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0) };

            float mid = RiverPaintOps.DistanceToPolyline(line, new Vector2(50, 3));
            if (Mathf.Abs(mid - 3f) > 0.001f)
            { Debug.LogError($"FAIL hit: расстояние {mid:F3}, ждали 3 — считать надо до ОТРЕЗКА (до ближайшей вершины вышло бы ~50)"); ok = false; }

            float beyond = RiverPaintOps.DistanceToPolyline(line, new Vector2(-10, 0));
            if (Mathf.Abs(beyond - 10f) > 0.001f)
            { Debug.LogError($"FAIL hit: за концом отрезка ждали 10, получили {beyond:F3} — проекция обязана зажиматься в [0,1]"); ok = false; }

            if (RiverPaintOps.DistanceToPolyline(new List<Vector2>(), Vector2.Zero) != float.MaxValue)
            { Debug.LogError("FAIL hit: пустое русло не должно попадать ни под какой клик"); ok = false; }

            Debug.Log(ok ? "Self-Test River Hit: PASS" : "Self-Test River Hit: FAIL");
        }

        // Мутант: «пустой мазок кладётся в историю» / «река не попадает в отмену» — Ctrl+Z начал бы
        // откатывать не то. Проверяется вперемешку с клеточным мазком: у них общая история.
        [ContextMenu("Self-Test: River Undo Interleaved")]
        public void SelfTestUndoInterleaved()
        {
            bool ok = true;
            var undo = new BrushUndoManager();
            var cell = new VoronoiCell(1, new Vector2(0, 0)) { Height = 0.5f, Humidity = 0.5f, Temperature = 0.5f };

            // Мазок 1 — клеточный: поднимаем высоту.
            undo.BeginStroke();
            undo.RecordBeforeChange(cell);
            CellOverrideService.AdjustElevation(cell, +0.3f, 0f);
            undo.EndStroke();

            // Мазок 2 — река: клеток не трогает вообще, живёт в списке рек.
            var rivers = new List<PaintedRiver>();
            undo.BeginStroke();
            var river = new PaintedRiver { Id = 7 };
            rivers.Add(river);
            undo.RecordUndoAction(() => rivers.Remove(river));
            undo.EndStroke();

            if (undo.UndoStackCount != 2)
            { Debug.LogError($"FAIL undo: в истории {undo.UndoStackCount} мазка(ов), ждали 2 — речной мазок не трогает клетки и был бы выброшен как «пустой»"); ok = false; }

            undo.Undo();
            if (rivers.Count != 0)
            { Debug.LogError("FAIL undo: первый Ctrl+Z обязан снять ПОСЛЕДНИЙ мазок — нарисованную реку"); ok = false; }
            if (!cell.ElevationOverride.HasValue)
            { Debug.LogError("FAIL undo: отмена реки не должна трогать клеточный мазок, сделанный до неё"); ok = false; }

            undo.Undo();
            if (cell.ElevationOverride.HasValue)
            { Debug.LogError("FAIL undo: второй Ctrl+Z обязан вернуть клетку к состоянию до её мазка"); ok = false; }

            Debug.Log(ok ? "Self-Test River Undo: PASS" : "Self-Test River Undo: FAIL");
        }
    }
}
