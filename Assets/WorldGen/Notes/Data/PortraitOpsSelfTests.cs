using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>
    /// Самопроверки для PortraitOps. Арифметика портрета: без UnityEngine, проверяется офлайн.
    /// </summary>
    public class PortraitOpsSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Fit Scales By Longer Side")]
        public void SelfTestFitScalesByLongerSide()
        {
            bool ok = true;

            // Мутант: уменьшается по ширине всегда, а не по БОЛЬШЕЙ стороне.
            int w, h;
            if (!PortraitOps.Fit(2048, 1024, out w, out h))
            { Debug.LogError("FAIL: широкая картинка не уменьшена"); ok = false; }
            if (w != 512 || h != 256)
            { Debug.LogError($"FAIL: широкая: получено {w}x{h}, ожидалось 512x256"); ok = false; }

            if (!PortraitOps.Fit(1024, 2048, out w, out h))
            { Debug.LogError("FAIL: высокая картинка не уменьшена"); ok = false; }
            if (w != 256 || h != 512)
            { Debug.LogError($"FAIL: высокая: получено {w}x{h}, ожидалось 256x512"); ok = false; }

            Debug.Log(ok ? "Self-Test Fit Scales By Longer Side: PASS" : "Self-Test Fit Scales By Longer Side: FAIL");
        }

        [ContextMenu("Self-Test: Fit Leaves Small Alone")]
        public void SelfTestFitLeavesSmallAlone()
        {
            bool ok = true;

            // Мутант: маленькая картинка РАСТЯГИВАЕТСЯ до 512.
            int w, h;
            if (PortraitOps.Fit(120, 90, out w, out h))
            { Debug.LogError("FAIL: маленькая картинка объявлена требующей уменьшения"); ok = false; }
            if (w != 120 || h != 90)
            { Debug.LogError($"FAIL: маленькая картинка изменена: {w}x{h}"); ok = false; }

            if (PortraitOps.Fit(512, 300, out w, out h))
            { Debug.LogError("FAIL: ровно 512 объявлено требующим уменьшения"); ok = false; }

            Debug.Log(ok ? "Self-Test Fit Leaves Small Alone: PASS" : "Self-Test Fit Leaves Small Alone: FAIL");
        }

        [ContextMenu("Self-Test: Fit Never Returns Zero Side")]
        public void SelfTestFitNeverReturnsZeroSide()
        {
            bool ok = true;

            // Мутант: округление до нуля — сторона 0 роняет создание текстуры.
            int w, h;
            PortraitOps.Fit(10000, 3, out w, out h);
            if (w < 1 || h < 1)
            { Debug.LogError($"FAIL: сторона схлопнулась в {w}x{h}"); ok = false; }

            Debug.Log(ok ? "Self-Test Fit Never Returns Zero Side: PASS" : "Self-Test Fit Never Returns Zero Side: FAIL");
        }
    }
}
