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
            //    obstacle's own 8 neighbours (Chebyshev distance 1). Keeping every cell at distance 1 is now
            //    merely HISTORICAL, not load-bearing: under the OLD three-term placement rule a straight
            //    stroke reaching Chebyshev distance 2 from a lone building would have run into the wall ring
            //    itself (BuildWallRing dilates by CourtyardCells + 1 = 2 cells, and the ring sits exactly at
            //    that outer edge) and been dropped for an unrelated reason, defeating this fixture — but the
            //    ring is placeable now (checkpoint-1 amendment; see case 9), so a distance-2 cell would no
            //    longer be dropped at all and this fixture would simply need a different fourth obstacle to
            //    stay non-vacuous, not a different distance. Kept at distance 1 anyway, unchanged, since
            //    nothing requires moving it.
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

            // 9. DRAW THROUGH THE WALL — the town GROWS (DM requirement, checkpoint 1). The wall is DERIVED,
            //    so founding a building on a wall cell is legal: the ring re-derives one cell further out.
            //    A lone building at (0,0) rings itself at Chebyshev distance exactly 2 (BuildWallRing dilates
            //    the seed by CourtyardCells + 1 = 2 and walls the outer edge of that), so the stroke
            //    (1,0)-(2,0)-(3,0) runs Void -> WALL -> outside-the-ring. Under the OLD three-term rule (2,0)
            //    was dropped, the stroke severed, and ComponentContainingFirst kept the single cell (1,0).
            //    THE grid.At ASSERTION IS THE PRECONDITION, not decoration: without it this case would pass
            //    for the wrong reason the day the ring's radius or the margin changes, and would then be
            //    asserting nothing at all.
            {
                var floor = Floor(null, (0, 0));
                var g9 = SettlementTileGrid.Build(floor);
                if (g9.At(2, 0) != TileType.Wall)
                {
                    Debug.LogError($"SelfTestBrushStrokes: the fixture's cell (2,0) reads {g9.At(2, 0)}, "
                                 + "expected Wall — the wall-crossing case is not testing what it claims to");
                    ok = false;
                }
                var room = SettlementBrushOps.PaintBuilding(floor, Cells((1, 0), (2, 0), (3, 0)));
                if (room == null)
                {
                    Debug.LogError("SelfTestBrushStrokes: a stroke crossing the derived wall painted nothing");
                    ok = false;
                }
                else
                {
                    var fp = SettlementTileGrid.FootprintOf(room);
                    if (fp.Count != 3)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: a stroke crossing the wall produced {fp.Count} "
                                     + "cells, expected 3 — the wall is derived, so a stroke may cross it and "
                                     + "the town simply grows");
                        ok = false;
                    }
                    bool crossed = false, outside = false;
                    foreach (var c in fp) { if (c == (2, 0)) crossed = true; if (c == (3, 0)) outside = true; }
                    if (!crossed)
                    {
                        Debug.LogError("SelfTestBrushStrokes: the painted building does not include the wall "
                                     + "cell (2,0) — the stroke was cut at the wall");
                        ok = false;
                    }
                    if (!outside)
                    {
                        Debug.LogError("SelfTestBrushStrokes: the painted building does not include (3,0), "
                                     + "outside the old ring — the town did not expand");
                        ok = false;
                    }
                }
            }

            // 10. OFF-FIELD CELLS ARE REFUSED, on BOTH sides of the field. Room positions live in normalized
            //     0..1 and the cascade Clamp01s every room every animation frame, so a building stored past
            //     the edge is pinned there on every later drag. Cell i = 33 has centre (33 + 0.5) * 0.03 =
            //     1.005, just past the field; i = 32 has 0.975, just inside; i = -1 has -0.015, just past the
            //     field on the NEGATIVE side — the direction the deleted renderer doc explicitly called out as
            //     the one actually reachable in production (a large city's margin band reaches negative
            //     normalized X). The panel really does draw such cells (Allocate pads MarginCells = 3 past the
            //     outermost building), and requirement 2 above aims the DM outward.
            //     THE OFF-FIELD CELL IS LISTED FIRST, deliberately, and deliberately NOT 4-adjacent to (32,0):
            //     ComponentContainingFirst keeps the component containing kept[0], the FIRST cell that
            //     SURVIVES the placement filter — not the first cell of the argument list. If a broken
            //     lower-bound check ever let (-1,0) survive, it would become kept[0] (nothing else in this
            //     stroke is within reach of it), and the WHOLE painted footprint would collapse onto that one
            //     isolated off-field cell instead of (32,0) — a stronger, more specific signal than a bare
            //     cell-count check, which a same-size wrong answer (1 cell either way) could still satisfy.
            {
                var floor = Floor(null);
                var room = SettlementBrushOps.PaintBuilding(floor, Cells((-1, 0), (32, 0), (33, 0), (34, 0)));
                if (room == null)
                {
                    Debug.LogError("SelfTestBrushStrokes: a stroke ending off-field painted nothing at all — "
                                 + "its on-field cell (32,0) should still have been kept");
                    ok = false;
                }
                else
                {
                    var fp = SettlementTileGrid.FootprintOf(room);
                    foreach (var c in fp)
                        if (SettlementFootprint.CenterOf(c.i) > 1f || SettlementFootprint.CenterOf(c.j) > 1f
                         || SettlementFootprint.CenterOf(c.i) < 0f || SettlementFootprint.CenterOf(c.j) < 0f)
                        {
                            Debug.LogError($"SelfTestBrushStrokes: the painted building claimed cell {c}, "
                                         + $"whose centre ({SettlementFootprint.CenterOf(c.i)}, "
                                         + $"{SettlementFootprint.CenterOf(c.j)}) lies off the 0..1 field");
                            ok = false;
                        }
                    if (fp.Count != 1)
                    {
                        Debug.LogError($"SelfTestBrushStrokes: the off-field stroke produced {fp.Count} cells, "
                                     + "expected 1 — only (32,0) is on the field");
                        ok = false;
                    }
                    bool has32 = false;
                    foreach (var c in fp) if (c == (32, 0)) has32 = true;
                    if (!has32)
                    {
                        Debug.LogError("SelfTestBrushStrokes: the painted building does not include (32,0), "
                                     + "the stroke's only genuinely on-field cell — a broken lower-bound check "
                                     + "let the off-field cell (-1,0) survive placement, and the connectivity "
                                     + "repair then collapsed the whole footprint onto that isolated cell "
                                     + "instead");
                        ok = false;
                    }
                }
            }

            // 11. ROOM OWNERSHIP: A STROKE MAY NOT CLAIM A NON-BUILDING ROOM'S OWN CELL — in practice a gate's
            //     (checkpoint-1 amendment, closing the hole narrowing IsPlaceable opened). A PREVIOUSLY-DRAGGED
            //     gate's own stored cell is normalized onto the wall/gate cell itself (SettlementTileGrid.GateRoomAt's own
            //     doc), so it reads Wall — or, once MarkGates retargets it, Gate — rather than the Road a
            //     freshly generated gate's own cell would read. Narrowing IsPlaceable to "refuses only
            //     Building" (case 9) reopened that cell: without a room-ownership rule a stroke could found a
            //     Building there, and Precedes makes a founded Building strictly beat a Gate for a shared cell,
            //     permanently hiding the gate from both drawing and clicking.
            //     Reuses case 9's exact shape — a lone building at (0,0) rings itself at Chebyshev distance 2,
            //     so (2,0) is the ring cell — plus a gate room (TypeId 0) whose own (X,Y) sits at (2,0)'s
            //     centre, simulating the previously-dragged normalization. Built INLINE, not through the shared
            //     `Floor` helper above, which only ever stamps TypeId==1 (Building) rooms.
            //     THE g11.At ASSERTION IS THE PRECONDITION, exactly like case 9's: without it this case would
            //     pass for the wrong reason if the ring's radius, MarkGates, or this fixture ever changed.
            {
                var floor11 = new InteriorFloor { SettlementParams = new SettlementParams { HasWall = true } };
                floor11.Rooms.Add(new Room
                {
                    Id = 1, TypeId = 1,
                    X = SettlementFootprint.CenterOf(0), Y = SettlementFootprint.CenterOf(0),
                    Cells = SettlementFootprint.Encode(Cells((0, 0)))
                });
                floor11.Rooms.Add(new Room
                {
                    Id = 2, TypeId = 0,
                    X = SettlementFootprint.CenterOf(2), Y = SettlementFootprint.CenterOf(0),
                });
                floor11.NextRoomId = 3;

                var g11 = SettlementTileGrid.Build(floor11);
                if (g11.At(2, 0) != TileType.Gate)
                {
                    Debug.LogError($"SelfTestBrushStrokes: the fixture's gate cell (2,0) reads {g11.At(2, 0)}, "
                                 + "expected Gate — the previously-dragged-gate case is not testing what it "
                                 + "claims to");
                    ok = false;
                }
                var room11 = SettlementBrushOps.PaintBuilding(floor11, Cells((1, 0), (2, 0), (3, 0)));
                if (room11 != null)
                {
                    var fp11 = SettlementTileGrid.FootprintOf(room11);
                    foreach (var c in fp11)
                        if (c == (2, 0))
                        {
                            Debug.LogError("SelfTestBrushStrokes: the painted building claimed cell (2,0), "
                                         + "the gate room's own cell — the gate would become permanently "
                                         + "unselectable and undraggable");
                            ok = false;
                        }
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
