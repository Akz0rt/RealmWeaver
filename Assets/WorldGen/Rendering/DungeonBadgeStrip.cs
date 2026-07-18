using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Inter-floor badge strip stacked below a room's visual: a Boss room's descend badge (only when a
    /// next level exists), an Entrance room's ascend badge (previous floor, or «Выход» on floor 1), then
    /// one badge per secret passage. Extracted from DungeonGraphView so the flat AND iso renderers share
    /// one implementation — badges are overlay chrome in both views, not projected geometry.
    /// </summary>
    public static class DungeonBadgeStrip
    {
        public static void Build(Transform parent, InteriorData dungeon, int levelIndex, Room r, Font font,
                                 System.Action<int> onJumpToLevel)
        {
            int index = 0;
            if (r.TypeId == 2 && dungeon != null && levelIndex + 1 < dungeon.Floors.Count)
            {
                int target = levelIndex + 1;
                AddBadge(parent, font, $"⬇ Этаж {levelIndex + 2}", index++, () => onJumpToLevel?.Invoke(target));
            }
            // Entrance mirrors the boss descent: it returns UP to the previous floor (its boss), or on
            // floor 1 it is the dungeon exit. Leaving the dungeon is live navigation (sub-project 2), so
            // «Выход» is informational here — no in-editor jump.
            if (r.TypeId == 0)
            {
                if (levelIndex <= 0) AddBadge(parent, font, "⬆ Выход", index++, null);
                else
                {
                    int prev = levelIndex - 1;
                    AddBadge(parent, font, $"⬆ Этаж {levelIndex}", index++, () => onJumpToLevel?.Invoke(prev));
                }
            }
            foreach (var s in r.Portals)
            {
                var kind = s.Kind;
                int targetLevel = s.TargetFloorIndex;
                int targetRoom = s.TargetRoomId;
                string summary = kind == PortalKind.DungeonExit ? "⇢ Выход" : $"⇢ Э{targetLevel + 1}·{targetRoom}";
                System.Action onClick = kind == PortalKind.SecretDoor
                    ? (System.Action)(() => onJumpToLevel?.Invoke(targetLevel)) : null;
                AddBadge(parent, font, summary, index++, onClick);
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
