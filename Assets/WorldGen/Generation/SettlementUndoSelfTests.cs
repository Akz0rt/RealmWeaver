using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>Self-tests for <see cref="SettlementUndo"/>. Lives in Rendering (the arc's self-test
    /// convention) even though the code under test is in Generation, so the harness — which compiles
    /// Generation and nothing else — can still run it. Floor/Cells are the SAME helpers
    /// SettlementBrushOpsSelfTests defines, spelled out again on purpose: a shared helper across test files
    /// is not rebound by the mutant machinery, so each test file that needs one carries its own copy.</summary>
    public class SettlementUndoSelfTests : MonoBehaviour
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

        [ContextMenu("Self-Test: Settlement Undo")]
        public void SelfTestSettlementUndo()
        {
            bool ok = true;

            // 1. A stroke, then an undo, restores rooms AND streets exactly.
            {
                var floor = Floor(Cells((0, 0)), (5, 5));
                var undo = new SettlementUndo();
                undo.PushSnapshot(floor);
                SettlementBrushOps.PaintBuilding(floor, Cells((2, 2), (3, 2)));
                SettlementBrushOps.PaintRoad(floor, Cells((1, 0), (2, 0)));
                if (floor.Rooms.Count != 2)
                {
                    Debug.LogError($"SelfTestSettlementUndo: the fixture did not change — {floor.Rooms.Count} "
                                 + "rooms after painting, expected 2. The undo below would prove nothing.");
                    ok = false;
                }
                if (!undo.TryUndo(floor))
                {
                    Debug.LogError("SelfTestSettlementUndo: TryUndo returned false with one snapshot pushed");
                    ok = false;
                }
                if (floor.Rooms.Count != 1)
                {
                    Debug.LogError($"SelfTestSettlementUndo: after undo the floor has {floor.Rooms.Count} rooms, "
                                 + "expected 1");
                    ok = false;
                }
                var streets = SettlementFootprint.Decode(floor.SettlementParams.StreetCells);
                if (streets.Count != 1 || streets[0] != (0, 0))
                {
                    Debug.LogError($"SelfTestSettlementUndo: after undo the streets are [{string.Join(" ", streets)}], "
                                 + "expected exactly (0, 0)");
                    ok = false;
                }
            }

            // 2. An empty stack returns false and changes nothing.
            {
                var floor = Floor(Cells((0, 0)), (5, 5));
                var undo = new SettlementUndo();
                if (undo.TryUndo(floor)) { Debug.LogError("SelfTestSettlementUndo: TryUndo succeeded on an empty stack"); ok = false; }
                if (floor.Rooms.Count != 1) { Debug.LogError("SelfTestSettlementUndo: an empty undo changed the floor"); ok = false; }
            }

            // 3. DEPTH IS CAPPED, and the OLDEST entry is the one dropped. Push MaxDepth + 5 snapshots, each
            //    after adding one more building, then undo MaxDepth times: the floor must land on the state
            //    that was current at snapshot 6, not at snapshot 1.
            {
                var floor = Floor(null);
                var undo = new SettlementUndo();
                for (int k = 0; k < SettlementUndo.MaxDepth + 5; k++)
                {
                    undo.PushSnapshot(floor);
                    SettlementBrushOps.PaintBuilding(floor, Cells((k, 0)));
                }
                if (undo.Count != SettlementUndo.MaxDepth)
                {
                    Debug.LogError($"SelfTestSettlementUndo: the stack holds {undo.Count} entries, expected "
                                 + $"{SettlementUndo.MaxDepth}");
                    ok = false;
                }
                while (undo.TryUndo(floor)) { }
                if (floor.Rooms.Count != 5)
                {
                    Debug.LogError($"SelfTestSettlementUndo: undoing the whole stack left {floor.Rooms.Count} "
                                 + "rooms, expected 5 — the five oldest snapshots should have been dropped, "
                                 + "not the newest");
                    ok = false;
                }
            }

            // 4. A STROKE ELSEWHERE MUST NOT ERASE A BUILDING'S AUTHORED CONTENT. TryUndo replaces the WHOLE
            //    Rooms list (see case 1), so every field of every room is in scope for every undo — not just
            //    the fields the brush that particular stroke changed. Title/Body/Preview are written by the
            //    DM through the building inspector (DungeonInspectorPanel), never by a brush stroke itself; a
            //    snapshot that silently drops them would erase an UNTOUCHED building's name, description and
            //    photo the moment ANY unrelated stroke on the same floor gets undone — a failure mode strictly
            //    worse than having no undo at all, because the DM has no reason to suspect it.
            {
                var floor = Floor(Cells((0, 0)), (5, 5));
                var authored = floor.GetRoom(1);
                authored.Title = "Кузница";
                authored.Body = "Здесь куёт кузнец";
                authored.Preview = new byte[] { 1, 2, 3 };
                var undo = new SettlementUndo();
                undo.PushSnapshot(floor);
                SettlementBrushOps.PaintBuilding(floor, Cells((2, 2), (3, 2)));
                if (!undo.TryUndo(floor))
                {
                    Debug.LogError("SelfTestSettlementUndo: TryUndo returned false with one snapshot pushed (case 4)");
                    ok = false;
                }
                var restored = floor.GetRoom(1);
                if (restored == null)
                {
                    Debug.LogError("SelfTestSettlementUndo: after undo room 1 (the pre-existing, untouched "
                                 + "building) is gone entirely");
                    ok = false;
                }
                else
                {
                    if (restored.Title != "Кузница")
                    {
                        Debug.LogError($"SelfTestSettlementUndo: after undo the untouched building's Title is "
                                     + $"\"{restored.Title}\", expected \"Кузница\" — undo erased authored "
                                     + "content a brush never touched");
                        ok = false;
                    }
                    if (restored.Body != "Здесь куёт кузнец")
                    {
                        Debug.LogError($"SelfTestSettlementUndo: after undo the untouched building's Body is "
                                     + $"\"{restored.Body}\", expected \"Здесь куёт кузнец\"");
                        ok = false;
                    }
                    if (restored.Preview == null || restored.Preview.Length != 3
                        || restored.Preview[0] != 1 || restored.Preview[1] != 2 || restored.Preview[2] != 3)
                    {
                        string got = restored.Preview == null ? "null" : string.Join(",", restored.Preview);
                        Debug.LogError($"SelfTestSettlementUndo: after undo the untouched building's Preview "
                                     + $"is [{got}], expected [1,2,3]");
                        ok = false;
                    }
                }
            }

            if (ok) Debug.Log("Self-Test Settlement Undo: PASS");
        }

        /// <summary>Trailing sentinel. Asserts nothing.</summary>
        [ContextMenu("Self-Test: Settlement Undo Sentinel")]
        public void SelfTestSettlementUndoSentinel()
        {
            Debug.Log("Settlement Undo Sentinel: no-op terminator (asserts nothing, not a test result)");
        }
    }
}
