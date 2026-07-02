using UnityEngine;
using UnityEngine.UI;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Row of tool buttons (Select/Note/Link/Drawing/Image) above the notes canvas.
    /// Clicking a button calls CanvasInteractionController.SetTool and highlights itself.
    /// </summary>
    public class NotesToolbar : MonoBehaviour
    {
        public Color activeColor = new Color(0.2f, 0.55f, 0.3f);
        public Color inactiveColor = new Color(0.3f, 0.3f, 0.3f);

        Font builtinFont;
        Button[] buttons;
        CanvasInteractionController controller;

        static readonly (NotesTool tool, string label)[] ToolDefs =
        {
            (NotesTool.Select, "Курсор"),
            (NotesTool.Note, "Заметка"),
            (NotesTool.Link, "Связь"),
            (NotesTool.Drawing, "Рисунок"),
            (NotesTool.Image, "Изображение"),
        };

        public void Initialize(CanvasInteractionController interactionController, Transform parent)
        {
            controller = interactionController;
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rowGO = new GameObject("NotesToolbar");
            rowGO.transform.SetParent(parent, false);
            var hLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 4f;
            hLayout.childControlWidth = true;
            hLayout.childForceExpandWidth = true;
            rowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            buttons = new Button[ToolDefs.Length];
            for (int i = 0; i < ToolDefs.Length; i++)
            {
                int index = i;
                var (tool, label) = ToolDefs[i];

                var btnGO = new GameObject($"Tool_{tool}");
                btnGO.transform.SetParent(rowGO.transform, false);
                var img = btnGO.AddComponent<Image>();
                img.color = inactiveColor;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActive(tool));
                buttons[index] = btn;

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(btnGO.transform, false);
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = builtinFont;
                text.fontSize = 10;
                text.color = Color.white;
                text.alignment = TextAnchor.MiddleCenter;
                var textRect = textGO.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
            }

            SetActive(NotesTool.Select);
        }

        void SetActive(NotesTool tool)
        {
            controller.SetTool(tool);
            for (int i = 0; i < ToolDefs.Length; i++)
                buttons[i].GetComponent<Image>().color = ToolDefs[i].tool == tool ? activeColor : inactiveColor;
        }
    }
}
