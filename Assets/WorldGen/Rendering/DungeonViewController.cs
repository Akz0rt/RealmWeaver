using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using WorldGen.Generation;
using WorldGen.Notes.Rendering;

namespace WorldGen.Rendering
{
    /// <summary>
    /// Owns ALL dungeon-floor editing mechanics: binding, selection, link mode, drag, delete/add, and the
    /// animated cascade. Draws NOTHING — it delegates every visual to an IDungeonRenderer and swaps that
    /// renderer when the Граф/Изо toggle flips (sub-project 3 revision, spec R5).
    ///
    /// The key move: pointer input is converted into TILE space via the active renderer's
    /// DungeonProjection, and hit-testing runs against room footprints IN TILES. Since Граф and Изо share
    /// one projection type differing only by SquashY, this single code path drives editing in BOTH views —
    /// editing in Изо (spec R4) required no second implementation.
    ///
    /// Sits on the same GameObject hierarchy as before (a child of DungeonEditorScreen.MapArea) and
    /// carries the same rect gotcha: never read a rect at Bind time (it is {0,0} pre-activation) —
    /// ResolveProjection is retried from LateUpdate until it succeeds.
    /// </summary>
    public class DungeonViewController : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<int> OnRoomSelected;   // fires with a room id, or 0 when selection clears
        public System.Action<int> OnRoomDoubleClicked;  // fires with a room id — opens its battle map
        public System.Action OnGraphMutated;        // fires after add/delete/link/drag-end (structural change)
        public System.Action<int> OnJumpToLevel;    // fires with a target level index (badge click)
        // Fires when the room positions have reached their FINAL resting values — either at the end of the
        // animation (Update's completion branch) or immediately from BeginCascade's !anyMoved early return.
        // The host uses it to re-run VALIDATION only: between BeginCascade and this callback the rooms are
        // deliberately rolled back to their pre-settle start positions so they can be animated, so anything
        // that inspects geometry in that window sees a state that is about to be thrown away. Must NOT be
        // wired to anything that mutates the graph or calls BeginCascade again — that would re-enter and loop.
        public System.Action OnCascadeSettled;

        public int SelectedRoomId { get; private set; }
        public bool LinkMode { get; private set; }
        /// <summary>True between BeginCascade and OnCascadeSettled — i.e. while the rooms are animating and
        /// their positions are deliberately NOT the settled ones. A host that reads room geometry (the shaft
        /// check is the only such rule today) must wait for OnCascadeSettled instead of reading it now.</summary>
        public bool Cascading => cascading;

        InteriorData dungeon;
        int levelIndex;
        InteriorFloor boundLevel;   // last-bound level OBJECT (not just index) — see Bind's sameBinding check
        Font font;
        IDungeonRenderer renderer;
        int pendingLinkId;
        bool needsProjectionFit;
        int draggingRoomId;
        // The room the current settle should treat as the "just-moved" one: the last room the DM dragged
        // (OnEndDrag) or a freshly added room (AddRoomAtCenter). For a BUILDING it's the room BeginCascade
        // nudges off overlaps; for a DUNGEON it's the leash/Separate anchor. Held here because OnEndDrag has
        // already cleared draggingRoomId by the time OnGraphMutated → BeginCascade runs.
        int lastAnchorRoomId;

        // Animated cascade state — moved verbatim from DungeonGraphView (Checkpoint-B tuning, user-approved
        // "мне нравится"). cascadeTargets holds the resolved end position per room id (computed once by
        // DungeonLayout.Separate at cascade start); cascadeVel is SmoothDamp's per-room velocity
        // accumulator. Both null when not cascading. DO NOT retune without a user checkpoint.
        bool cascading;
        Dictionary<int, (float x, float y)> cascadeTargets;
        Dictionary<int, Vector2> cascadeVel;
        const float CascadeSmoothTime = 0.18f;
        const float CascadeDoneEpsilon = 5e-4f;

