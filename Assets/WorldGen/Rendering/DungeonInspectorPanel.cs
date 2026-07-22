using System.Collections.Generic;
using SFB;
using UnityEngine;
using UnityEngine.UI;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Sidebar room inspector + validation panel (Task 5 of the room-graph rework). Hosted as a child
    /// of DungeonEditorScreen.Sidebar; owns a single vertical ScrollRect (mirrors the pre-Task-1
    /// DungeonEditorScreen.BuildKeySidebar recipe: ScrollRect + Viewport(RectMask2D) + Content
    /// (VerticalLayoutGroup + ContentSizeFitter)) built directly on this component's own transform,
    /// which the host stretches to fill Sidebar — the same self-contained composition DungeonViewController
    /// uses for MapArea.
    ///
    /// ShowRoom/ShowValidation both funnel into one Rebuild() that destroys and reconstructs the whole
    /// content tree from (roomId, lastIssues). This is safe against losing in-progress text edits only
    /// because every InputField commits on `onEndEdit` (not per-keystroke) — by the time a rebuild can
    /// happen (triggered by OnChanged, which only fires after a commit), there is no pending edit to lose.
    ///
    /// Deviation from the original brief: the type-change confirm for a singleton demote (second
    /// Вход/Босс) does NOT use ConfirmDialog. ConfirmDialog only ships two shapes — a destructive
    /// «Отмена»/«Удалить» confirm and a single-«Ок» info dialog — neither reads correctly for a neutral
    /// "change type?" prompt («Удалить» would misleadingly suggest deletion). DungeonOps.SetRoomType is
    /// called directly; it auto-demotes the prior holder to Normal, and the demoted room's card recolors
    /// immediately on the next viewController.Refresh() (via OnChanged), which is clear feedback on its own.
    /// </summary>
    public class DungeonInspectorPanel : MonoBehaviour
    {
        public System.Action OnChanged;   // fires after any edit; screen re-runs validation + view.BeginCascade()
        public System.Action<int> OnOpenBattleGridRequested;   // room id

        InteriorData dungeon;
        System.Func<int> currentLevelIndex;
        Font font;

        int roomId;                                        // 0 = no selection
        List<DungeonIssue> lastIssues = new List<DungeonIssue>();

        RectTransform content;
        bool built;

        InteriorFloor CurrentLevel
        {
            get
            {
                if (dungeon == null || currentLevelIndex == null) return null;
                int idx = currentLevelIndex();
                return idx >= 0 && idx < dungeon.Floors.Count ? dungeon.Floors[idx] : null;
            }
        }

        void Awake() { if (isActiveAndEnabled) EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }   // hot-reload guard
            BuildScroll();
            built = true;
        }

        public void Bind(InteriorData dungeon, System.Func<int> currentLevelIndex, Font font)
        {
            EnsureBuilt();
            this.dungeon = dungeon;
            this.currentLevelIndex = currentLevelIndex;
            this.font = font;
        }

        /// <summary>Render the inspector for a room; 0 clears to a muted hint. Falls back to the hint if
        /// the id no longer resolves on the current level (e.g. stale selection after a level switch or
        /// a delete elsewhere).</summary>
        public void ShowRoom(int id)
        {
            EnsureBuilt();
            roomId = id;
            Rebuild();
        }

        public void ShowValidation(List<DungeonIssue> issues)
        {
            EnsureBuilt();
            lastIssues = issues ?? new List<DungeonIssue>();
            Rebuild();
        }

        // ── Rebuild ──────────────────────────────────────────────────────────────

        void Rebuild()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

            var lvl = CurrentLevel;
            Room room = (lvl != null && roomId != 0) ? lvl.GetRoom(roomId) : null;
            if (room == null) roomId = 0;

            if (room == null)
                AddInfoText(content, "Выберите комнату", 12, ThemeRole.Mut, FontStyle.Italic);
            else
            {
                // Settlement building (Ц1.5 Task 6): a dummy is decorative filler, not authorable — it gets
                // ONLY the minimal BuildDummySection (info line + toggle button) and NOTHING else: no room/
                // name/description, no preview, no battle-map/secrets/corridors controls either (a dummy has
                // no gameplay content to route to). Gated on Kind == Settlement && TypeId == 1 so gates
                // (TypeId 0) and every dungeon/building room fall through to the unchanged else-branch below,
                // byte-identical to pre-Task-6 behaviour.
                bool isSettlementBuilding = dungeon != null && dungeon.Kind == InteriorKind.Settlement && room.TypeId == 1;
                if (isSettlementBuilding && room.IsDummy)
                {
                    BuildDummySection(content, room);
                }
                else
                {
                    // On a generated upper floor the ROOM (type/size) is locked, but TRANSITIONS stay editable — the
                    // DM authors which rooms connect. So corridors + secret passages are always shown; only
                    // BuildRoomSection hides the layout-affecting type/size controls when locked.
                    BuildRoomSection(content, lvl, room, StructureLocked);
                    if (isSettlementBuilding)
                    {
                        BuildPreviewSection(content, room);
                        BuildDummyToggleSection(content, room);
                    }
                    // Final-review fix C2: a settlement building gets no direct battle map — its interior comes
                    // via building-interior recursion (Ц2), not a room-level grid over the WHOLE town's ~40-node
                    // graph (BattleGridGenerator.ProjectDoors routes RoutingMode.Clean, a multi-second hang at
                    // settlement scale). Gate at the room-graph level (Kind), not room.TypeId, so it also covers
                    // non-building rooms on a settlement floor (e.g. the gate room).
                    if (dungeon == null || dungeon.Kind != InteriorKind.Settlement)
                        BuildBattleGridSection(content, room);
                    BuildSecretsSection(content, room);
                    BuildCorridorsSection(content, lvl, room);
                }
                AddDivider(content);
            }
            BuildValidationSection(content);
        }

        /// <summary>A building's GENERATED upper floors are machine-maintained (DungeonEditorScreen's generate-only
        /// toolbar): the LAYOUT-affecting controls — room type and size — are not hand-edited here, because a
        /// type change could break the shared stairwell and a resize would overlap neighbours with no re-pack.
        /// Transitions (corridors, secret passages) and Title/Body stay editable — they don't move rooms. Floor 0
        /// and dungeons are fully editable.</summary>
        bool StructureLocked =>
            dungeon != null && dungeon.Kind == InteriorKind.Building && currentLevelIndex != null && currentLevelIndex() > 0;

        // ── Комната ──────────────────────────────────────────────────────────────

        void BuildRoomSection(Transform parent, InteriorFloor lvl, Room room, bool locked)
        {
            var sec = AddSection(parent, "RoomSection");
            AddInfoText(sec.transform, "КОМНАТА", 10, ThemeRole.Mut, FontStyle.Bold);

            if (locked)
            {
                // Generated upper floor: type + size are locked (layout is machine-maintained). Transitions and
                // notes below stay editable; a hint explains why type/size are absent.
                AddInfoText(sec.transform, "Раскладка этажа генерируется («Перегенерировать»). Тип и размер не меняются; переходы и заметки — редактируются.",
                    10, ThemeRole.Mut, FontStyle.Italic);
            }
            else
            {
                var typeRow = AddRow(sec.transform, "TypeRow", 26f, 4f);
                var profile = Profiles.ForRoom(dungeon);
                for (int i = 0; i < profile.RoomTypes.Length; i++)
                {
                    int typeId = i;   // capture for the closure
                    AddChoiceButton(typeRow.transform, profile.RoomTypes[i].Label, room.TypeId == typeId, () => SetType(lvl, room, typeId));
                }

                // Размер: [W-] W [W+]  ×  [H-] H [H+] — two nested BuildStepper rows inside one outer row,
                // same nesting precedent as the secret-passage «Эт./Ком.» steppers below (targetRow). Each
                // stepper clamps into RoomSizing's 1..MaxSide range and fires OnChanged, which runs
                // RevalidateAndRefresh → viewController.BeginCascade(), so the room resizes and the cascade
                // animates the whole floor to its re-settled positions.
                var sizeRow = AddRow(sec.transform, "SizeRow", 22f, 4f);
                var sizeCap = MakeText(sizeRow.transform, "Размер:", 10, ThemeRole.Mut, FontStyle.Normal, TextAnchor.MiddleLeft);
                sizeCap.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
                sizeCap.raycastTarget = false;
                BuildStepper(sizeRow.transform, "W", room.SizeW.ToString(),
                    () => ResizeRoom(room, -1, 0), () => ResizeRoom(room, 1, 0), true);
                var sizeX = MakeText(sizeRow.transform, "×", 11, ThemeRole.Mut, FontStyle.Normal, TextAnchor.MiddleCenter);
                sizeX.gameObject.AddComponent<LayoutElement>().preferredWidth = 12f;
                sizeX.raycastTarget = false;
                BuildStepper(sizeRow.transform, "H", room.SizeH.ToString(),
                    () => ResizeRoom(room, 0, -1), () => ResizeRoom(room, 0, 1), true);
            }

            var titleField = BuildInputField(sec.transform, false, "Название комнаты");
            titleField.text = room.Title;
            titleField.onEndEdit.AddListener(v => { room.Title = v; OnChanged?.Invoke(); });

            var bodyField = BuildInputField(sec.transform, true, "Заметки: что здесь, ловушки, добыча…");
            bodyField.text = room.Body;
            bodyField.onEndEdit.AddListener(v => { room.Body = v; OnChanged?.Invoke(); });
        }

        // Singleton demote is silent (see class doc) — SetRoomType handles the auto-demote itself.
        void SetType(InteriorFloor lvl, Room room, int type)
        {
            DungeonOps.SetRoomType(lvl, room.Id, type);
            Rebuild();
            OnChanged?.Invoke();
        }

        // dw/dh are ±1 (or 0) nudges to SizeW/SizeH; RoomSizing.Clamp keeps the result in 1..MaxSide even
        // at the range edges (no explicit bounds check needed — mirrors StepFloor/StepRoom's own style).
        void ResizeRoom(Room room, int dw, int dh)
        {
            if (dw != 0) room.SizeW = RoomSizing.Clamp(room.SizeW + dw);
            if (dh != 0) room.SizeH = RoomSizing.Clamp(room.SizeH + dh);
            Rebuild();
            OnChanged?.Invoke();
        }

        // ── Превью здания ────────────────────────────────────────────────────────

        // Same image-extension filters PoiEditorScreen.IconFilters uses — this is the same "pick a
        // picture file" gesture, just landing in Room.Preview instead of PoiData.CustomIconBytes.
        static readonly ExtensionFilter[] PreviewImageFilters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        // Settlement-building-only section (Task 11 of the settlements arc): an optional picture the DM
        // attaches to a building room, modelled on PoiEditorScreen's icon controls (OnPickIconClicked /
        // BuildIconRow, :246-255 / :578-609) — pick a file, shrink it, assign, notify. Callers gate this
        // on dungeon.Kind == Settlement && room.TypeId == 1 (buildings only; never gates or dungeons/
        // stand-alone buildings). Both edits below call Rebuild() (so the thumbnail/«Убрать» button
        // appear or disappear immediately in THIS panel) and OnChanged?.Invoke() (so the room-graph view's
        // per-card has-image indicator — Task 10 — gets rebuilt too; see DungeonEditorScreen.
        // RevalidateAndRefresh -> DungeonViewController.BeginCascade -> Refresh -> renderer.RebuildView).
        void BuildPreviewSection(Transform parent, Room room)
        {
            var sec = AddSection(parent, "PreviewSection");
            AddInfoText(sec.transform, "ПРЕВЬЮ ЗДАНИЯ", 10, ThemeRole.Mut, FontStyle.Bold);

            if (room.Preview != null)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(room.Preview))
                {
                    var thumbGO = new GameObject("Thumb", typeof(RectTransform));
                    thumbGO.transform.SetParent(sec.transform, false);
                    var img = thumbGO.AddComponent<Image>();
                    img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    thumbGO.AddComponent<LayoutElement>().preferredHeight = 100f;
                }
            }

            AddFullWidthButton(sec.transform, "Добавить изображение", ThemeRole.Elev, () => PickPreviewImage(room));

            if (room.Preview != null)
                AddFullWidthButton(sec.transform, "Убрать", ThemeRole.Danger, () =>
                {
                    room.Preview = null;
                    Rebuild();
                    OnChanged?.Invoke();
                });
        }

        // StandaloneFileBrowser.OpenFilePanel -> ImageImport.LoadAndShrink(path, 512) -> Room.Preview.
        // LoadAndShrink bounds the longest side to 512px so a 60-building town's saved images stay small
        // (spec §9) and re-encodes to PNG regardless of the source format. A null result (unreadable file,
        // undecodable image) is silently ignored per the brief — no assignment, no Rebuild/OnChanged, same
        // as a cancelled file-picker.
        void PickPreviewImage(Room room)
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать изображение", "", PreviewImageFilters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            byte[] shrunk = ImageImport.LoadAndShrink(paths[0], 512);
            if (shrunk == null) return;
            room.Preview = shrunk;
            Rebuild();
            OnChanged?.Invoke();
        }

        // ── Пустышка (активно/пустышка) ─────────────────────────────────────────

        // Active-state addendum (Ц1.5 Task 6): appended after BuildPreviewSection for an active settlement
        // building. The handler ONLY flips IsDummy and calls OnChanged — same minimal shape as the
        // Title/Body onEndEdit handlers above (no local Rebuild(): OnChanged already reaches
        // RevalidateAndRefresh -> inspectorPanel.ShowRoom(selectedRoomId) -> Rebuild(), which redraws this
        // panel into the dummy's minimal view). Non-destructive: Title/Body/Preview are left untouched,
        // just hidden while the room is a dummy — toggling back restores them unchanged.
        void BuildDummyToggleSection(Transform parent, Room room)
        {
            var sec = AddSection(parent, "DummyToggleSection");
            AddFullWidthButton(sec.transform, "Сделать пустышкой", ThemeRole.Elev, () =>
            {
                room.IsDummy = true;
                OnChanged?.Invoke();
            });
        }

        // Dummy-state minimal panel (Ц1.5 Task 6): a dummy is decorative filler, not authorable — callers
        // build ONLY this section instead of BuildRoomSection/BuildPreviewSection (no name/description/
        // preview controls). The toggle-back handler mirrors BuildDummyToggleSection's shape exactly.
        void BuildDummySection(Transform parent, Room room)
        {
            var sec = AddSection(parent, "DummySection");
            AddInfoText(sec.transform, "Пустышка — декоративное здание, игрок не взаимодействует", 11, ThemeRole.Mut, FontStyle.Italic);
            AddFullWidthButton(sec.transform, "Сделать активным", ThemeRole.Elev, () =>
            {
                room.IsDummy = false;
                OnChanged?.Invoke();
            });
        }

        // ── Боевая карта ─────────────────────────────────────────────────────────

        // Own section — it used to be appended to СЕКРЕТНЫЕ ХОДЫ below, where it rendered directly beneath
        // «+ Секретный ход» and read as a secret-passage control. A battle map is a property of the ROOM
        // itself. Deliberately NOT gated by `locked` the way BuildRoomSection's type/size controls are:
        // battle maps are available on every floor, including a building's generated upper floors — unlike
        // room type/size they are never machine-maintained, so the structure lock does not apply to them.
        void BuildBattleGridSection(Transform parent, Room room)
        {
            var sec = AddSection(parent, "BattleGridSection");
            // PRIMARY styling (accent fill, AccentInk label, taller, larger type): this is the one control
            // in the inspector that opens a whole other screen, and as a plain Elev button it read as just
            // another minor action. Same accent the toolbar's active tab uses, so both themes follow along.
            AddFullWidthButton(sec.transform, "Боевая карта", ThemeRole.Accent,
                               () => OnOpenBattleGridRequested?.Invoke(room.Id),
                               labelRole: ThemeRole.AccentInk, height: 30f, fontSize: 13);
        }

        // ── Секретные ходы ───────────────────────────────────────────────────────

        // No InteriorFloor param needed — secret targets are resolved against dungeon.Floors directly
        // (SafeLevelIndex/StepFloor/StepRoom), not the current room's own level.
        void BuildSecretsSection(Transform parent, Room room)
        {
            var sec = AddSection(parent, "SecretsSection");
            AddInfoText(sec.transform, "СЕКРЕТНЫЕ ХОДЫ", 10, ThemeRole.Mut, FontStyle.Bold);

            // Snapshot: BuildSecretRow only registers click callbacks during this loop, it never mutates
            // room.Portals while iterating — a defensive copy costs nothing and rules the concern out.
            foreach (var s in new List<Portal>(room.Portals))
                BuildSecretRow(sec.transform, room, s);

            AddFullWidthButton(sec.transform, "+ Секретный ход", ThemeRole.Elev, () =>
            {
                DungeonOps.AddSecret(room);
                Rebuild();
                OnChanged?.Invoke();
            });
        }

        void BuildSecretRow(Transform parent, Room room, Portal s)
        {
            var row = AddSection(parent, "SecretRow", 4f, new RectOffset(6, 6, 4, 4));
            ThemeService.Tag(row.gameObject.AddComponent<Image>(), ThemeRole.Panel2);

            var header = AddRow(row.transform, "Header", 24f, 4f);
            AddChoiceButton(header.transform, "Комната", s.Kind == PortalKind.SecretDoor,
                () => { s.Kind = PortalKind.SecretDoor; Rebuild(); OnChanged?.Invoke(); });
            AddChoiceButton(header.transform, "Выход", s.Kind == PortalKind.DungeonExit,
                () => { s.Kind = PortalKind.DungeonExit; Rebuild(); OnChanged?.Invoke(); });
            AddRemoveButton(header.transform, () => { DungeonOps.RemoveSecret(room, s); Rebuild(); OnChanged?.Invoke(); });

            if (s.Kind == PortalKind.SecretDoor)
            {
                var targetRow = AddRow(row.transform, "Target", 22f, 4f);
                bool floorEnabled = dungeon != null && dungeon.Floors.Count > 1;
                BuildStepper(targetRow.transform, "Эт.", FloorLabel(s), () => StepFloor(s, -1), () => StepFloor(s, 1), floorEnabled);
                bool roomEnabled = dungeon != null && dungeon.Floors.Count > 0 && dungeon.Floors[SafeLevelIndex(s)].Rooms.Count > 0;
                BuildStepper(targetRow.transform, "Ком.", RoomLabel(s), () => StepRoom(s, -1), () => StepRoom(s, 1), roomEnabled);
            }

            var biRow = AddRow(row.transform, "Bi", 22f, 0f);
            AddBoolToggle(biRow.transform, "Двусторонний", s.Bidirectional, v => { s.Bidirectional = v; Rebuild(); OnChanged?.Invoke(); });

            var labelField = BuildInputField(row.transform, false, "Подпись хода");
            labelField.text = s.Label;
            labelField.onEndEdit.AddListener(v => { s.Label = v; OnChanged?.Invoke(); });
        }

        // Target level, safely wrapped into 0..Floors.Count-1 even if serialized data drifted out of range.
        int SafeLevelIndex(Portal s)
        {
            if (dungeon == null || dungeon.Floors.Count == 0) return 0;
            int n = dungeon.Floors.Count;
            return ((s.TargetFloorIndex % n) + n) % n;
        }

        string FloorLabel(Portal s) =>
            dungeon != null && dungeon.Floors.Count > 0 ? $"Эт.{SafeLevelIndex(s) + 1}" : "-";

        // Invalid target (TargetRoomId not present on the target level) is shown, not hidden — the
        // validator already raises a hard error for it; the stepper still recovers it (jumps to the
        // list's first room) on the next click.
        string RoomLabel(Portal s)
        {
            if (dungeon == null || dungeon.Floors.Count == 0) return "-";
            var targetLvl = dungeon.Floors[SafeLevelIndex(s)];
            return targetLvl.GetRoom(s.TargetRoomId) != null ? s.TargetRoomId.ToString() : $"{s.TargetRoomId}⚠";
        }

        void StepFloor(Portal s, int dir)
        {
            if (dungeon == null || dungeon.Floors.Count == 0) return;
            int n = dungeon.Floors.Count;
            s.TargetFloorIndex = ((SafeLevelIndex(s) + dir) % n + n) % n;
            Rebuild();
            OnChanged?.Invoke();
        }

        void StepRoom(Portal s, int dir)
        {
            if (dungeon == null || dungeon.Floors.Count == 0) return;
            var targetLvl = dungeon.Floors[SafeLevelIndex(s)];
            if (targetLvl.Rooms.Count == 0) return;
            int idx = targetLvl.Rooms.FindIndex(r => r.Id == s.TargetRoomId);
            idx = idx < 0 ? 0 : ((idx + dir) % targetLvl.Rooms.Count + targetLvl.Rooms.Count) % targetLvl.Rooms.Count;
            s.TargetRoomId = targetLvl.Rooms[idx].Id;
            Rebuild();
            OnChanged?.Invoke();
        }

        // ── Коридоры ─────────────────────────────────────────────────────────────

        void BuildCorridorsSection(Transform parent, InteriorFloor lvl, Room room)
        {
            var sec = AddSection(parent, "CorridorsSection");
            AddInfoText(sec.transform, "КОРИДОРЫ", 10, ThemeRole.Mut, FontStyle.Bold);

            bool any = false;
            foreach (var c in lvl.Links)
            {
                if (c.RoomA != room.Id && c.RoomB != room.Id) continue;
                any = true;
                int otherId = c.RoomA == room.Id ? c.RoomB : c.RoomA;
                var row = AddRow(sec.transform, $"Corridor_{otherId}", 24f, 4f);
                var lbl = MakeText(row.transform, $"↔ Комната {otherId}", 11, ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleLeft);
                lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                lbl.raycastTarget = false;
                AddRemoveButton(row.transform, () => { DungeonOps.RemoveCorridor(lvl, room.Id, otherId); Rebuild(); OnChanged?.Invoke(); });
            }
            if (!any) AddInfoText(sec.transform, "Нет коридоров", 11, ThemeRole.Mut, FontStyle.Italic);
        }

        // ── Проверки ─────────────────────────────────────────────────────────────

        void BuildValidationSection(Transform parent)
        {
            var sec = AddSection(parent, "ValidationSection");
            AddInfoText(sec.transform, "ПРОВЕРКИ", 10, ThemeRole.Mut, FontStyle.Bold);

            int curLvl = currentLevelIndex != null ? currentLevelIndex() : -1;
            var relevant = lastIssues.FindAll(i => i.LevelIndex == curLvl);
            if (relevant.Count == 0)
                AddInfoText(sec.transform, "Проверки пройдены", 11, ThemeRole.Mut, FontStyle.Italic);
            else
                foreach (var issue in relevant)
                {
                    var role = issue.Severity == IssueSeverity.Error ? ThemeRole.Danger : ThemeRole.Mut;
                    string prefix = issue.Severity == IssueSeverity.Error ? "⚠ " : "• ";
                    AddInfoText(sec.transform, prefix + issue.Message, 11, role, FontStyle.Normal);
                }
        }

        // ── Small builder primitives (self-contained, mirrors DungeonEditorScreen/DungeonViewController) ──

        void BuildScroll()
        {
            var scrollGO = new GameObject("Scroll", typeof(RectTransform));
            scrollGO.transform.SetParent(transform, false);
            Stretch(scrollGO.GetComponent<RectTransform>());
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            viewportGO.AddComponent<RectMask2D>();
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            Stretch(viewportGO.GetComponent<RectTransform>());
            scroll.viewport = viewportGO.GetComponent<RectTransform>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;
        }

        // A vertical block (caption + rows) that stretches to full sidebar width; height is
        // content-driven (sum of its own children), matching the old BuildKeySidebar row recipe.
        VerticalLayoutGroup AddSection(Transform parent, string name, float spacing = 4f, RectOffset padding = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = padding ?? new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            return vlg;
        }

        // A single horizontal row of fixed height. childControlWidth=true + per-child LayoutElement
        // (fixed preferredWidth or flexibleWidth) mirrors the old BuildKeyRow header recipe — the one
        // proven way in this codebase to mix fixed-width controls with a width that fills remaining space.
        RectTransform AddRow(Transform parent, string name, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go.GetComponent<RectTransform>();
        }

        // Auto-height informational text (caption/hint/validation line) — no LayoutElement.preferredHeight
        // so the row's own ancestor VerticalLayoutGroup(childControlHeight=true) measures it from its
        // wrapped content, same idiom as ConfirmDialog's message Body text.
        Text AddInfoText(Transform parent, string message, int size, ThemeRole role, FontStyle style)
        {
            var t = MakeText(parent, message, size, role, style, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            return t;
        }

        void AddDivider(Transform parent)
        {
            var go = new GameObject("Divider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Border);
            img.raycastTarget = false;
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
        }

        // Shares remaining row width equally with sibling choice buttons (flexibleWidth=1) — used for
        // both the 3-way type row and the 2-way secret-kind toggle.
        void AddChoiceButton(Transform parent, string label, bool active, System.Action onClick)
        {
            var go = new GameObject($"Choice_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, active ? ThemeRole.AccentSoft : ThemeRole.Elev);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 11, active ? ThemeRole.AccentInk : ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        void AddRemoveButton(Transform parent, System.Action onClick)
        {
            var go = new GameObject("Remove");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Danger, 0.25f);
            go.AddComponent<LayoutElement>().preferredWidth = 22f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, "✕", 12, ThemeRole.Danger, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        /// <summary>A full-width button. The label role, height and font size are parameters so a PRIMARY
        /// button can be built from the same recipe as a secondary one: an accent fill needs
        /// ThemeRole.AccentInk text (dark on gold in the dark theme, white on indigo in the light one),
        /// because the default Txt colour is unreadable on an accent background in both themes.</summary>
        void AddFullWidthButton(Transform parent, string label, ThemeRole bgRole, System.Action onClick,
                                ThemeRole labelRole = ThemeRole.Txt, float height = 24f, int fontSize = 11)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, bgRole);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, fontSize, labelRole, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        void AddBoolToggle(Transform parent, string label, bool value, System.Action<bool> onToggle)
        {
            var go = new GameObject($"Bool_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, value ? ThemeRole.AccentSoft : ThemeRole.Elev);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onToggle(!value));
            var lbl = MakeText(go.transform, $"{(value ? "✓" : "✗")} {label}", 11,
                value ? ThemeRole.AccentInk : ThemeRole.Txt, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        // ◄ value ► stepper (used for the secret-passage floor/room targets — simpler and more robust
        // than a Dropdown per the task brief). `interactable` disables both arrow buttons (e.g. only one
        // floor exists, or the target floor has zero rooms) without hiding the row.
        void BuildStepper(Transform parent, string caption, string valueLabel, System.Action onPrev, System.Action onNext, bool interactable)
        {
            var row = AddRow(parent, $"Step_{caption}", 20f, 2f);
            var capLbl = MakeText(row.transform, caption, 10, ThemeRole.Mut, FontStyle.Normal, TextAnchor.MiddleLeft);
            capLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 24f;
            capLbl.raycastTarget = false;
            AddStepBtn(row.transform, "◄", onPrev, interactable);
            var valTxt = MakeText(row.transform, valueLabel, 11, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            valTxt.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            valTxt.raycastTarget = false;
            AddStepBtn(row.transform, "►", onNext, interactable);
        }

        void AddStepBtn(Transform parent, string label, System.Action onClick, bool interactable)
        {
            var go = new GameObject($"Step_{label}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            ThemeService.Tag(img, ThemeRole.Elev);
            go.AddComponent<LayoutElement>().preferredWidth = 20f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = interactable;
            btn.onClick.AddListener(() => onClick());
            var lbl = MakeText(go.transform, label, 11, ThemeRole.Txt, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(lbl.rectTransform); lbl.raycastTarget = false;
        }

        /// <summary>Self-contained InputField builder — copied from the pre-Task-1 DungeonEditorScreen's
        /// BuildInputField (removed in Task 1's gut) since this panel needs the same recipe.</summary>
        InputField BuildInputField(Transform parent, bool multiline, string placeholder)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            ThemeService.Tag(bg, ThemeRole.Panel2, 0.95f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

            var text = MakeText(go.transform, "", 12, ThemeRole.Txt, FontStyle.Normal, TextAnchor.UpperLeft);
            text.supportRichText = false;
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0.03f, 0f);
            textRect.anchorMax = new Vector2(0.98f, 1f);
            textRect.sizeDelta = Vector2.zero;
            field.textComponent = text;

            var ph = MakeText(go.transform, placeholder, 12, ThemeRole.Mut, FontStyle.Italic, TextAnchor.UpperLeft);
            var phRect = ph.rectTransform;
            phRect.anchorMin = new Vector2(0.03f, 0f);
            phRect.anchorMax = new Vector2(0.98f, 1f);
            phRect.sizeDelta = Vector2.zero;
            field.placeholder = ph;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = multiline ? 54f : 22f;
            le.flexibleWidth = 1f;
            return field;
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
