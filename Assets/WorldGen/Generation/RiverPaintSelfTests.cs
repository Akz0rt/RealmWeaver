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
        //   • «оставить клетки воды на концах» → первая точка станет (0,0), а не берегом у (5,0);
        //   • «начать прямо с клетки суши»     → первая точка станет (10,0);
        //   • «обрезать и середину мазка»      → пропадёт вода ВНУТРИ русла;
        //   • «мазок по воде — тоже река»      → непустой результат там, где суши не было;
        //   • «перепутать устья местами»       → море и озеро поменяются концами, а значит градиент
        //     ляжет задом наперёд и скруглится не тот конец. Ради него концы РАЗНЫЕ: на симметричной
        //     заготовке подмена была бы не видна.
        [ContextMenu("Self-Test: River Trim To Shore")]
        public void SelfTestTrimToShore()
        {
            bool ok = true;

            // Море → суша → суша → озеро: оба конца режутся по кромке и заходят за неё на 2.
            var sites = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0) };
            var kind = new List<RiverMouth> { RiverMouth.Sea, RiverMouth.None, RiverMouth.None, RiverMouth.Lake };
            var trimmed = RiverPaintOps.TrimToShore(sites, kind, overshoot: 2f, out var startMouth, out var endMouth);
            if (trimmed.Count != 4)
            { Debug.LogError($"FAIL trim: точек {trimmed.Count}, ждали 4 (берег + две клетки суши + берег)"); ok = false; }
            else
            {
                if (!Near(trimmed[0], new Vector2(3, 0)))
                { Debug.LogError($"FAIL trim: начало {trimmed[0]}, ждали (3,0) — берег на (5,0) (середина между центрами суши и воды, она же общее ребро клеток Вороного) плюс заход 2 в сторону воды"); ok = false; }
                if (!Near(trimmed[1], new Vector2(10, 0)) || !Near(trimmed[2], new Vector2(20, 0)))
                { Debug.LogError("FAIL trim: клетки суши внутри русла должны остаться как есть"); ok = false; }
                if (!Near(trimmed[3], new Vector2(27, 0)))
                { Debug.LogError($"FAIL trim: устье {trimmed[3]}, ждали (27,0) — берег на (25,0) плюс заход 2"); ok = false; }
            }
            if (startMouth != RiverMouth.Sea || endMouth != RiverMouth.Lake)
            { Debug.LogError($"FAIL trim: устья определились как {startMouth}/{endMouth}, ждали Sea/Lake — от этого зависит и цвет каждого конца, и в какую сторону идёт градиент"); ok = false; }

            // Тот же мазок задом наперёд обязан поменять устья местами.
            var reversedSites = new List<Vector2>(sites); reversedSites.Reverse();
            var reversedKind = new List<RiverMouth>(kind); reversedKind.Reverse();
            RiverPaintOps.TrimToShore(reversedSites, reversedKind, 2f, out var revStart, out var revEnd);
            if (revStart != RiverMouth.Lake || revEnd != RiverMouth.Sea)
            { Debug.LogError($"FAIL trim: у перевёрнутого мазка устья {revStart}/{revEnd}, ждали Lake/Sea"); ok = false; }

            // Конец, не дошедший до воды, — свободный (его потом скругляют).
            RiverPaintOps.TrimToShore(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0) },
                new List<RiverMouth> { RiverMouth.None, RiverMouth.None, RiverMouth.Sea },
                2f, out var freeStart, out var seaEnd);
            if (freeStart != RiverMouth.None || seaEnd != RiverMouth.Sea)
            { Debug.LogError($"FAIL trim: концы {freeStart}/{seaEnd}, ждали None/Sea — конец на суше не должен притворяться устьем"); ok = false; }

            // Мазок целиком по воде — не река.
            var allWater = RiverPaintOps.TrimToShore(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0) },
                new List<RiverMouth> { RiverMouth.Sea, RiverMouth.Sea }, 2f, out _, out _);
            if (allWater.Count != 0)
            { Debug.LogError("FAIL trim: мазок, не задевший сушу, обязан выбрасываться"); ok = false; }

            // Озерцо ПОСРЕДИ реки не режет её надвое.
            var through = RiverPaintOps.TrimToShore(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0) },
                new List<RiverMouth> { RiverMouth.None, RiverMouth.Lake, RiverMouth.None }, 2f, out _, out _);
            if (through.Count != 3 || !Near(through[1], new Vector2(10, 0)))
            { Debug.LogError("FAIL trim: вода ВНУТРИ мазка обрезаться не должна — режутся только концы"); ok = false; }

            // Одна клетка суши — точка, а не река.
            var single = RiverPaintOps.TrimToShore(new List<Vector2> { new Vector2(0, 0) },
                new List<RiverMouth> { RiverMouth.None }, 2f, out _, out _);
            if (single.Count != 0)
            { Debug.LogError("FAIL trim: из одной точки река не получается"); ok = false; }

            Debug.Log(ok ? "Self-Test River Trim: PASS" : "Self-Test River Trim: FAIL");
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

        // Четыре требования ДМ к виду реки, записанные как проверки: свободный конец скруглён,
        // устье гаснет, середина темнее краёв, цвет вдоль русла переливается от конца к концу.
        // Мутанты: «не строить шапку», «не гасить устье», «красить одним цветом поперёк»,
        // «брать цвет только одного конца».
        [ContextMenu("Self-Test: River Mesh Shading")]
        public void SelfTestMeshShading()
        {
            bool ok = true;
            var curve = new List<Vector2> { new Vector2(0, 0), new Vector2(20, 0), new Vector2(40, 0) };
            var free = new WorldGen.Rendering.RiverEndStyle
            {
                Edge = new Color32(200, 200, 200, 255), Center = new Color32(60, 60, 60, 255),
                Round = true, FadeLength = 0f
            };
            var mouth = new WorldGen.Rendering.RiverEndStyle
            {
                Edge = new Color32(0, 0, 200, 255), Center = new Color32(0, 0, 60, 255),
                Round = false, FadeLength = 10f, FlattenLength = 20f
            };

            var mesh = WorldGen.Rendering.RiverMeshBuilder.Build(curve, width: 6f, yHeight: 0.45f, start: free, end: mouth);
            var verts = mesh.vertices;
            var colors = mesh.colors32;

            if (verts.Length <= curve.Count * 3)
            { Debug.LogError($"FAIL mesh: вершин {verts.Length} при {curve.Count} точках — свободный конец не скруглён (шапки нет)"); ok = false; }
            if (colors.Length != verts.Length)
            { Debug.LogError("FAIL mesh: у вершин нет цветов — вся раскраска держится на них"); ok = false; }
            else
            {
                // Поперечник в середине: [край, ось, край].
                int mid = 1 * 3;
                if (Luma(colors[mid + 1]) >= Luma(colors[mid]))
                { Debug.LogError($"FAIL mesh: ось русла (яркость {Luma(colors[mid + 1]):F0}) не темнее края ({Luma(colors[mid]):F0}) — правило «к берегу светлее, к центру темнее» не применилось"); ok = false; }

                // Цвет вдоль русла: у свободного конца край серый (красного 200), у устья синий
                // (красного 0), а посередине обязана быть их смесь — это и есть градиент.
                int lastEdge = (curve.Count - 1) * 3;
                if (!(colors[0].r > 150 && colors[lastEdge].r < 60))
                { Debug.LogError($"FAIL mesh: концы покрашены как {colors[0].r}/{colors[lastEdge].r} по красному, ждали ~200 и ~0 — каждый конец должен брать цвет своего водоёма"); ok = false; }
                if (colors[mid].r < 40 || colors[mid].r > 180)
                { Debug.LogError($"FAIL mesh: в середине русла красного {colors[mid].r}, ждали смесь около 100 — цвет обязан ПЕРЕЛИВАТЬСЯ от конца к концу, а не прыгать"); ok = false; }

                if (colors[0].a != 255)
                { Debug.LogError($"FAIL mesh: свободный конец полупрозрачен (альфа {colors[0].a}) — гаснуть должно только устье"); ok = false; }

                byte minAlpha = 255;
                foreach (var c in colors) if (c.a < minAlpha) minAlpha = c.a;
                if (minAlpha > 5)
                { Debug.LogError($"FAIL mesh: минимальная альфа {minAlpha} — устье не гаснет, и стык с водоёмом останется резким"); ok = false; }

                // «Река — продолжение водоёма»: у самого устья поперечник обязан быть РОВНЫМ
                // мелководьем (ось сошлась с краем), иначе тёмная сердцевина воткнётся языком в
                // светлую прибрежную полосу. Мутант: не распрямлять профиль у устья.
                if (Mathf.Abs(Luma(colors[lastEdge + 1]) - Luma(colors[lastEdge])) > 12f)
                { Debug.LogError($"FAIL mesh: у устья ось (яркость {Luma(colors[lastEdge + 1]):F0}) всё ещё темнее края ({Luma(colors[lastEdge]):F0}) — в водоём река должна входить ровной полосой мелководья"); ok = false; }
            }

            Debug.Log(ok ? "Self-Test River Mesh: PASS" : "Self-Test River Mesh: FAIL");
        }

        static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

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
