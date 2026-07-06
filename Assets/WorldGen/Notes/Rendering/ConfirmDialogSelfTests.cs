using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// [ContextMenu] self-tests for ConfirmDialog (Screen F), matching this project's convention of
    /// self-tests living on a component. Add to any scene GameObject and run from the Inspector's
    /// right-click menu. Each test builds a dialog, inspects its structure, then cleans it up.
    /// </summary>
    public class ConfirmDialogSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Backdrop Blocks Input")]
        public void SelfTestBackdrop()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ConfirmDialog.ShowInfo(font, "Заголовок", "Тело");

            var canvas = GameObject.Find("ConfirmDialogCanvas");
            bool ok = canvas != null && canvas.transform.childCount >= 2;
            if (ok)
            {
                var backdrop = canvas.transform.GetChild(0);
                var dialog = canvas.transform.GetChild(1);
                var bImg = backdrop.GetComponent<Image>();
                var bBtn = backdrop.GetComponent<Button>();
                ok = backdrop.name == "Backdrop"
                     && dialog.name == "Dialog"
                     && bImg != null && bImg.raycastTarget
                     && bBtn != null
                     && backdrop.GetSiblingIndex() < dialog.GetSiblingIndex();
            }

            if (canvas != null) DestroyImmediate(canvas);
            Debug.Log(ok
                ? "Self-Test Backdrop Blocks Input: PASS"
                : "Self-Test Backdrop Blocks Input: FAIL");
        }

        [ContextMenu("Self-Test: Details Button Visibility")]
        public void SelfTestDetailsVisibility()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            ConfirmDialog.ShowInfo(font, "Заголовок", "Тело", null, null);
            var c1 = GameObject.Find("ConfirmDialogCanvas");
            var details1 = c1 != null ? c1.transform.Find("Dialog/Footer/Btn_Подробнее") : null;
            bool hiddenWhenNull = details1 != null && !details1.gameObject.activeSelf;
            if (c1 != null) DestroyImmediate(c1);

            ConfirmDialog.ShowInfo(font, "Заголовок", "Тело", null, () => { });
            var c2 = GameObject.Find("ConfirmDialogCanvas");
            var details2 = c2 != null ? c2.transform.Find("Dialog/Footer/Btn_Подробнее") : null;
            bool shownWhenSet = details2 != null && details2.gameObject.activeSelf;
            if (c2 != null) DestroyImmediate(c2);

            bool ok = hiddenWhenNull && shownWhenSet;
            Debug.Log(ok
                ? "Self-Test Details Button Visibility: PASS"
                : $"Self-Test Details Button Visibility: FAIL (hiddenWhenNull={hiddenWhenNull}, shownWhenSet={shownWhenSet})");
        }
    }
}
