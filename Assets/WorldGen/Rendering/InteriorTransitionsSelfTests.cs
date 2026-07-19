using System.Collections.Generic;
using UnityEngine;
using WorldGen.Generation;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-tests for InteriorTransitions — add to any GameObject, run from the
    /// Inspector. Every assertion targets a transition a specific rule produces, so removing that rule flips
    /// the test to FAIL (non-vacuous — the project's #1 past failure was tests that pass either way).</summary>
    public class InteriorTransitionsSelfTests : MonoBehaviour
    {
        const string Up = "⬆";
        const string Down = "⬇";

        [ContextMenu("Self-Test: Interior Transitions")]
        public void SelfTestInteriorTransitions()
        {
            bool ok = true;

            // ── BUILDING (ExplicitStairs) ────────────────────────────────────────────────────────────
            // 3 floors. Floor 0: entrance (id1) + stair room (id2) with a Stairs portal UP to floor 1 room 1.
            // Floor 1: room 1 is the TARGET of floor-0's stair AND has its own Stairs portal UP to floor 2 —
            // a middle floor, so it must show BOTH ⬆ (own) and ⬇ (derived). Room 2 is TypeId 2 ("Приватная",
            // NOT a boss). Floor 2: room 1 is the target of floor-1's stair (down endpoint only).
            var b = new InteriorData { Kind = InteriorKind.Building };
            var bf0 = new InteriorFloor();
            bf0.Rooms.Add(new Room { Id = 1, TypeId = 0 });
            var stair0 = new Room { Id = 2, TypeId = 1 };
            stair0.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 1, TargetRoomId = 1 });
            bf0.Rooms.Add(stair0);
            b.Floors.Add(bf0);

            var bf1 = new InteriorFloor();
            var mid = new Room { Id = 1, TypeId = 1 };
            mid.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 2, TargetRoomId = 1 });
            bf1.Rooms.Add(mid);
            bf1.Rooms.Add(new Room { Id = 2, TypeId = 2 });   // "Приватная" — must NOT descend like a dungeon boss
            b.Floors.Add(bf1);

            var bf2 = new InteriorFloor();
            bf2.Rooms.Add(new Room { Id = 1, TypeId = 1 });
            b.Floors.Add(bf2);

            // 1. Middle stair room: BOTH up (own portal → Этаж 3) and down (DERIVED from floor 0 → Этаж 1).
            var midT = InteriorTransitions.For(b, FloorLinkMode.ExplicitStairs, 1, mid);
            if (!Has(midT, Up, "Этаж 3")) { Debug.LogError("FAIL building: middle floor missing up ⬆ Этаж 3"); ok = false; }
            if (!Has(midT, Down, "Этаж 1")) { Debug.LogError("FAIL building: middle floor missing DERIVED down ⬇ Этаж 1"); ok = false; }
            if (midT.Count != 2) { Debug.LogError($"FAIL building: middle floor has {midT.Count} transitions, want 2"); ok = false; }

            // 2. Top stair room: only the DERIVED down (⬇ Этаж 2); no up (no portal above).
            var topT = InteriorTransitions.For(b, FloorLinkMode.ExplicitStairs, 2, bf2.Rooms[0]);
            if (!Has(topT, Down, "Этаж 2") || topT.Count != 1)
            { Debug.LogError($"FAIL building: top floor want only ⬇ Этаж 2, got {topT.Count}"); ok = false; }

            // 3. Floor-0 entrance: the exit to the outside (informational — no jump).
            var entT = InteriorTransitions.For(b, FloorLinkMode.ExplicitStairs, 0, bf0.Rooms[0]);
            if (!Has(entT, Up, "Выход") || entT.Count != 1)
            { Debug.LogError($"FAIL building: floor-0 entrance want ⬆ Выход, got {entT.Count}"); ok = false; }
            var exit = Find(entT, Up, "Выход");
            if (exit.Clickable || exit.TargetFloorIndex != -1)
            { Debug.LogError("FAIL building: Выход must be informational (not clickable, target -1)"); ok = false; }

            // 4. TypeId-2 room on a non-top floor: NO transitions (building does not boss-descend). Non-vacuous:
            //    under the dungeon rule this room would emit ⬇ Этаж 3.
            var privT = InteriorTransitions.For(b, FloorLinkMode.ExplicitStairs, 1, bf1.Rooms[1]);
            if (privT.Count != 0)
            { Debug.LogError($"FAIL building: TypeId-2 room emitted {privT.Count} transitions (boss-descent not gated out)"); ok = false; }

            // ── DUNGEON (ImplicitDescent) — behaviour-preserving ─────────────────────────────────────
            var d = new InteriorData { Kind = InteriorKind.Dungeon };
            var df0 = new InteriorFloor();
            df0.Rooms.Add(new Room { Id = 1, TypeId = 0 });   // entrance
            df0.Rooms.Add(new Room { Id = 2, TypeId = 2 });   // boss
            d.Floors.Add(df0);
            var df1 = new InteriorFloor();
            df1.Rooms.Add(new Room { Id = 1, TypeId = 0 });
            d.Floors.Add(df1);

            // 5. Boss descends to the next level (clickable jump to floor index 1).
            var bossT = InteriorTransitions.For(d, FloorLinkMode.ImplicitDescent, 0, df0.Rooms[1]);
            var boss = Find(bossT, Down, "Этаж 2");
            if (bossT.Count != 1 || !boss.Clickable || boss.TargetFloorIndex != 1)
            { Debug.LogError("FAIL dungeon: boss must descend to Этаж 2 (clickable, target 1)"); ok = false; }

            // 6. Entrance on floor 0 exits (informational) — unchanged dungeon behaviour.
            var dEntT = InteriorTransitions.For(d, FloorLinkMode.ImplicitDescent, 0, df0.Rooms[0]);
            if (!Has(dEntT, Up, "Выход") || dEntT.Count != 1)
            { Debug.LogError($"FAIL dungeon: floor-0 entrance want ⬆ Выход, got {dEntT.Count}"); ok = false; }

            Debug.Log(ok ? "Self-Test Interior Transitions: PASS" : "Self-Test Interior Transitions: FAIL");
        }

        static bool Has(List<InteriorTransitions.Transition> list, string arrow, string label)
        {
            foreach (var t in list) if (t.Arrow == arrow && t.Label == label) return true;
            return false;
        }

        static InteriorTransitions.Transition Find(List<InteriorTransitions.Transition> list, string arrow, string label)
        {
            foreach (var t in list) if (t.Arrow == arrow && t.Label == label) return t;
            return default;
        }
    }
}
