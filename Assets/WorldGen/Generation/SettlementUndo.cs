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
    /// Grid and Preview are carried BY REFERENCE, deliberately, not cloned: nothing in this codebase mutates
    /// them in place through a settlement floor's own rooms (DungeonInspectorPanel only ever assigns a new
    /// BattleGrid/byte[] wholesale — `room.Preview = shrunk` / `= null` — never edits one in place), so sharing
    /// the reference is exactly as safe as cloning and skips cloning a 512px PNG or a battle grid 64 layers
    /// deep, which is precisely the cost the snapshot-not-delta argument above exists to avoid. Portals, by
    /// contrast, IS cloned: elsewhere in the codebase (dungeon/building interiors) it genuinely is mutated in
    /// place (DungeonOps.AddSecretPassage/RemoveSecret, BuildingGenerator's stair wiring all call .Add/.Remove
    /// on the live list) — unreachable for a settlement floor's own rooms today, but Room is one shared type,
    /// and a List<Portal> is cheap enough that cloning it costs nothing, so it is cloned as a shallow copy
    /// rather than trusted to stay untouched.
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
            public Room[] Rooms;      // copies: value fields copied, Grid/Preview/Portals carried per-field (see class doc)
            public int NextRoomId;
            public int[] Streets;     // a copy of the encoded array, or null
        }

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
                    Portals = r.Portals == null ? null : new List<Portal>(r.Portals),
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
