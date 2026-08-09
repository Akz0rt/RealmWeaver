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

        // Требование ДМ: две пересекающиеся реки должны читаться ОДНОЙ рекой, без перекрытий.
        //
        // Мутант, которого валит этот тест: «строить меш рекой за рекой» (внешний цикл по рекам, а
        // не по полосам) — именно так было раньше. Проверка: точка (20,2) лежит на оси реки Б, но
        // сбоку от оси реки А. При верном порядке последней её закрашивает сердцевина Б в обоих
        // случаях; при мутанте — та река, которую построили ПОСЛЕДНЕЙ, и цвет от порядка рисования
        // зависит. Поэтому меш строится дважды с переставленными реками и цвета сравниваются: тест
        // вида «в перекрестье какой-то речной цвет» прошёл бы и на сломанном рендере.
        [ContextMenu("Self-Test: River Mesh Union")]
        public void SelfTestMeshUnion()
        {
            bool ok = true;
            var edge = new Color32(200, 200, 200, 255);
            var core = new Color32(60, 60, 60, 255);

            var a = new WorldGen.Rendering.RiverShape
            {
                Curve = new List<Vector2> { new Vector2(0, 0), new Vector2(20, 0), new Vector2(40, 0) },
                Width = 6f
            };
            var b = new WorldGen.Rendering.RiverShape
            {
                Curve = new List<Vector2> { new Vector2(20, -20), new Vector2(20, 0), new Vector2(20, 20) },
                Width = 6f
            };

            // На оси Б, но в третьей полосе А (полуширины полос: 3; 2,5; 2; 1,5; 1; 0,5) — здесь
            // порядок построения и вылезает.
            var probe = new Vector2(20, 1.8f);
            var background = new Color(1f, 0f, 1f, 1f);
            var direct = SampleMesh(WorldGen.Rendering.RiverMeshBuilder.BuildAll(
                new List<WorldGen.Rendering.RiverShape> { a, b }, 0.45f, edge, core), probe, background);
            var swapped = SampleMesh(WorldGen.Rendering.RiverMeshBuilder.BuildAll(
                new List<WorldGen.Rendering.RiverShape> { b, a }, 0.45f, edge, core), probe, background);

            if (SameColor(direct, background))
            { Debug.LogError("FAIL union: в перекрестье вообще не легло русло — заготовка не проверяет ничего"); ok = false; }
            if (!SameColor(direct, swapped))
            { Debug.LogError($"FAIL union: цвет перекрестья зависит от порядка рек ({direct} против {swapped}) — на карте это видимый шов, а от поворота камеры он ещё и мигает"); ok = false; }
            if (Mathf.Abs(direct.r * 255f - core.r) > 3f)
            { Debug.LogError($"FAIL union: в точке на оси реки Б красного {direct.r * 255f:F0}, ждали сердцевину {core.r} — объединение обязано показывать самую внутреннюю полосу"); ok = false; }

            // Поперёк русла цвет обязан меняться от кромки к оси — иначе это плоская лента.
            var rim = SampleMesh(WorldGen.Rendering.RiverMeshBuilder.BuildAll(
                new List<WorldGen.Rendering.RiverShape> { a }, 0.45f, edge, core), new Vector2(10, 2.8f), background);
            var axis = SampleMesh(WorldGen.Rendering.RiverMeshBuilder.BuildAll(
                new List<WorldGen.Rendering.RiverShape> { a }, 0.45f, edge, core), new Vector2(10, 0f), background);
            if (!(Luma(rim) > Luma(axis) + 20f))
            { Debug.LogError($"FAIL union: у кромки яркость {Luma(rim):F0}, на оси {Luma(axis):F0} — правило «к берегу светлее, к центру темнее» не применилось"); ok = false; }

            Debug.Log(ok ? "Self-Test River Union: PASS" : "Self-Test River Union: FAIL");
        }

        // Требование ДМ: место втекания в водоём не должно выглядеть отдельным объектом.
        // Мутанты: «не сужать конец», «не гасить конец», «красить кончик общим цветом реки».
        [ContextMenu("Self-Test: River Mesh Mouth")]
        public void SelfTestMeshMouth()
        {
            bool ok = true;
            var edge = new Color32(200, 200, 200, 255);
            var core = new Color32(60, 60, 60, 255);
            var water = new Color32(0, 0, 255, 255);   // нарочно НЕ речной цвет: подмену видно

            var shape = new WorldGen.Rendering.RiverShape
            {
                Curve = new List<Vector2> { new Vector2(0, 0), new Vector2(20, 0), new Vector2(40, 0) },
                Width = 6f,
                Start = new WorldGen.Rendering.RiverEndStyle { Round = true },
                End = new WorldGen.Rendering.RiverEndStyle { MouthLength = 10f, MouthWater = water }
            };
            var mesh = WorldGen.Rendering.RiverMeshBuilder.BuildAll(
                new List<WorldGen.Rendering.RiverShape> { shape }, 0.45f, edge, core);
            var verts = mesh.vertices;
            var colors = mesh.colors32;

            if (colors.Length != verts.Length)
            { Debug.LogError("FAIL mouth: у вершин нет цветов — вся раскраска держится на них"); ok = false; }

            // Скруглённый конец: у самой левой вершины X должен уйти ЛЕВЕЕ начала русла (шапка).
            float minX = float.MaxValue;
            foreach (var v in verts) minX = Mathf.Min(minX, v.x);
            if (minX > -0.5f)
            { Debug.LogError($"FAIL mouth: левее начала русла ничего нет (minX {minX:F2}) — свободный конец не скруглён, он обрывается ножом"); ok = false; }

            // Сужение: у самого устья лента уже, чем в теле реки.
            float halfAtMid = HalfWidthNear(verts, 20f), halfAtTip = HalfWidthNear(verts, 40f);
            if (!(halfAtTip < halfAtMid * 0.35f))
            { Debug.LogError($"FAIL mouth: полуширина у устья {halfAtTip:F2} против {halfAtMid:F2} в теле — конец не сужается, в водоём воткнётся полоса с резкими боками"); ok = false; }

            // Гашение и цвет: у кончика альфа падает почти в ноль, а цвет уходит в цвет воды.
            byte minAlpha = 255;
            foreach (var c in colors) minAlpha = (byte)Mathf.Min(minAlpha, c.a);
            if (minAlpha > 5)
            { Debug.LogError($"FAIL mouth: минимальная альфа {minAlpha} — устье не гаснет, и стык с водоёмом останется резким"); ok = false; }

            Color32 tip = ColorNear(verts, colors, 40f);
            if (tip.b < 200 || tip.r > 60)
            { Debug.LogError($"FAIL mouth: у кончика цвет {tip} вместо цвета воды {water} — река въедет в водоём чужим цветом и будет видна отдельной полосой"); ok = false; }

            Debug.Log(ok ? "Self-Test River Mouth: PASS" : "Self-Test River Mouth: FAIL");
        }

        /// <summary>Наибольшее отклонение вершины от оси Z=0 среди вершин около заданного X — она же
        /// полуширина ленты в этом месте.</summary>
        static float HalfWidthNear(Vector3[] verts, float x)
        {
            float best = 0f;
            foreach (var v in verts)
                if (Mathf.Abs(v.x - x) < 0.6f) best = Mathf.Max(best, Mathf.Abs(v.z));
            return best;
        }

        static Color32 ColorNear(Vector3[] verts, Color32[] colors, float x)
        {
            float bestDx = float.MaxValue;
            Color32 best = default;
            for (int i = 0; i < verts.Length; i++)
            {
                float dx = Mathf.Abs(verts[i].x - x);
                if (dx >= bestDx) continue;
                bestDx = dx; best = colors[i];
            }
            return best;
        }

        /// <summary>Красит один пиксель так же, как это делает видеокарта на прозрачной очереди с
        /// выключенной записью глубины: треугольники в порядке индексов, каждый смешивается с уже
        /// накопленным по своей альфе. Без этого «перекрытия не видно» проверить нечем.</summary>
        static Color SampleMesh(Mesh mesh, Vector2 p, Color background)
        {
            var verts = mesh.vertices;
            var colors = mesh.colors32;
            var tris = mesh.triangles;
            Color dst = background;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 a = Flat(verts[tris[i]]), b = Flat(verts[tris[i + 1]]), c = Flat(verts[tris[i + 2]]);
                if (Mathf.Abs(Cross(b - a, c - a)) < 1e-5f) continue;   // выродившийся у кончика — пропускаем
                if (!InTriangle(p, a, b, c)) continue;

                Color32 src = colors[tris[i]];
                dst = Color.Lerp(dst, new Color(src.r / 255f, src.g / 255f, src.b / 255f, 1f), src.a / 255f);
            }
            return dst;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
        static float Cross(Vector2 u, Vector2 w) => u.X * w.Y - u.Y * w.X;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static bool SameColor(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        static float Luma(Color c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * 255f;

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
