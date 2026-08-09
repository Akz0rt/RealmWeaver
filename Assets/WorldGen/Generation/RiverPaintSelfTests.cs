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
        //   • «оставить клетки воды на концах» → первая точка станет (0,0), а не серединой (5,0);
        //   • «начать прямо с клетки суши»     → первая точка станет (10,0);
        //   • «обрезать и середину мазка»      → пропадёт вода ВНУТРИ русла;
        //   • «мазок по воде — тоже река»      → непустой результат там, где суши не было.
        [ContextMenu("Self-Test: River Trim To Shore")]
        public void SelfTestTrimToShore()
        {
            bool ok = true;

            // Вода → суша → суша → вода: оба конца режутся ровно по кромке (середина между центрами).
            var sites = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0) };
            var water = new List<bool> { true, false, false, true };
            var trimmed = RiverPaintOps.TrimToShore(sites, water);
            if (trimmed.Count != 4)
            { Debug.LogError($"FAIL trim: точек {trimmed.Count}, ждали 4 (кромка + две клетки суши + кромка)"); ok = false; }
            else
            {
                if (!Near(trimmed[0], new Vector2(5, 0)))
                { Debug.LogError($"FAIL trim: начало {trimmed[0]}, ждали (5,0) — СЕРЕДИНУ между центрами суши и воды, она же общее ребро клеток Вороного, она же берег"); ok = false; }
                if (!Near(trimmed[1], new Vector2(10, 0)) || !Near(trimmed[2], new Vector2(20, 0)))
                { Debug.LogError("FAIL trim: клетки суши внутри русла должны остаться как есть"); ok = false; }
                if (!Near(trimmed[3], new Vector2(25, 0)))
                { Debug.LogError($"FAIL trim: устье {trimmed[3]}, ждали (25,0) — река доходит до кромки воды и там кончается, а не рисуется поверх водоёма"); ok = false; }
            }

            // Мазок целиком по воде — не река.
            var allWater = RiverPaintOps.TrimToShore(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0) }, new List<bool> { true, true });
            if (allWater.Count != 0)
            { Debug.LogError("FAIL trim: мазок, не задевший сушу, обязан выбрасываться"); ok = false; }

            // Озерцо ПОСРЕДИ реки не режет её надвое.
            var through = RiverPaintOps.TrimToShore(
                new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0) },
                new List<bool> { false, true, false });
            if (through.Count != 3 || !Near(through[1], new Vector2(10, 0)))
            { Debug.LogError("FAIL trim: вода ВНУТРИ мазка обрезаться не должна — режутся только концы"); ok = false; }

            // Одна клетка суши — точка, а не река.
            var single = RiverPaintOps.TrimToShore(new List<Vector2> { new Vector2(0, 0) }, new List<bool> { false });
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
