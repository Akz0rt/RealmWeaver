using UnityEngine;

namespace WorldGen.Notes.Data
{
    public class BrushOpsSelfTests : MonoBehaviour
    {
        const int Tex = 256;

        [ContextMenu("Self-Test: Кисть — нерастянутый рисунок")]
        public void SelfTestUnstretchedRadius()
        {
            // Объект ровно в размер растра: 5 единиц толщины = радиус 2.5 пикселя.
            float r = BrushOps.RadiusInPixels(5f, 256f, Tex);
            bool ok = Mathf.Approximately(r, 2.5f);
            if (!ok) Debug.LogError($"FAIL нерастянутый: радиус {r}, ожидался 2.5");
            Done(ok);
        }

        [ContextMenu("Self-Test: Кисть — растянутый вдвое рисунок рисует вдвое тоньше")]
        public void SelfTestStretchedHalvesRadius()
        {
            // ФИКСТУРА РАЗВОДИТ ПРАВИЛО И ПОДДЕЛКУ: деление даёт 1.25, умножение — 10.
            float r = BrushOps.RadiusInPixels(5f, 512f, Tex);
            bool ok = Mathf.Approximately(r, 1.25f);
            if (!ok) Debug.LogError($"FAIL растянутый: радиус {r}, ожидался 1.25 (умножение вместо деления дало бы 10)");
            Done(ok);
        }

        [ContextMenu("Self-Test: Кисть — сжатый рисунок рисует толще")]
        public void SelfTestShrunkGrowsRadius()
        {
            float r = BrushOps.RadiusInPixels(5f, 128f, Tex);
            bool ok = Mathf.Approximately(r, 5f);
            if (!ok) Debug.LogError($"FAIL сжатый: радиус {r}, ожидался 5");
            Done(ok);
        }

        [ContextMenu("Self-Test: Кисть — на огромном растяжении кисть не исчезает")]
        public void SelfTestRadiusNeverVanishes()
        {
            // 2 / 2 * 256 / 4096 = 0.0625 — округлилось бы в ноль пикселей, и кисть перестала бы рисовать.
            float r = BrushOps.RadiusInPixels(2f, 4096f, Tex);
            bool ok = Mathf.Approximately(r, 0.5f);
            if (!ok) Debug.LogError($"FAIL нижняя граница: радиус {r}, ожидался 0.5");
            Done(ok);
        }

        [ContextMenu("Self-Test: Кисть — нулевая ширина объекта не даёт бесконечность")]
        public void SelfTestZeroObjectWidthIsSafe()
        {
            float r = BrushOps.RadiusInPixels(5f, 0f, Tex);
            bool ok = Mathf.Approximately(r, 2.5f);
            if (!ok) Debug.LogError($"FAIL нулевая ширина: радиус {r}, ожидался 2.5 (как у нерастянутого)");
            Done(ok);
        }

        [ContextMenu("Self-Test: Кисть — три толщины различаются")]
        public void SelfTestThreeWidths()
        {
            bool ok = BrushOps.DiameterInCanvasUnits(BrushWidth.Thin) == 2f
                   && BrushOps.DiameterInCanvasUnits(BrushWidth.Medium) == 5f
                   && BrushOps.DiameterInCanvasUnits(BrushWidth.Thick) == 10f;
            if (!ok) Debug.LogError("FAIL толщины: ожидались 2/5/10");
            Done(ok);
        }

        static void Done(bool ok, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        { if (ok) Debug.Log($"PASS {name}"); }
    }
}
