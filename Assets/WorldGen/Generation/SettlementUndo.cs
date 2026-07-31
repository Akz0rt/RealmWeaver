using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>One snapshot per brush stroke, modelled on BattleGridUndo down to its MaxDepth — and on
    /// BrushToolController's one-press-one-undo idiom on the world map.
    ///
    /// SNAPSHOTS, NOT DELTAS, and that is a deliberate simplification of the precedent. BattleGridUndo carries
    /// a delta/snapshot hybrid because a battle grid is large enough for per-stroke snapshots to be wasteful.
    /// A town is not: ~120 buildings and a few hundred street cells, so a snapshot is a few kilobytes and 64
    /// of them are noise. Deltas would buy nothing here and would have to stay correct across three different
    /// op shapes, which is exactly where a subtle bug would hide.
    ///
    /// WHAT IS CAPTURED IS EVERY FIELD OF EVERY ROOM, not just the ones a brush writes — TryUndo replaces the
    /// WHOLE Rooms list (it has to: a stroke can both add and remove rooms), so every field of every room is
    /// in scope for every undo, including rooms the stroke being undone never touched. Id/TypeId/Title/Body/
    /// X/Y/SizeW/SizeH/IsDummy/Cells, plus Grid/Preview/Portals (treated as described below) and the floor's
    /// NextRoomId and SettlementParams.StreetCells. Title/Body/Preview are authored through the building
    /// inspector (DungeonInspectorPanel), never by a brush — but a snapshot that omitted them would still
    /// erase them from every OTHER room on the floor the moment any one stroke gets undone, which is a
    /// strictly worse failure than no undo at all: the DM has no reason to suspect an unrelated action wiped a
    /// building's name, description or photo.
    ///
    /// Grid and Preview are carried BY REFERENCE, deliberately, not cloned: re-confirmed (not just inherited
    /// from an earlier pass) that nothing in this codebase mutates either one in place through a settlement
    /// floor's own rooms. BattleGridScreen only ever assigns `room.Grid` a freshly-built model wholesale
    /// (`room.Grid = view.Buffer.ToModel()` — the live, in-place-editable state lives in a SEPARATE
    /// GridBuffer, never in room.Grid itself), and DungeonInspectorPanel only ever assigns `room.Preview` a
    /// new byte[] wholesale (`= shrunk` / `= null`). So sharing the reference is exactly as safe as cloning,
    /// and skips cloning a 512px PNG or a battle grid 64 layers deep — precisely the cost the
    /// snapshot-not-delta argument above exists to avoid.
    ///
    /// Portals is DEEP-cloned — every Portal object copied field-by-field into a new instance, not just the
    /// LIST shallow-copied — and this is the fix for a real defect a task review caught: Portal is a mutable
    /// class, and DungeonInspectorPanel.BuildSecretRow edits an EXISTING portal's fields IN PLACE (Kind/
    /// Hidden/TargetFloorIndex/TargetRoomId/Bidirectional/Label), on a path that is NOT gated off for a
    /// settlement building or a gate (only a dummy building skips it). A shallow list-copy shares the Portal
    /// OBJECTS, so an in-place edit made after PushSnapshot would still be visible through the snapshot's own
    /// copy — undo would restore whatever the portal reads NOW, not what it read at push time. Confirmed
    /// load-bearing (SelfTestSettlementUndo case 5) against exactly the shallow-copy version before this fix.
    ///
    /// Cells is cloned because the ops DO produce a fresh array per paint (SettlementFootprint.Encode), so a
    /// clone is cheap insurance against a future op that writes into an existing one in place instead.
    ///
    /// Deliberately NOT the wall, the gates or the roads as TILE TYPES — those are DERIVED and come back on
    /// the next SettlementTileGrid.Build.</summary>
    public class SettlementUndo
    {
        public const int MaxDepth = 64;

        class Entry
        {
            public Room[] Rooms;      // copies: value fields copied, Grid/Preview by reference, Portals deep-cloned (see class doc)
            public int NextRoomId;
            public int[] Streets;     // a copy of the encoded array, or null
        }

        // A deep copy of one portal — every field Portal actually declares. Not a reference: BuildSecretRow
        // edits an existing Portal's fields in place (see class doc), so a shared Portal object would let a
        // later in-place edit leak back into an already-pushed snapshot.
        static Portal ClonePortal(Portal p) => new Portal
        {
            Kind = p.Kind, Hidden = p.Hidden, TargetFloorIndex = p.TargetFloorIndex,
            TargetRoomId = p.TargetRoomId, Bidirectional = p.Bidirectional, Label = p.Label,
        };

        // The per-room half of CloneFloor, factored out only because CloneFloor loops over it — same
        // field list PushSnapshot always copied (see the class doc for why every field is in scope, not
        // just the ones a brush writes), same by-value/by-reference/deep split: Cells cloned (a fresh array
        // per paint anyway), Grid/Preview by reference (nothing mutates either in place through a room),
        // Portals deep-cloned through ClonePortal (BuildSecretRow's in-place edits, see the class doc).
        static Room CloneRoom(Room r) => new Room
        {
            Id = r.Id, TypeId = r.TypeId, Title = r.Title, Body = r.Body,
            X = r.X, Y = r.Y, SizeW = r.SizeW, SizeH = r.SizeH, IsDummy = r.IsDummy,
            Cells = r.Cells == null ? null : (int[])r.Cells.Clone(),
            Grid = r.Grid, Preview = r.Preview,
            Portals = r.Portals == null ? null : r.Portals.ConvertAll(ClonePortal),
        };

        /// <summary>The ONE whole-floor deep clone (Task 8, Step 3) — what PushSnapshot below always built by
        /// hand, now exposed so a SECOND caller (DungeonViewController's eraser scratch floor) can get the
        /// identical guarantee instead of a fresh, independently-written clone. THAT is the risk this method
        /// closes: a second hand-rolled clone that forgot Portals — or, just as fatal for the eraser
        /// specifically, forgot to give the clone its OWN SettlementParams/StreetCells array — would resurrect
        /// exactly the bug Task 2's review caught with a ReferenceEquals repro, except this time corrupting the
        /// LIVE floor the moment the scratch's own Erase call rewrote StreetCells through a SHARED
        /// SettlementParams reference.
        ///
        /// Rooms and SettlementParams (including its StreetCells array) are fully independent copies — see
        /// CloneRoom and ClonePortal for the per-room split. Links is a FRESH LIST sharing the existing Link
        /// objects (the same by-reference treatment Grid/Preview already get): nothing in this codebase edits
        /// a Link's RoomA/RoomB in place after construction (DungeonOps only ever adds or removes whole Link
        /// objects), so cloning the list but not the objects is exactly as safe as cloning nothing at all, and
        /// cheaper. Null-safe: a null `source` clones to null, matching every other producer in this arc.</summary>
        public static InteriorFloor CloneFloor(InteriorFloor source)
        {
            if (source == null) return null;
            var rooms = new List<Room>(source.Rooms.Count);
            foreach (var r in source.Rooms) rooms.Add(CloneRoom(r));
            var clone = new InteriorFloor
            {
                Rooms = rooms,
                Links = new List<Link>(source.Links),
                NextRoomId = source.NextRoomId,
            };
            if (source.SettlementParams != null)
            {
                clone.SettlementParams = new SettlementParams
                {
                    Size = source.SettlementParams.Size,
                    LegacyTargetBuildings = source.SettlementParams.LegacyTargetBuildings,
                    ActiveBuildings = source.SettlementParams.ActiveBuildings,
                    HasWall = source.SettlementParams.HasWall,
                    StreetCells = source.SettlementParams.StreetCells == null
                        ? null : (int[])source.SettlementParams.StreetCells.Clone(),
                };
            }
            return clone;
        }

        readonly List<Entry> stack = new List<Entry>();

        public int Count => stack.Count;

        public void Clear() => stack.Clear();

        public void PushSnapshot(InteriorFloor floor)
        {
            if (floor?.SettlementParams == null) return;
            var clone = CloneFloor(floor);
            stack.Add(new Entry
            {
                Rooms = clone.Rooms.ToArray(),
                NextRoomId = clone.NextRoomId,
                Streets = clone.SettlementParams.StreetCells,
            });
            // Drop the OLDEST when the cap is reached — the DM keeps the most recent MaxDepth strokes.
            if (stack.Count > MaxDepth) stack.RemoveAt(0);
        }

        public bool TryUndo(InteriorFloor floor)
        {
            if (floor?.SettlementParams == null || stack.Count == 0) return false;
            var e = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            floor.Rooms.Clear();
            foreach (var r in e.Rooms) floor.Rooms.Add(r);
            floor.NextRoomId = e.NextRoomId;
            floor.SettlementParams.StreetCells = e.Streets;
            return true;
        }

        /// <summary>Drop the newest snapshot WITHOUT applying it — for a gesture that pushed one and then
        /// turned out to change nothing (a dab on an occupied cell). Never TryUndo for this: TryUndo
        /// RESTORES, so a paint that silently failed would be indistinguishable from one that never
        /// happened, and the stack would look right while hiding a real defect. Empty stack: no-op.</summary>
        public void Discard()
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }
    }
}
