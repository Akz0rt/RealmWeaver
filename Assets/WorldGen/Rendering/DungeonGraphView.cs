using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Draggable node-graph canvas for one dungeon floor (Task 4 of the room-graph rework). Hosted as
    /// a child of DungeonEditorScreen.MapArea; owns three stretched child layers built in draw order —
    /// BackgroundHit, LinesLayer, NodesLayer (later siblings render on top, so corridor lines sit
    /// behind node cards).
    ///
    /// Node cards are anchored by NORMALIZED position (anchorMin=anchorMax=(room.X, 1-room.Y)) and
    /// NEVER read this transform's rect at Bind/Refresh time — the host screen can Bind before the
    /// screen is activated, when rect=={0,0} (see DungeonEditorScreen's own doc comment for the same
    /// gotcha). Corridor lines DO need pixel geometry (PlaceLine/NodeCenter), which only produces
    /// correct numbers once the rect has actually laid out; RelayoutLines() is therefore re-run on the
    /// first valid LateUpdate after any rebuild (Refresh sets needsInitialRelayout=true), in addition
    /// to running live on every drag sample.
    /// </summary>
    public class DungeonGraphView : MonoBehaviour
    {
        public System.Action<int> OnRoomSelected;   // fires with a room id, or 0 when selection clears
        public System.Action OnGraphMutated;        // fires after add/delete/link (structural change)
        public System.Action<int> OnJumpToLevel;    // fires with a target level index (badge click)

        public int SelectedRoomId { get; private set; }
        public bool LinkMode { get; private set; }

        DungeonData dungeon;
        int levelIndex;
        DungeonLevel boundLevel;   // last-bound level OBJECT (not just index) — see Bind's sameBinding check
        Font font;
        bool built;

        RectTransform linesLayer;
        RectTransform nodesLayer;
        readonly Dictionary<int, NodeCardUI> nodeCards = new Dictionary<int, NodeCardUI>();
        readonly List<RectTransform> lineRects = new List<RectTransform>();
        int pendingLinkId;
        bool needsInitialRelayout;

        class NodeCardUI { public Outline outline; }

        DungeonLevel BoundLevel =>
            dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Levels.Count
                ? dungeon.Levels[levelIndex] : null;

        void Awake() { if (isActiveAndEnabled) EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard
            BuildUI();
            built = true;
        }

        /// <summary>(Re)bind to a level and rebuild. Selection/link-pending state resets only when the
        /// dungeon or level index actually changes — a same-binding re-Bind (e.g. the round-trip from
        /// OnGraphMutated -> DungeonEditorScreen.RefreshBody -> Bind after an add/link) preserves the
        /// current selection instead of stomping it back to none. "Same binding" is keyed on the actual
        /// bound DungeonLevel OBJECT, not just (dungeon, levelIndex) — RemoveCurrentLevel() re-binds to
        /// the same numeric index but a DIFFERENT DungeonLevel, so keying on the index alone would let a
        /// stale SelectedRoomId survive and spuriously match an unrelated room on the new level.</summary>
        public void Bind(DungeonData dungeon, int levelIndex, Font font)
        {
            EnsureBuilt();
            var newLevel = (dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Levels.Count)
                ? dungeon.Levels[levelIndex] : null;
            bool sameBinding = this.dungeon == dungeon && this.levelIndex == levelIndex && newLevel == boundLevel;
            this.dungeon = dungeon;
            this.levelIndex = levelIndex;
            this.font = font;
            boundLevel = newLevel;
            if (!sameBinding)
            {
                SelectedRoomId = 0;
                pendingLinkId = 0;
            }
            Refresh();
        }

        /// <summary>Rebuilds node cards + corridor lines + inter-floor badges from the bound level.</summary>
        public void Refresh()
        {
            EnsureBuilt();
            ClearLayer(nodesLayer);
            ClearLayer(linesLayer);
            nodeCards.Clear();
            lineRects.Clear();

            var lvl = BoundLevel;
            if (lvl == null) { RefreshHighlights(); return; }

            foreach (var c in lvl.Corridors) lineRects.Add(BuildLineRect());
            foreach (var r in lvl.Rooms) nodeCards[r.Id] = BuildNodeCard(r);

            if (SelectedRoomId != 0 && !nodeCards.ContainsKey(SelectedRoomId)) SelectedRoomId = 0;
            RefreshHighlights();

            needsInitialRelayout = true;
            RelayoutLines();   // attempt now; LateUpdate retries once the rect is actually valid (gotcha #2)
        }

        void LateUpdate()
        {
            if (!needsInitialRelayout) return;
            var rt = (RectTransform)transform;
            if (rt.rect.width <= 0f) return;   // still not laid out (e.g. screen not yet activated) — retry next frame
            RelayoutLines();
            needsInitialRelayout = false;
        }

        public void SetLinkMode(bool on)
        {
            LinkMode = on;
            pendingLinkId = 0;
            RefreshHighlights();
        }

        /// <summary>Removes the selected room (DungeonOps also strips its corridors and any secrets
        /// anywhere in the dungeon that targeted it), clears selection, rebuilds.</summary>
        public void DeleteSelected()
        {
            if (SelectedRoomId == 0 || dungeon == null) return;
            DungeonOps.RemoveRoom(dungeon, levelIndex, SelectedRoomId);
            SelectRoom(0);   // clears through the same path as a background click — fires OnRoomSelected(0)
                              // so the host (e.g. DungeonEditorScreen.selectedRoomId) drops the deleted id too
            Refresh();
            OnGraphMutated?.Invoke();
        }

        /// <summary>Adds a Normal room at the canvas center, selects it, rebuilds.</summary>
        public Room AddRoomAtCenter()
        {
            var lvl = BoundLevel;
            if (lvl == null) return null;
            var room = DungeonOps.AddRoom(lvl, 0.5f, 0.5f);
            Refresh();
            SelectRoom(room.Id);
            OnGraphMutated?.Invoke();
            return room;
        }

        // ── Selection / link click routing ──────────────────────────────────────

        void SelectRoom(int id)
        {
            SelectedRoomId = id;
            RefreshHighlights();
            OnRoomSelected?.Invoke(id);
        }

        void OnCardClicked(int id)
        {
            if (!LinkMode) { SelectRoom(id); return; }

            if (pendingLinkId == 0) { pendingLinkId = id; RefreshHighlights(); return; }

            int a = pendingLinkId;
            pendingLinkId = 0;
            if (a == id) { RefreshHighlights(); return; }   // same card twice — cancel silently, no dialog

            var lvl = BoundLevel;
            if (lvl == null) return;
            string reason = DungeonOps.AddCorridor(lvl, a, id);
            if (reason != null)
            {
                RefreshHighlights();
                ConfirmDialog.ShowInfo(font, "Нельзя связать", reason);
            }
            else
            {
                Refresh();
                OnGraphMutated?.Invoke();
            }
        }

        void OnBackgroundClicked() => SelectRoom(0);

        void RefreshHighlights()
        {
            foreach (var kv in nodeCards)
            {
                bool hi = kv.Key == SelectedRoomId || (LinkMode && kv.Key == pendingLinkId);
                kv.Value.outline.enabled = hi;
            }
        }

        // ── Drag ─────────────────────────────────────────────────────────────────

        void OnCardDragged(int id, RectTransform cardRect, PointerEventData data)
        {
            var lvl = BoundLevel;
            var room = lvl?.GetRoom(id);
            if (room == null) return;

            var root = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, data.position, null, out var local)) return;
            var rect = root.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;   // not laid out — ignore this drag sample

            float nx = Mathf.Clamp((local.x - rect.xMin) / rect.width, 0.04f, 0.96f);
            float ny = Mathf.Clamp((local.y - rect.yMin) / rect.height, 0.04f, 0.96f);
            room.X = nx;
            room.Y = 1f - ny;   // grid Y is top-down → invert for the bottom-origin rect space

            cardRect.anchorMin = cardRect.anchorMax = new Vector2(room.X, 1f - room.Y);
            RelayoutLines();
        }

        /// <summary>Repositions every corridor line from the current room X/Y. No-ops until this view's
        /// own rect has actually laid out (rect is {0,0} before first activation) — Refresh() arranges a
        /// retry via LateUpdate for the first frame the rect becomes valid.</summary>
        void RelayoutLines()
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            var area = (RectTransform)transform;
            if (area.rect.width <= 0f) return;

            for (int i = 0; i < lineRects.Count && i < lvl.Corridors.Count; i++)
            {
                var c = lvl.Corridors[i];
                var ra = lvl.GetRoom(c.RoomA);
                var rb = lvl.GetRoom(c.RoomB);
                if (ra == null || rb == null) continue;
                PlaceLine(lineRects[i], NodeCenter(area, ra), NodeCenter(area, rb), 3f);
            }
        }

        // Places `lineRect` (pivot 0.5,0.5, anchored center of `area`) as a segment from pixel point p0 to p1.
        static void PlaceLine(RectTransform lineRect, Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 mid = (p0 + p1) * 0.5f;
            Vector2 dir = p1 - p0;
            float len = dir.magnitude;
            lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = new Vector2(len, thickness);
            lineRect.anchoredPosition = mid;                 // mid is relative to area center (see NodeCenter below)
            lineRect.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // Pixel center of a room within `area`, relative to the area's CENTER (matches anchoredPosition space
        // when the line/nodes layer is stretched to the area and pivoted at 0.5).
        static Vector2 NodeCenter(RectTransform area, Room r)
        {
            var rect = area.rect;
            float px = (r.X - 0.5f) * rect.width;
            float py = ((1f - r.Y) - 0.5f) * rect.height;    // invert grid-Y for bottom-origin space
            return new Vector2(px, py);
        }

        // ── Construction ─────────────────────────────────────────────────────────

        void BuildUI()
        {
            var bgGO = new GameObject("BackgroundHit", typeof(RectTransform));
            bgGO.transform.SetParent(transform, false);
            Stretch((RectTransform)bgGO.transform);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0f);   // invisible hit-plate (not a themed visual — mirrors
            bgImg.raycastTarget = true;                // PoiEditorScreen's Viewport mask: new Color(0,0,0,0))
            var bgBtn = bgGO.AddComponent<Button>();
            bgBtn.targetGraphic = bgImg;
            bgBtn.onClick.AddListener(OnBackgroundClicked);

            var linesGO = new GameObject("LinesLayer", typeof(RectTransform));
            linesGO.transform.SetParent(transform, false);
            linesLayer = (RectTransform)linesGO.transform;
            Stretch(linesLayer);

            var nodesGO = new GameObject("NodesLayer", typeof(RectTransform));   // added AFTER LinesLayer → renders on top
            nodesGO.transform.SetParent(transform, false);
            nodesLayer = (RectTransform)nodesGO.transform;
            Stretch(nodesLayer);
        }

        RectTransform BuildLineRect()
        {
            var go = new GameObject("Corridor", typeof(RectTransform));
            go.transform.SetParent(linesLayer, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Mut);
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        NodeCardUI BuildNodeCard(Room r)
        {
            var go = new GameObject($"Room_{r.Id}", typeof(RectTransform));
            go.transform.SetParent(nodesLayer, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(r.X, 1f - r.Y);   // NORMALIZED anchor — never rect math (gotcha #1)
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(96f, 40f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, TypeRole(r.Type));

            var outline = go.AddComponent<Outline>();
            outline.effectColor = ThemeService.Get(ThemeRole.Accent);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;

            var lbl = MakeText(go.transform, NodeLabel(r), 11, LabelRole(r.Type), FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;

            // PointerClick selects/links; Drag moves the card + relayouts lines live; EndDrag fires
            // OnGraphMutated. A drag can still end under the card (dragging never leaves this GameObject),
            // so a plain click-vs-drag flag (reset on PointerDown) stops the release from ALSO firing a
            // stray click — without it, every drag-release would re-select (and, worse, feed link mode).
            bool dragged = false;
            var trigger = go.AddComponent<EventTrigger>();
            AddEventTriggerEntry(trigger, EventTriggerType.PointerDown, _ => dragged = false);
            AddEventTriggerEntry(trigger, EventTriggerType.Drag, data => { dragged = true; OnCardDragged(r.Id, rt, (PointerEventData)data); });
            AddEventTriggerEntry(trigger, EventTriggerType.EndDrag, _ => OnGraphMutated?.Invoke());
            AddEventTriggerEntry(trigger, EventTriggerType.PointerClick, _ => { if (!dragged) OnCardClicked(r.Id); });

            BuildBadges(go.transform, r);

            return new NodeCardUI { outline = outline };
        }

        /// <summary>Inter-floor badges stacked below the card: a Boss room's descend badge (only if a
        /// next level exists), an Entrance room's ascend badge (return to the previous floor, or «Выход»
        /// on floor 1), then one badge per secret passage (room target or dungeon exit).</summary>
        void BuildBadges(Transform cardTransform, Room r)
        {
            int index = 0;
            if (r.Type == RoomType.Boss && dungeon != null && levelIndex + 1 < dungeon.Levels.Count)
            {
                int target = levelIndex + 1;
                AddBadge(cardTransform, $"⬇ Этаж {levelIndex + 2}", index++, () => OnJumpToLevel?.Invoke(target));
            }
            // Entrance is the mirror of the boss descent: it returns UP to the previous floor (its boss),
            // or on floor 1 it is the dungeon exit. Leaving the dungeon is a live-navigation action
            // (sub-project 2), so the «Выход» badge is informational here (no in-editor jump).
            if (r.Type == RoomType.Entrance)
            {
                if (levelIndex <= 0)
                    AddBadge(cardTransform, "⬆ Выход", index++, null);
                else
                {
                    int prev = levelIndex - 1;
                    AddBadge(cardTransform, $"⬆ Этаж {levelIndex}", index++, () => OnJumpToLevel?.Invoke(prev));
                }
            }
            foreach (var s in r.Secrets)
            {
                var kind = s.Kind;
                int targetLevel = s.TargetLevelIndex;
                int targetRoom = s.TargetRoomId;
                string summary = kind == SecretTargetKind.DungeonExit ? "⇢ Выход" : $"⇢ Э{targetLevel + 1}·{targetRoom}";
                System.Action onClick = kind == SecretTargetKind.Room ? (System.Action)(() => OnJumpToLevel?.Invoke(targetLevel)) : null;
                AddBadge(cardTransform, summary, index++, onClick);
            }
        }

        void AddBadge(Transform parent, string text, int index, System.Action onClick)
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

            var lbl = MakeText(go.transform, text, 10, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform);
            lbl.raycastTarget = false;

            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onClick());
            }
            else img.raycastTarget = false;   // non-interactive summary (e.g. a DungeonExit secret)
        }

        static void ClearLayer(RectTransform layer)
        {
            if (layer == null) return;
            for (int i = layer.childCount - 1; i >= 0; i--) Destroy(layer.GetChild(i).gameObject);
        }

        static ThemeRole TypeRole(RoomType t) => t switch
        {
            RoomType.Entrance => ThemeRole.Accent,
            RoomType.Boss => ThemeRole.Danger,
            _ => ThemeRole.Elev,
        };

        // Marker-style precedent (old DungeonEditorScreen.BuildMarker): AccentInk reads on both the
        // Accent and Danger card tints; Normal cards (Elev) use plain Txt.
        static ThemeRole LabelRole(RoomType t) => t == RoomType.Normal ? ThemeRole.Txt : ThemeRole.AccentInk;

        static string TypeLabel(RoomType t) => t switch
        {
            RoomType.Entrance => "Вход",
            RoomType.Boss => "Босс",
            _ => "Комната",
        };

        static string NodeLabel(Room r) => $"{r.Id}. {(string.IsNullOrEmpty(r.Title) ? TypeLabel(r.Type) : r.Title)}";

        static void AddEventTriggerEntry(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data));
            trigger.triggers.Add(entry);
        }

        Text MakeText(Transform parent, string content, int size, ThemeRole role, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.text = content; text.font = font; text.fontSize = size; text.fontStyle = style;
            ThemeService.Tag(text, role); text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }
    }
}
