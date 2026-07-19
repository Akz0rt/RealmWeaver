using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Inter-floor badge strip stacked below a room's visual: one chip per inter-floor transition (up/down/
    /// exit stairs, secret passages). WHAT the transitions are is decided by <see cref="InteriorTransitions"/>
    /// per the interior's <see cref="FloorLinkMode"/> (dungeon = type-derived descent; building = explicit
    /// stairs + derived reverse + exit); this class only DRAWS them. Badges are overlay chrome, not projected
    /// geometry.
    /// </summary>
    public static class DungeonBadgeStrip
    {
        public static void Build(Transform parent, InteriorData dungeon, FloorLinkMode mode, int levelIndex,
                                 Room r, Font font, System.Action<int> onJumpToLevel)
        {
            var transitions = InteriorTransitions.For(dungeon, mode, levelIndex, r);
            for (int i = 0; i < transitions.Count; i++)
            {
                var t = transitions[i];
                System.Action onClick = (t.Clickable && t.TargetFloorIndex >= 0)
                    ? (System.Action)(() => onJumpToLevel?.Invoke(t.TargetFloorIndex)) : null;
                AddBadge(parent, font, t.Arrow + " " + t.Label, i, onClick);
            }
        }

        static void AddBadge(Transform parent, Font font, string text, int index, System.Action onClick)
        {
            var go = new GameObject("Badge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(112f, 15f);
            rt.anchoredPosition = new Vector2(0f, -(4f + index * 17f));

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Panel2);

            var lbl = DungeonUiKit.MakeText(go.transform, font, text, 10, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleCenter);
            DungeonUiKit.Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;

            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onClick());
            }
            else img.raycastTarget = false;   // non-interactive summary (e.g. a DungeonExit secret)
        }
    }

    /// <summary>Tiny shared UGUI helpers, lifted verbatim from DungeonGraphView so both renderers and the
    /// badge strip use one copy.</summary>
    public static class DungeonUiKit
    {
        public static Text MakeText(Transform parent, Font font, string content, int size, ThemeRole role,
                                    FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = content; text.font = font; text.fontSize = size; text.fontStyle = style;
            ThemeService.Tag(text, role); text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        public static void ClearLayer(RectTransform layer)
        {
            if (layer == null) return;
            for (int i = layer.childCount - 1; i >= 0; i--) Object.Destroy(layer.GetChild(i).gameObject);
        }
    }
}