        // Drag clamp in NORMALIZED space — same 0.04..0.96 margin the old DungeonGraphView.OnCardDragged
        // used, so a room can never be dragged fully off the board.
        const float DragClampMin = 0.04f;
        const float DragClampMax = 0.96f;

        InteriorFloor BoundLevel =>
            dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Floors.Count
                ? dungeon.Floors[levelIndex] : null;

        // Extra breathing room (tiles) fit around the contour so the blue outline isn't flush against the
        // canvas edge. Beyond ContourMargin (which sets how far the outline sits from the rooms).
        const float ContourViewPad = 2f;

        /// <summary>Tile-space bounds to fit the projection to for `lvl`. For a BUILDING the fit is CONSTANT
        /// across floors — floor 0's footprint bbox expanded by the contour margin + a little pad — so every
        /// floor renders at the IDENTICAL scale and position, and the stairwell column (and every room) looks
        /// absolutely the same on every floor (user 2026-07-19). The current floor is NOT unioned in: upper
        /// floors are generated within this outline (nothing is clipped), and floor 0's own rooms ARE the
        /// outline, so a per-floor union would only make the fit jitter between floors. Dungeons: the current
        /// floor's own bounds — byte-identical to the pre-contour per-floor fit.</summary>
        (float minX, float minY, float maxX, float maxY) FitBoundsFor(InteriorFloor lvl)
        {
            if (dungeon != null && dungeon.Kind == InteriorKind.Building && dungeon.Floors.Count > 0)
            {
                var c = DungeonProjection.ContentBoundsTiles(dungeon.Floors[0]);
                float pad = FloorFootprint.ContourMargin + ContourViewPad;
                return (c.minX - pad, c.minY - pad, c.maxX + pad, c.maxY + pad);
            }
            return DungeonProjection.ContentBoundsTiles(lvl);
        }

        /// <summary>Swap the active renderer (Граф ⇄ Изо). Deactivates the old host, activates the new,
        /// re-fits the new renderer's projection to the bound level and rebuilds it. Selection, link mode
        /// and cascade state all survive the swap — they live here, not in the renderer.</summary>
        public void SetRenderer(IDungeonRenderer next)
        {
            if (next == null || ReferenceEquals(next, renderer)) return;
            if (renderer != null && renderer.Host != null) renderer.Host.SetActive(false);
            renderer = next;
            if (renderer.Host != null) renderer.Host.SetActive(true);
            needsProjectionFit = true;   // the new host's rect may not have laid out yet — LateUpdate retries
            Refresh();
        }

        /// <summary>(Re)bind to a level and rebuild. Selection/link state resets only when the dungeon or
        /// the bound InteriorFloor OBJECT actually changes — a same-level re-Bind (DungeonEditorScreen
        /// re-Binds on every refresh) preserves the current selection instead of stomping it. Keying on the
        /// OBJECT, not the index: RemoveCurrentLevel re-binds the same index to a DIFFERENT level, and
        /// keying on the index alone would let a stale SelectedRoomId match an unrelated room.</summary>
        public void Bind(InteriorData dungeon, int levelIndex, Font font)
        {
            var newLevel = (dungeon != null && levelIndex >= 0 && levelIndex < dungeon.Floors.Count)
                ? dungeon.Floors[levelIndex] : null;
            bool sameBinding = this.dungeon == dungeon && this.levelIndex == levelIndex && newLevel == boundLevel;
            this.dungeon = dungeon;
            this.levelIndex = levelIndex;
            this.font = font;
            boundLevel = newLevel;
            if (!sameBinding)
            {
                SelectedRoomId = 0;
                pendingLinkId = 0;
                // Same stale-id-across-bind hazard as SelectedRoomId: a leftover anchor from the previous
                // level may coincidentally match a room here, making this level's first cascade depend on
                // what was dragged before the switch. Fall back to the leash's own deterministic root.
                lastAnchorRoomId = 0;
                // A stale cascade from the PREVIOUS level must not keep running against the new one
                // (wrong ids, wrong targets) — cancel outright on a genuine level switch.
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                // Re-fit the scale to the new level's content (spec R6: fit once per bind, then hold).
                needsProjectionFit = true;
            }
            Refresh();
        }

