using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Главный экран приложения. Строит свой интерфейс в Awake, поэтому сцена Home.unity
    /// содержит ровно один объект с этим компонентом — сцена без содержимого не конфликтует при
    /// слиянии, а работа над ней идёт в двух ветках параллельно.</summary>
    public class HomeScreenController : MonoBehaviour
    {
        void Awake()
        {
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGO.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.09f, 0.09f, 0.10f);

            var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
            column.transform.SetParent(canvasGO.transform, false);
            var colRt = column.GetComponent<RectTransform>();
            colRt.anchorMin = new Vector2(0.5f, 0.5f); colRt.anchorMax = new Vector2(0.5f, 0.5f);
            colRt.pivot = new Vector2(0.5f, 0.5f);
            colRt.sizeDelta = new Vector2(420f, 420f);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;

            UiKit.Label(column.transform, "RealmWeaver", 44, TextAnchor.MiddleCenter);
            Spacer(column.transform, 24f);

            Row(column.transform, "Играть  ·  скоро", null, enabled: false);
            Spacer(column.transform, 18f);
            UiKit.Label(column.transform, "Подготовка", 18, TextAnchor.MiddleCenter);
            Row(column.transform, "Для игрока", () => SceneManager.LoadScene("PlayerPrep"));
            Row(column.transform, "Для ДМ", () => SceneManager.LoadScene("SampleScene"));
        }

        static void Row(Transform parent, string label, System.Action onClick, bool enabled = true)
        {
            var btn = UiKit.Button(parent, label, onClick, enabled);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;
        }

        static void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }
    }
}
