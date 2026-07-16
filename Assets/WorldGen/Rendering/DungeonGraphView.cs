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
    /// a child of DungeonEditorScreen.MapArea; owns four stretched child layers built in draw order —
    /// BackgroundHit, LinesLayer, JunctionsLayer, NodesLayer (later siblings render on top, so corridor
    /// segments sit behind junction diamonds, which sit behind node cards).
    ///
    /// Node cards are anchored by NORMALIZED position (anchorMin=anchorMax=(room.X, 1-room.Y)) and
    /// sized from the room's tile footprint (Room.SizeW/H × the FIXED PxPerTile constant — never a
    /// rect read). NEVER read this transform's rect at Bind/Refresh time — the host screen can Bind
    /// before the screen is activated, when rect=={0,0} (see DungeonEditorScreen's own doc comment for
    /// the same gotcha). Corridor lines and junction diamonds DO need pixel geometry (PlaceLine/
    /// PointCenter), which only produces correct numbers once the rect has actually laid out;
    /// RelayoutLines() is therefore re-run on the first valid LateUpdate after any rebuild (Refresh
    /// sets needsInitialRelayout=true), in addition to running live on every drag sample.
    ///
    /// Lines and junction diamonds are drawn from DungeonLayout.BuildRenderGraph(BoundLevel) — a
    /// DERIVED view over lvl.Corridors (splits each corridor at every point it crosses another
    /// corridor, emitting a junction point there) — not lvl.Corridors directly. Junction diamonds are
    /// draw-only: never added to nodeCards, raycastTarget=false, never touch selection/ops/validator.
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

        // Fixed px/tile for node-card footprint sizing — deliberately NOT derived from this view's rect
        // (rect=={0,0} before the host screen activates; see class doc gotcha). MinCardPx keeps a
        // 1-tile room's card at a usable click target even though 1×PxPerTile alone would be tiny.
        const float PxPerTile = 14f;
        const float MinCardPx = 20f;

        RectTransform linesLayer;
        RectTransform junctionsLayer;
        RectTransform nodesLayer;
        readonly Dictionary<int, NodeCardUI> nodeCards = new Dictionary<int, NodeCardUI>();
        readonly List<RectTransform> lineRects = new List<RectTransform>();
        readonly List<RectTransform> junctionRects = new List<RectTransform>();
        int pendingLinkId;
        bool needsInitialRelayout;

        // Animated cascade state (BeginCascade/Update, Checkpoint-B tuning). cascadeTargets holds the
        // resolved end position per room id (computed once by DungeonLayout.Separate at cascade start);
        // cascadeVel is SmoothDamp's per-room velocity accumulator. Both null when not cascading.
        bool cascading;
        Dictionary<int, (float x, float y)> cascadeTargets;
        Dictionary<int, Vector2> cascadeVel;
        const float CascadeSmoothTime = 0.18f;
        const float CascadeDoneEpsilon = 5e-4f;

        class NodeCardUI { public Outline outline; public RectTransform rect; }

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
        /// dungeon or level index actually changes — a genuine same-level re-Bind (RefreshBody calls
        /// Bind on every Bind/SetLevel, even when the level itself didn't change) preserves the current
        /// selection instead of stomping it back to none. "Same binding" is keyed on the actual bound
        /// DungeonLevel OBJECT, not just (dungeon, levelIndex) — RemoveCurrentLevel() re-binds to the
        /// same numeric index but a DIFFERENT DungeonLevel, so keying on the index alone would let a
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
                // A stale cascade animation from the PREVIOUS bound level must not keep running against
                // the new one (wrong room ids, wrong targets) — cancel outright on a genuine level switch.
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
            }
            Refresh();
        }

        /// <summary>Rebuilds node cards + corridor lines + inter-floor badges from the bound level.</summary>
        public void Refresh()
        {
            EnsureBuilt();
            ClearLayer(nodesLayer);
            ClearLayer(linesLayer);
            ClearLayer(junctionsLayer);
            nodeCards.Clear();
            lineRects.Clear();
            junctionRects.Clear();

            var lvl = BoundLevel;
            if (lvl == null) { RefreshHighlights(); return; }

            var rg = DungeonLayout.BuildRenderGraph(lvl);
            foreach (var seg in rg.Segments) lineRects.Add(BuildLineRect());
            foreach (var j in rg.Junctions) junctionRects.Add(BuildJunctionRect());
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

        /// <summary>Entry point for the room-cascade separation (replaces a direct DungeonLayout.Separate
        /// + Refresh call). Snapshots current positions, resolves the target layout via Separate (mutates
        /// rooms to their resolved end positions), then either self-skips (no room actually moved — a
        /// link/delete/level-switch edit that never overlapped) with a single static redraw, or restores
        /// rooms to their start positions and animates them to the captured targets in Update() via
        /// SmoothDamp. Safe to call with no bound level (no-op).</summary>
        public void BeginCascade()
        {
            var lvl = BoundLevel;
            if (lvl == null) return;

            var start = new Dictionary<int, (float x, float y)>();
            foreach (var r in lvl.Rooms) start[r.Id] = (r.X, r.Y);

            DungeonLayout.Separate(lvl);   // mutates rooms to resolved target positions

            const float eps = 1e-4f;
            var targets = new Dictionary<int, (float x, float y)>();
            bool anyMoved = false;
            foreach (var r in lvl.Rooms)
            {
                targets[r.Id] = (r.X, r.Y);
                if (start.TryGetValue(r.Id, out var s) &&
                    (Mathf.Abs(s.x - r.X) > eps || Mathf.Abs(s.y - r.Y) > eps))
                    anyMoved = true;
            }

            if (!anyMoved)
            {
                // Nothing overlapped — rooms are already at (== their start ==) target positions. No
                // animation needed; a plain redraw covers link/delete/level-switch edits.
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                Refresh();
                return;
            }

            // Roll rooms back to their pre-Separate start so Update() can animate start → target.
            foreach (var r in lvl.Rooms)
                if (start.TryGetValue(r.Id, out var s)) { r.X = s.x; r.Y = s.y; }

            cascadeTargets = targets;
            cascadeVel = new Dictionary<int, Vector2>();
            foreach (var r in lvl.Rooms) cascadeVel[r.Id] = Vector2.zero;
            cascading = true;

            Refresh();   // draw the restored start state now; Update() takes over from here
        }

        void Update()
        {
            if (!cascading) return;
            var lvl = BoundLevel;
            if (lvl == null || cascadeTargets == null) { cascading = false; return; }

            float maxRemaining = 0f;
            foreach (var r in lvl.Rooms)
            {
                if (!cascadeTargets.TryGetValue(r.Id, out var target)) continue;
                Vector2 cur = new Vector2(r.X, r.Y);
                Vector2 tgt = new Vector2(target.x, target.y);
                Vector2 vel = cascadeVel.TryGetValue(r.Id, out var v) ? v : Vector2.zero;
                Vector2 next = Vector2.SmoothDamp(cur, tgt, ref vel, CascadeSmoothTime);
                cascadeVel[r.Id] = vel;

                r.X = Mathf.Clamp01(next.x);
                r.Y = Mathf.Clamp01(next.y);
                maxRemaining = Mathf.Max(maxRemaining, (tgt - next).magnitude);

                if (nodeCards.TryGetValue(r.Id, out var card) && card.rect != null)
                    card.rect.anchorMin = card.rect.anchorMax = new Vector2(r.X, 1f - r.Y);
            }
            RelayoutLines();   // lightweight reposition of existing line/junction rects — no rebuild (avoids per-frame GC/flicker)

            if (maxRemaining < CascadeDoneEpsilon)
            {
                // Snap exactly to target (SmoothDamp asymptotically approaches but never exactly reaches it).
                foreach (var r in lvl.Rooms)
                {
                    if (!cascadeTargets.TryGetValue(r.Id, out var target)) continue;
                    r.X = target.x; r.Y = target.y;
                    if (nodeCards.TryGetValue(r.Id, out var card) && card.rect != null)
                        card.rect.anchorMin = card.rect.anchorMax = new Vector2(r.X, 1f - r.Y);
                }
                RelayoutLines();
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
            }
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

        /// <summary>Repositions every corridor-segment line and junction diamond from a freshly-computed
        /// DungeonLayout.BuildRenderGraph (recomputed each call since dragging moves room endpoints
        /// continuously). No-ops until this view's own rect has actually laid out (rect is {0,0} before
        /// first activation) — Refresh() arranges a retry via LateUpdate for the first frame the rect
        /// becomes valid. Only repositions up to however many line/junction rects Refresh() last built
        /// (same defensive min-count guard the old corridor-count loop used) — if a drag changes the
        /// crossing TOPOLOGY mid-drag (a junction appears/disappears), the rect count only catches up on
        /// the next full Refresh (drag-end fires OnGraphMutated → RevalidateAndRefresh → Refresh).</summary>
        void RelayoutLines()
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            var area = (RectTransform)transform;
            if (area.rect.width <= 0f) return;

            var rg = DungeonLayout.BuildRenderGraph(lvl);
            for (int i = 0; i < lineRects.Count && i < rg.Segments.Count; i++)
            {
                var seg = rg.Segments[i];
                PlaceLine(lineRects[i], PointCenter(area, seg.A.X, seg.A.Y), PointCenter(area, seg.B.X, seg.B.Y), 3f);
            }
            for (int i = 0; i < junctionRects.Count && i < rg.Junctions.Count; i++)
            {
                var j = rg.Junctions[i];
                junctionRects[i].anchoredPosition = PointCenter(area, j.X, j.Y);
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
            lineRect.anchoredPosition = mid;                 // mid is relative to area center (see PointCenter below)
            lineRect.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // Pixel center of a normalized (x,y) point within `area`, relative to the area's CENTER (matches
        // anchoredPosition space when the lines/junctions/nodes layer is stretched to the area and
        // pivoted at 0.5). Used for both room centers and derived junction points.
        static Vector2 PointCenter(RectTransform area, float x, float y)
        {
            var rect = area.rect;
            float px = (x - 0.5f) * rect.width;
            float py = ((1f - y) - 0.5f) * rect.height;    // invert grid-Y for bottom-origin space
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

            var junctionsGO = new GameObject("JunctionsLayer", typeof(RectTransform));   // added AFTER LinesLayer → renders on top of segments
            junctionsGO.transform.SetParent(transform, false);
            junctionsLayer = (RectTransform)junctionsGO.transform;
            Stretch(junctionsLayer);

            var nodesGO = new GameObject("NodesLayer", typeof(RectTransform));   // added AFTER JunctionsLayer → renders on top
            nodesGO.transform.SetParent(transform, false);
            nodesLayer = (RectTransform)nodesGO.transform;
            Stretch(nodesLayer);
        }

        RectTransform BuildLineRect()
        {
            var go = new GameObject("Segment", typeof(RectTransform));
            go.transform.SetParent(linesLayer, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Mut);
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        // Draw-only crossing marker: fixed small diamond (square rotated 45°), size/rotation set once
        // here at build time — only its anchoredPosition updates per RelayoutLines call. Never added to
        // nodeCards, no Button/EventTrigger, raycastTarget off — not selectable, not in the ops/validator
        // paths (see class doc).
        RectTransform BuildJunctionRect()
        {
            var go = new GameObject("Junction", typeof(RectTransform));
            go.transform.SetParent(junctionsLayer, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(9f, 9f);
            rt.localEulerAngles = new Vector3(0f, 0f, 45f);

            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Txt);
            img.raycastTarget = false;
            return rt;
        }

        NodeCardUI BuildNodeCard(Room r)
        {
            var go = new GameObject($"Room_{r.Id}", typeof(RectTransform));
            go.transform.SetParent(nodesLayer, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(r.X, 1f - r.Y);   // NORMALIZED anchor — never rect math (gotcha #1)
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = FootprintPx(r);
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

            return new NodeCardUI { outline = outline, rect = rt };
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

        // Card pixel size from the room's tile footprint × the fixed PxPerTile constant (never a rect
        // read — gotcha #1). Falls back to the type default for an unmigrated/fresh SizeW/H<=0 (mirrors
        // RoomSizing.ApplyDefaults' own <=0 guard), then clamps to RoomSizing's 1..8 range in case
        // serialized data drifted out of bounds. MinCardPx floors a 1-tile room to a still-clickable size.
        static Vector2 FootprintPx(Room r)
        {
            int w = r.SizeW, h = r.SizeH;
            if (w <= 0 || h <= 0)
            {
                var (dw, dh) = RoomSizing.Default(r.Type);
                if (w <= 0) w = dw;
                if (h <= 0) h = dh;
            }
            w = RoomSizing.Clamp(w);
            h = RoomSizing.Clamp(h);
            return new Vector2(Mathf.Max(MinCardPx, w * PxPerTile), Mathf.Max(MinCardPx, h * PxPerTile));
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
