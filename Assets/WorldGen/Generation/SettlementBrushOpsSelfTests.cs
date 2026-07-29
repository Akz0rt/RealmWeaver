using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Self-tests for <see cref="SettlementBrushOps"/> — what a brush STROKE means: interpolation
    /// over the lattice, and the two constructive ops (paint a building, paint a road). Lives in Rendering
    /// (the arc's convention for self-test files) even though the code under test is in Generation, so the
    /// harness — which compiles Generation and nothing else — can still run it.</summary>
    public class SettlementBrushOpsSelfTests : MonoBehaviour
    {
        // A settlement floor with one single-cell building per listed cell and the given streets.
        static InteriorFloor Floor(System.Collections.Generic.List<(int i, int j)> streets,
                                   params (int i, int j)[] buildings)
        {
            var f = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = true } };
            int id = 1;
            foreach (var (i, j) in buildings)
                f.Rooms.Add(new Room
                {
                    Id = id++, TypeId = 1,
                    X = SettlementFootprint.CenterOf(i), Y = SettlementFootprint.CenterOf(j),
                    Cells = SettlementFootprint.Encode(new System.Collections.Generic.List<(int i, int j)> { (i, j) })
                });
            f.NextRoomId = id;
            if (streets != null && streets.Count > 0)
                f.SettlementParams.StreetCells = SettlementFootprint.Encode(streets);
            return f;
        }

        static System.Collections.Generic.List<(int i, int j)> Cells(params (int i, int j)[] c)
            => new System.Collections.Generic.List<(int i, int j)>(c);

        /// <summary>The brush's constructive half: what a stroke's cells become.</summary>
        [ContextMenu("Self-Test: Brush Strokes")]
        public void SelfTestBrushStrokes()
        {
            bool ok = true;

            // 1. INTERPOLATION. A pointer sampled per frame skips cells; without interpolation a fast drag
            //    paints a dotted line. Two samples six cells apart must yield six appended cells, contiguous.
            {
                var cells = new System.Collections.Generic.List<(int i, int j)>();
                SettlementBrushOps.AppendSegment(cells, (0, 0), (0, 0));
                SettlementBrushOps.AppendSegment(cells, (0, 0), (6, 0));
                if (cells.Count != 7)
                {
                    Debug.LogError($"SelfTestBrushStrokes: a stroke from (0,0) to (6,0) produced {cells.Count} "
                                 + "cells, expected 7 — the segment is not interpolated");
                    ok = false;
                }
                for (int k = 1; k < cells.Count; k++)
                {
                    int d = System.Math.Abs(cells[k].i - cells[k - 1].i)
                          + System.Math.Abs(cells[k].j - cells[k - 1].j);
                    if (d != 1)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: cells {cells[k - 1]} and {cells[k]} are {d} "
                                     + "apart, expected 1 — the stroke has a gap");
                        ok = false;
                        break;
                    }
                }
            }

            // 2. A DIAGONAL stroke must also be 4-contiguous, or a painted building is not 4-connected and
            //    DungeonValidator reports it as an Error.
            {
                var cells = new System.Collections.Generic.List<(int i, int j)>();
                SettlementBrushOps.AppendSegment(cells, (0, 0), (0, 0));
                SettlementBrushOps.AppendSegment(cells, (0, 0), (4, 3));
                for (int k = 1; k < cells.Count; k++)
                {
                    int d = System.Math.Abs(cells[k].i - cells[k - 1].i)
                          + System.Math.Abs(cells[k].j - cells[k - 1].j);
                    if (d != 1)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: diagonal stroke stepped from {cells[k - 1]} to "
                                     + $"{cells[k]}, a distance of {d} — a diagonal step leaves a footprint "
                                     + "that is not 4-connected");
                        ok = false;
                        break;
                    }
                }
                if (!SettlementFootprint.IsConnected4(cells))
                {
                    Debug.LogError("SelfTestBrushStrokes: the diagonal stroke's cells are not 4-connected");
                    ok = false;
                }
            }

            // 3. A SELF-CROSSING STROKE CLAIMS EACH CELL ONCE — asserted on the FOOTPRINT, not on the stroke.
            //    AppendSegment produces a PATH: it is ordered, consecutive cells are 4-adjacent, and a
            //    re-crossed cell genuinely appears twice. Those two properties cannot both hold on one list —
            //    skipping a duplicate breaks the adjacency case 1 relies on to prove interpolation happened.
            //    So deduplication is the OPS' job and this is where it is pinned.
            {
                var cells = new System.Collections.Generic.List<(int i, int j)>();
                SettlementBrushOps.AppendSegment(cells, (0, 0), (0, 0));
                SettlementBrushOps.AppendSegment(cells, (0, 0), (3, 0));
                SettlementBrushOps.AppendSegment(cells, (3, 0), (0, 0));
                if (cells.Count != 7)
                {
                    Debug.LogError($"SelfTestBrushStrokes: an out-and-back stroke listed {cells.Count} cells, "
                                 + "expected 7 — AppendSegment must record the path, repeats included");
                    ok = false;
                }
                var floor3 = Floor(null);
                var room3 = SettlementBrushOps.PaintBuilding(floor3, cells);
                if (room3 == null) { Debug.LogError("SelfTestBrushStrokes: the out-and-back stroke painted nothing"); ok = false; }
                else
                {
                    var fp3 = SettlementTileGrid.FootprintOf(room3);
                    if (fp3.Count != 4)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: the out-and-back stroke's building has "
                                     + $"{fp3.Count} cells, expected 4 — a re-crossed cell was claimed twice");
                        ok = false;
                    }
                }
            }

            // 4. PAINT A BUILDING. The surviving cells become ONE room, 4-connected, with X/Y at the
            //    representative cell's centre — the convention every other producer writes.
            {
                var floor = Floor(null);
                var cells = Cells((2, 2), (3, 2), (4, 2));
                var room = SettlementBrushOps.PaintBuilding(floor, cells);
                if (room == null) { Debug.LogError("SelfTestBrushStrokes: PaintBuilding returned null on a free floor"); ok = false; }
                else
                {
                    var fp = SettlementTileGrid.FootprintOf(room);
                    if (fp.Count != 3)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: painted building has {fp.Count} cells, expected 3");
                        ok = false;
                    }
                    if (room.TypeId != 1)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: painted room has TypeId {room.TypeId}, expected 1");
                        ok = false;
                    }
                    var rep = SettlementFootprint.Representative(fp);
                    if (room.X != SettlementFootprint.CenterOf(rep.i) || room.Y != SettlementFootprint.CenterOf(rep.j))
                    {
                        Debug.LogError($"SelfTestBrushStrokes: painted room's point ({room.X},{room.Y}) is not "
                                     + $"the representative cell {rep}'s centre");
                        ok = false;
                    }
                }
            }

            // 5. THE PLACEMENT RULE IS OBEYED. The fixture is an L drawn AROUND an obstacle at (3,2): the
            //    stroke touches the occupied cell and then comes back down the row below it. Dropping (3,2)
            //    leaves five cells that are ALREADY one connected piece, chained through (2,1)-(3,1)-(4,1),
            //    BEFORE ComponentContainingFirst ever runs — so this fixture does NOT exercise the
            //    connectivity repair itself (a version that skipped it entirely would produce the identical
            //    5-cell result here; see case 6 for the fixture that actually severs the stroke). What THIS
            //    fixture separates is the placement rule from a second wrong shortcut:
            //      - "keep everything, ignore the rule"          -> 6 cells, and (3,2) present;
            //      - "stop the stroke at the first occupied cell" -> 1 cell;
            //      - correct: drop the occupied cell, keep the whole (already-connected) remainder -> 5 cells.
            //    A straight three-cell stroke could not tell the last two apart.
            {
                var floor = Floor(null, (3, 2));
                var cells = Cells((2, 2), (3, 2), (4, 2), (4, 1), (3, 1), (2, 1));
                var room = SettlementBrushOps.PaintBuilding(floor, cells);
                if (room == null) { Debug.LogError("SelfTestBrushStrokes: PaintBuilding returned null with five free cells"); ok = false; }
                else
                {
                    var fp = SettlementTileGrid.FootprintOf(room);
                    foreach (var c in fp)
                        if (c == (3, 2))
                        {
                            Debug.LogError("SelfTestBrushStrokes: the painted building claimed cell (3,2), "
                                         + "which another building already occupies");
                            ok = false;
                        }
                    if (fp.Count != 5)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: painted building has {fp.Count} cells, expected 5 "
                                     + "(the occupied cell dropped, the rest of the L kept)");
                        ok = false;
                    }
                    if (!SettlementFootprint.IsConnected4(fp))
                    {
                        Debug.LogError("SelfTestBrushStrokes: the painted building is not 4-connected after a "
                                     + "cell was dropped from the middle of the stroke");
                        ok = false;
                    }
                }
            }

            // 6. THE CONNECTIVITY REPAIR ACTUALLY FIRES. A building at (1,1) sits in the MIDDLE of a bent
            //    stroke (0,0)-(0,1)-(1,1)-(2,1)-(2,2), so dropping the occupied cell genuinely SEVERS it into
            //    two components: {(0,0),(0,1)} and {(2,1),(2,2)}. EVERY non-obstacle cell here is one of the
            //    obstacle's own 8 neighbours (Chebyshev distance 1), which the settlement's wall ring always
            //    leaves as plain Void — a straight stroke reaching Chebyshev distance 2 from a LONE building
            //    would instead run into the wall ring itself (BuildWallRing dilates by CourtyardCells + 1 = 2
            //    cells, and the ring sits exactly at that outer edge), which drops those cells for an
            //    unrelated reason and defeats the fixture; keeping every cell at distance 1 avoids that trap.
            //    Without the repair — the kept cells used as-is — the painted footprint would be all four
            //    remaining cells, split across the gap and NOT 4-connected. WITH the repair, only the
            //    component containing the first cell (0,0) — the piece the DM started drawing — survives:
            //    2 cells, connected, and never (2,1) or (2,2). Case 5's L cannot tell this apart from "no
            //    repair at all" (its remainder is already one piece); this fixture is the one that can.
            {
                var floor = Floor(null, (1, 1));
                var cells = Cells((0, 0), (0, 1), (1, 1), (2, 1), (2, 2));
                var room = SettlementBrushOps.PaintBuilding(floor, cells);
                if (room == null) { Debug.LogError("SelfTestBrushStrokes: PaintBuilding returned null with four free cells either side of a severing obstacle"); ok = false; }
                else
                {
                    var fp = SettlementTileGrid.FootprintOf(room);
                    foreach (var c in fp)
                        if (c == (2, 1) || c == (2, 2))
                        {
                            Debug.LogError($"SelfTestBrushStrokes: the painted building claimed cell {c}, on "
                                         + "the far side of the obstacle that severed the stroke — only the "
                                         + "component containing the first cell (0,0) should survive");
                            ok = false;
                        }
                    if (fp.Count != 2)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: painted building has {fp.Count} cells, expected 2 "
                                     + "(the obstacle at (1,1) severs the stroke; only {(0,0),(0,1)} — the piece "
                                     + "the DM started from — should survive)");
                        ok = false;
                    }
                    if (!SettlementFootprint.IsConnected4(fp))
                    {
                        Debug.LogError("SelfTestBrushStrokes: the painted building is not 4-connected — the "
                                     + "severed far side was not dropped");
                        ok = false;
                    }
                }
            }

            // 7. NOTHING PLACEABLE → null AND an untouched floor.
            {
                var floor = Floor(null, (3, 2));
                int before = floor.Rooms.Count;
                var room = SettlementBrushOps.PaintBuilding(floor, Cells((3, 2)));
                if (room != null) { Debug.LogError("SelfTestBrushStrokes: PaintBuilding placed a building on an occupied cell"); ok = false; }
                if (floor.Rooms.Count != before)
                {
                    Debug.LogError($"SelfTestBrushStrokes: a no-op PaintBuilding changed the room count from "
                                 + $"{before} to {floor.Rooms.Count}");
                    ok = false;
                }
            }

            // 8. PAINT A ROAD, and adding the same cell twice adds it once.
            {
                var floor = Floor(Cells((0, 0)));
                int added = SettlementBrushOps.PaintRoad(floor, Cells((1, 0), (2, 0), (2, 0)));
                if (added != 2)
                {
                    Debug.LogError($"SelfTestBrushStrokes: PaintRoad reported {added} new cells, expected 2");
                    ok = false;
                }
                var streets = new System.Collections.Generic.HashSet<(int i, int j)>(
                    SettlementFootprint.Decode(floor.SettlementParams.StreetCells));
                foreach (var c in Cells((0, 0), (1, 0), (2, 0)))
                    if (!streets.Contains(c))
                    {
                        Debug.LogError($"SelfTestBrushStrokes: street cell {c} is missing after PaintRoad");
                        ok = false;
                    }
                if (SettlementBrushOps.PaintRoad(floor, Cells((1, 0))) != 0)
                {
                    Debug.LogError("SelfTestBrushStrokes: PaintRoad counted an existing cell as new");
                    ok = false;
                }
            }

            if (ok) Debug.Log("Self-Test Brush Strokes: PASS");
        }

        /// <summary>Trailing sentinel — see the arc's trailing-sentinel rule. Asserts nothing.</summary>
        [ContextMenu("Self-Test: Brush Ops Sentinel")]
        public void SelfTestBrushOpsSentinel()
        {
            Debug.Log("Brush Ops Sentinel: no-op terminator (asserts nothing, not a test result)");
        }
    }
}
