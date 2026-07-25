using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    public class SettlementTileGridSelfTests : MonoBehaviour
    {
        // Build a settlement floor: buildings (TypeId=1) at lattice points, optional gate (TypeId=0).
        static InteriorFloor Floor(bool hasWall, params (int i, int j)[] cells)
        {
            float c = SettlementGenerator.BuildingCell, ax = 0.3f, ay = 0.3f;
            var f = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = hasWall } };
            int id = 1;
            foreach (var (i, j) in cells)
                f.Rooms.Add(new Room { Id = id++, TypeId = 1, X = ax + i * c, Y = ay + j * c });
            return f;
        }

        [ContextMenu("Self-Test: Tile Mapping")]
        public void SelfTestTileMapping()
        {
            bool ok = true;
            float c = SettlementGenerator.BuildingCell, ax = 0.3f, ay = 0.3f;
            var f = Floor(false, (0,0), (2,0), (0,3));
            var g = SettlementTileGrid.Allocate(f.Rooms);

            // each building maps to its lattice cell, centers round-trip
            foreach (var r in f.Rooms)
            {
                int i = g.CellI(r.X), j = g.CellJ(r.Y);
                if (System.Math.Abs(g.CenterX(i) - r.X) > 1e-4f || System.Math.Abs(g.CenterY(j) - r.Y) > 1e-4f)
                { Debug.LogError($"FAIL tilemap: room {r.Id} at ({r.X},{r.Y}) does not round-trip to cell ({i},{j}) center ({g.CenterX(i)},{g.CenterY(j)})"); ok = false; }
            }
            // anchor is the min building corner → its cell is 0
            if (g.CellI(ax) != 0 || g.CellJ(ay) != 0)
            { Debug.LogError($"FAIL tilemap: anchor building not at cell 0 (got {g.CellI(ax)},{g.CellJ(ay)})"); ok = false; }
            // extent covers bbox (i 0..2, j 0..3) plus MarginCells on each side
            int expW = (2 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            int expH = (3 - 0 + 1) + 2 * SettlementTileGrid.MarginCells;
            if (g.W != expW || g.H != expH)
            { Debug.LogError($"FAIL tilemap: extent {g.W}x{g.H}, expected {expW}x{expH}"); ok = false; }
            if (g.OriginI != -SettlementTileGrid.MarginCells || g.OriginJ != -SettlementTileGrid.MarginCells)
            { Debug.LogError($"FAIL tilemap: origin ({g.OriginI},{g.OriginJ}) not (-margin,-margin)"); ok = false; }
            // snap picks the NEAREST cell centre, not floor/ceiling: an offset of 0.3*Cell above a lattice
            // point must snap DOWN to that point's centre; an offset of 0.7*Cell must snap UP to the next
            // cell's centre. Floor gets the 0.7 case wrong (stays at the lower centre); Ceiling gets the 0.3
            // case wrong (jumps to the upper centre) — so together these pin Round specifically. (The old
            // idempotency check — SnapX(SnapX(x)) — held for Floor/Ceiling/Round alike, since the second call
            // always receives an exact lattice value; it could not tell them apart.)
            float snapDownX = g.SnapX(ax + 0.3f * c);
            if (System.Math.Abs(snapDownX - ax) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapX(ax+0.3*Cell) = {snapDownX}, want {ax} (snap DOWN to the lattice point)"); ok = false; }
            float snapUpX = g.SnapX(ax + 0.7f * c);
            float expSnapUpX = ax + c;
            if (System.Math.Abs(snapUpX - expSnapUpX) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapX(ax+0.7*Cell) = {snapUpX}, want {expSnapUpX} (snap UP to the next cell)"); ok = false; }
            float snapDownY = g.SnapY(ay + 0.3f * c);
            if (System.Math.Abs(snapDownY - ay) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapY(ay+0.3*Cell) = {snapDownY}, want {ay} (snap DOWN to the lattice point)"); ok = false; }
            float snapUpY = g.SnapY(ay + 0.7f * c);
            float expSnapUpY = ay + c;
            if (System.Math.Abs(snapUpY - expSnapUpY) > 1e-4f)
            { Debug.LogError($"FAIL tilemap: SnapY(ay+0.7*Cell) = {snapUpY}, want {expSnapUpY} (snap UP to the next cell)"); ok = false; }

            if (ok) Debug.Log("Settlement TileGrid Mapping: PASS");
        }

        [ContextMenu("Self-Test: TileGrid Sanity")]
        public void SelfTestTileGridSanity()
        {
            // Trailing non-reboundable sentinel: a plain smoke check so mutant-reboundable tests are never last.
            bool ok = true;
            var g = SettlementTileGrid.Allocate(new System.Collections.Generic.List<Room>());
            if (g == null)
            { Debug.LogError("FAIL tilegrid-sanity: empty Allocate returned null"); ok = false; }
            else if (g.W != 1 || g.H != 1)
            { Debug.LogError($"FAIL tilegrid-sanity: empty Allocate yielded {g.W}x{g.H}, want 1x1"); ok = false; }

            if (ok) Debug.Log("Settlement TileGrid Sanity: PASS");
        }
    }
}