        /// <summary>Full visual rebuild from the bound level.</summary>
        public void Refresh()
        {
            if (renderer == null) return;
            var lvl = BoundLevel;
            if (lvl == null) { renderer.RebuildView(dungeon, levelIndex, null, new RenderGraph(), font, OnJumpToLevel); return; }

            if (needsProjectionFit)
            {
                var (fminX, fminY, fmaxX, fmaxY) = FitBoundsFor(lvl);
                if (renderer.ResolveProjection(fminX, fminY, fmaxX, fmaxY)) needsProjectionFit = false;
            }

            var rg = DungeonLayout.BuildRenderGraph(lvl);
            if (SelectedRoomId != 0 && lvl.GetRoom(SelectedRoomId) == null) SelectedRoomId = 0;
            renderer.RebuildView(dungeon, levelIndex, lvl, rg, font, OnJumpToLevel);
            RefreshHighlights();
        }

        void LateUpdate()
        {
            if (!needsProjectionFit || renderer == null) return;
            var lvl = BoundLevel;
            if (lvl == null) return;
            var (minX, minY, maxX, maxY) = FitBoundsFor(lvl);
            if (!renderer.ResolveProjection(minX, minY, maxX, maxY)) return;   // rect still {0,0} — retry next frame
            needsProjectionFit = false;
            Refresh();
        }

        // ── Cascade (moved verbatim from DungeonGraphView.cs:151-241) ────────────

