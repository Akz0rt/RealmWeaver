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

        readonly List<Entry> stack = new List<Entry>();

        public int Count => stack.Count;

        public void Clear() => stack.Clear();

        public void PushSnapshot(InteriorFloor floor)
        {
            if (floor?.SettlementParams == null) return;
            var rooms = new Room[floor.Rooms.Count];
            for (int k = 0; k < rooms.Length; k++)
            {
                var r = floor.Rooms[k];
                rooms[k] = new Room
                {
                    Id = r.Id, TypeId = r.TypeId, Title = r.Title, Body = r.Body,
                    X = r.X, Y = r.Y, SizeW = r.SizeW, SizeH = r.SizeH, IsDummy = r.IsDummy,
                    Cells = r.Cells == null ? null : (int[])r.Cells.Clone(),
                    Grid = r.Grid, Preview = r.Preview,
                    Portals = r.Portals == null ? null : r.Portals.ConvertAll(ClonePortal),
                };
            }
            var streets = floor.SettlementParams.StreetCells;
            stack.Add(new Entry
            {
                Rooms = rooms,
                NextRoomId = floor.NextRoomId,
                Streets = streets == null ? null : (int[])streets.Clone(),
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
    }
}