        /// <summary>Entry point for the room-cascade separation. Snapshots current positions, resolves the
        /// target layout via Separate (which mutates rooms to their end positions), then either self-skips
        /// (nothing moved — a link/delete edit that never overlapped) with one static redraw, or restores
        /// the start positions and animates to the captured targets in Update() via SmoothDamp.</summary>
        public void BeginCascade()
        {
            var lvl = BoundLevel;
            if (lvl == null) return;

            var start = new Dictionary<int, (float x, float y)>();
            foreach (var r in lvl.Rooms) start[r.Id] = (r.X, r.Y);

            if (dungeon != null && dungeon.Kind == InteriorKind.Building)
            {
                // BUILDING (spec C4, revised 2026-07-19): a dragged room STAYS exactly where the DM dropped
                // it — NO floor re-pack, NO cascade of other rooms (the user's hard rule: dragging a room
                // must never move another). The sole correction is anti-overlap on the just-moved room itself
                // (lastAnchorRoomId): if it landed on top of another room, IT ALONE is shoved clear. A room
                // parked outside floor 0's contour is LEFT there (C2' red-flags it) — out-of-contour is a
                // deliberate choice, not auto-fixed. This mutates at most that one room's X/Y, so the shared
                // snapshot→rollback→SmoothDamp path below animates only its small nudge (every other room has
                // start==target and does not move). Floor 0 and upper floors behave identically now (no
                // per-floor contour containment).
                //
                // The shaft auto-realign (commit e61473a) is PART of this step and lives inside
                // SettleDraggedRoom, not at the RevalidateAndRefresh call site above it: the nudge can move
                // floor 0's stairwell column (the DM may drag the Лестница itself), so a realign that ran
                // BEFORE it would sync the upper floors to a position the column is about to leave. The
                // ordering is pinned headlessly by BuildingGeneratorSelfTests.SelfTestDragSettleOrdering.
                //
                // Realigning here — before the target snapshot below — puts the shaft back in sync as part of
                // the same settle. NOTE what it does NOT do: the snapshot/rollback/animate machinery below
                // only ever sees `lvl.Rooms`, i.e. the CURRENTLY VIEWED floor. When the viewed floor is floor
                // 0 (the usual case — only floor 0 is draggable), the upper-floor rooms this realign
                // translates are in NO snapshot: they are neither rolled back nor animated, they simply jump
                // to their settled positions immediately. That is fine visually (they are off-screen) but it
                // means the viewed floor and the rest of the building are momentarily inconsistent — floor 0
                // sits at its rolled-back DROP position while the upper floors already sit at the SETTLED
                // one. Any shaft check run in that window sees a mismatch of the nudge distance and reports a
                // false «лестница не совпадает со столбом». Hence OnCascadeSettled: the host re-validates only
                // once the animation has landed. Do not move validation back before the animation.
                BuildingGenerator.SettleDraggedRoom(dungeon, lvl, lastAnchorRoomId);
            }
            else
            {
                // DUNGEON — byte-identical to the pre-C4 cascade; do not touch (user re-verifies at the
                // checkpoint that dungeon drag/settle is unchanged).
                //
                // Separate → leash → Separate. Separate resolves overlap but knows nothing about corridors, so
                // on its own it can leave a corridor stretched past the leash with nothing to pull it back
                // (the leash otherwise only runs during a drag). The leash pass then re-pulls, and the second
                // Separate cleans up any overlap that pull introduced.
                //
                // Best-effort, NOT a proven joint fixpoint: the final Separate could in principle re-stretch a
                // corridor a little. Both passes are convergent and cheap, and any residual is small and
                // cosmetic — the alternative is a combined solver, which this does not need.
                DungeonLayout.Separate(lvl);
                DungeonLayout.EnforceCorridorLeash(lvl, lastAnchorRoomId);
                DungeonLayout.Separate(lvl);   // mutates rooms to resolved target positions
            }

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
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                Refresh();
                // Nothing to animate — the settled state IS the current state, so the "settled" moment is
                // now. Fires here too so the host has ONE re-validation hook that covers every path out of
                // BeginCascade (an upper floor can still have been realigned even when the viewed floor
                // itself did not move).
                OnCascadeSettled?.Invoke();
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
            if (lvl == null || cascadeTargets == null || renderer == null)
            {
                cascading = false; cascadeTargets = null; cascadeVel = null;
                return;
            }

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
            }
            RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Fast);

            if (maxRemaining < CascadeDoneEpsilon)
            {
                // Snap exactly to target (SmoothDamp asymptotically approaches but never reaches it).
                foreach (var r in lvl.Rooms)
                {
                    if (!cascadeTargets.TryGetValue(r.Id, out var target)) continue;
                    r.X = target.x; r.Y = target.y;
                }
                RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Clean);
                cascading = false;
                cascadeTargets = null;
                cascadeVel = null;
                // Rooms are now EXACTLY on their targets — the first moment the whole interior is
                // self-consistent again (see BeginCascade's building branch). Fire last, after the state is
                // cleared, so a handler that somehow re-enters cannot see a half-torn-down cascade.
                OnCascadeSettled?.Invoke();
            }
        }

        void RepositionNow(InteriorFloor lvl, RoomLinkGeometry.RoutingMode mode)
        {
            if (renderer == null || lvl == null) return;
            renderer.RepositionRooms(lvl, DungeonLayout.BuildRenderGraph(lvl, mode));
        }

        // ── Commands ─────────────────────────────────────────────────────────────

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
            SelectRoom(0);   // clears via the same path as a background click — fires OnRoomSelected(0) so
                             // the host drops the deleted id too
            Refresh();
            OnGraphMutated?.Invoke();
        }

        /// <summary>Adds a Normal room at the canvas center, selects it, rebuilds.</summary>
        public Room AddRoomAtCenter()
        {
            var lvl = BoundLevel;
            if (lvl == null) return null;
            var room = DungeonOps.AddRoom(lvl, 0.5f, 0.5f);
            // BUILDING (spec C6 / user 2026-07-19): a + room must become PART of the building, never float in
            // empty space outside the contour — attach it flush to the nearest room, so the footprint grows to
            // wrap it. The placement is FINAL — we do NOT set lastAnchorRoomId, so BeginCascade's anti-overlap
            // nudge does not drag the new room off its chosen spot. Dungeons keep their previous behaviour
            // (Separate cascade handles a new room as before).
            //
            // Only the GROUND floor is reachable here for a building: a building's UPPER floors are
            // GENERATE-ONLY, and RefreshToolbar builds them a stepper + «Перегенерировать» toolbar with NO
            // «+ Комната» button at all (DungeonEditorScreen.RefreshToolbar). The upper-floor branch this used
            // to carry — CompactLayout.PlaceNewRoomInContour, placing the room at the freest spot inside floor
            // 0's contour — was therefore dead, and its comment described a button that no longer exists. That
            // primitive is still in CompactLayout, self-tested, for the day an upper-floor «+» comes back.
            if (dungeon != null && dungeon.Kind == InteriorKind.Building)
                CompactLayout.AttachNewRoom(lvl, room.Id);
            Refresh();
            SelectRoom(room.Id);
            OnGraphMutated?.Invoke();
            return room;
        }

        // ── Input → tile space → hit-test (renderer-agnostic; the point of the split) ──

        /// <summary>Pointer screen position → this renderer's local px → TILE space. False if the renderer
        /// is missing or its rect has not laid out (never act on a garbage sample).</summary>
        bool TryPointerToTile(PointerEventData data, out float tx, out float ty)
        {
            tx = ty = 0f;
            if (renderer == null || renderer.Area == null) return false;
            // An unfitted projection (PxPerTile == 0, before the first ResolveProjection) makes
            // LocalToTile return (0,0) by design. Acting on that would clamp a dragged room to the
            // corner. The rect can be valid a frame before LateUpdate fits — reject that window.
            if (renderer.Projection.PxPerTile <= 0f) return false;
            var area = renderer.Area;
            if (area.rect.width <= 0f || area.rect.height <= 0f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position, null, out var local))
                return false;
            // ScreenPointToLocalPointInRectangle returns pivot-relative coords; the projection's local
            // space is CENTRE-relative. area is stretched with a 0.5 pivot, so they already coincide —
            // this is the same assumption DungeonGraphView.PointCenter made.
            (tx, ty) = renderer.Projection.LocalToTile(local.x, local.y);
            return true;
        }

        /// <summary>Topmost room whose FOOTPRINT contains the tile point. "Topmost" = drawn last = largest
        /// tile Y (painter's order is by Y — see DungeonIsoRenderer.DepthOf), so overlapping rooms resolve
        /// the same way they LOOK stacked. Returns 0 for a miss (background).</summary>
        int HitRoomId(InteriorFloor lvl, float tx, float ty)
        {
            int best = 0;
            float bestY = float.MinValue;
            foreach (var r in lvl.Rooms)
            {
                if (!DungeonProjection.HitTest(r, tx, ty)) continue;
                if (r.Y > bestY) { bestY = r.Y; best = r.Id; }
            }
            return best;
        }

        public void OnPointerClick(PointerEventData data)
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            // Release of a drag — not a click. Read data.dragging; do NOT consume draggingRoomId here.
            // Unity's release order is pointerUp → pointerCLICK → endDrag, so this handler runs BEFORE
            // OnEndDrag. Clearing draggingRoomId here would make OnEndDrag's guard swallow the
            // OnGraphMutated that drives the cascade — a silent no-settle-after-drag regression. And
            // because pointerPress == pointerDrag (this component is both handlers on one GameObject),
            // eligibleForClick is never cleared, so this click DOES fire on every in-place release.
            // data.dragging is still true here; the input module clears it only after endDrag.
            if (data.dragging) return;
            if (!TryPointerToTile(data, out float tx, out float ty)) return;
            int id = HitRoomId(lvl, tx, ty);
            if (id == 0) { SelectRoom(0); return; }                    // background → clear selection

            // Double-click opens the room's battle map — the same shortcut a POI already has on the world
            // map. clickCount == 2 matches Notes' DoubleClickHandler, the project's existing convention.
            // Gated on !LinkMode on purpose: while «Связать» is armed both clicks are link picks, and
            // hijacking the second one would silently swallow half of a linking gesture. The first click
            // of the pair has already selected the room, so the inspector is on the right room either way.
            if (data.clickCount == 2 && !LinkMode)
            {
                SelectRoom(id);
                OnRoomDoubleClicked?.Invoke(id);
                return;
            }
            OnRoomActivated(id);
        }

        public void OnBeginDrag(PointerEventData data)
        {
            var lvl = BoundLevel;
            if (lvl == null) return;
            // Building UPPER floors are GENERATE-ONLY (spec stairwell stage B): rooms can't be dragged — the
            // floor is (re)generated around the column. Leaving draggingRoomId at 0 makes OnDrag a no-op.
            if (dungeon != null && dungeon.Kind == InteriorKind.Building && levelIndex > 0) return;
            if (!TryPointerToTile(data, out float tx, out float ty)) return;
            draggingRoomId = HitRoomId(lvl, tx, ty);
        }

        public void OnDrag(PointerEventData data)
        {
            if (draggingRoomId == 0) return;
            var lvl = BoundLevel;
            var room = lvl?.GetRoom(draggingRoomId);
            if (room == null) return;
            if (!TryPointerToTile(data, out float tx, out float ty)) return;

            room.X = Mathf.Clamp(tx / DungeonLayout.TilesPerAxis, DragClampMin, DragClampMax);
            room.Y = Mathf.Clamp(ty / DungeonLayout.TilesPerAxis, DragClampMin, DragClampMax);

            // BUILDING (spec C4): the room moves FREELY with the cursor — NO corridor leash — so the DM can
            // pull it right out of the contour and watch C2' flag it live (RepositionRooms re-tests the
            // contour every sample). On release (BeginCascade) the ONLY correction is anti-overlap on THIS
            // room; it otherwise stays exactly where dropped and no other room ever moves.
            // DUNGEON: stitched-together feel — a corridor may not stretch past MaxCorridorTiles, so dragging
            // this room drags its linked rooms along, and they drag theirs. Runs per drag sample (live),
            // unlike the cascade — the pull must be felt while moving, not on release. The dragged room is the
            // anchor and never yields.
            if (dungeon == null || dungeon.Kind != InteriorKind.Building)
                DungeonLayout.EnforceCorridorLeash(lvl, draggingRoomId);
            RepositionNow(lvl, RoomLinkGeometry.RoutingMode.Fast);
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (draggingRoomId == 0) return;
            // Clear on EVERY drag path, here and nowhere else. If the release lands outside this
            // hit-plate (over the toolbar, or a badge Button), no click fires at all — leaving the clear
            // to OnPointerClick would strand draggingRoomId set and swallow the next click.
            lastAnchorRoomId = draggingRoomId;
            draggingRoomId = 0;
            OnGraphMutated?.Invoke();
        }

        void OnRoomActivated(int id)
        {
            if (!LinkMode) { SelectRoom(id); return; }

            if (pendingLinkId == 0) { pendingLinkId = id; RefreshHighlights(); return; }

            int a = pendingLinkId;
            pendingLinkId = 0;
            if (a == id) { RefreshHighlights(); return; }   // same room twice — cancel silently, no dialog

            var lvl = BoundLevel;
            if (lvl == null) return;
            // authored: true — this IS the «Связать» action, the only path that hand-links two rooms
            // (see DungeonOps.HasAuthoredContent, which the regen/floor-delete confirm gates on).
            string reason = DungeonOps.AddCorridor(lvl, a, id, authored: true);
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

        void SelectRoom(int id)
        {
            SelectedRoomId = id;
            RefreshHighlights();
            OnRoomSelected?.Invoke(id);
        }

        void RefreshHighlights()
        {
            if (renderer == null) return;
            var lvl = BoundLevel;
            if (lvl == null) return;
            foreach (var r in lvl.Rooms)
                renderer.SetHighlight(r.Id, r.Id == SelectedRoomId || (LinkMode && r.Id == pendingLinkId));
        }
    }
}
