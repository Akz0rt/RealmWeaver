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
        const string Side = "⇢";

        [ContextMenu("Self-Test: Interior Transitions")]
        public void SelfTestInteriorTransitions()
        {
            bool ok = true;

            // ── BUILDING (ExplicitStairs) ────────────────────────────────────────────────────────────
            // 3 floors. Floor 0: entrance (id1) + stair room (id2) with a Stairs portal UP to floor 1 room 1.
            // Floor 1: room 1 is the TARGET of floor-0's stair AND has its own Stairs portal UP to floor 2 —
            // a middle floor, so it must show BOTH ⬆ (own) and ⬇ (derived). Room 2 carries TypeId 2 (a legacy/
            // out-of-palette value — buildings are now just {0,1}) to prove the building path NEVER treats a
            // TypeId-2 room as a dungeon boss. Floor 2: room 1 is the target of floor-1's stair (down only).
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
            bf1.Rooms.Add(new Room { Id = 2, TypeId = 2 });   // legacy/out-of-palette type — must NOT descend like a dungeon boss
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

            // 7. DUNGEON portals render "⇢" — the byte-identity case the boss/entrance tests miss. A SecretDoor
            //    is clickable→its target; a DungeonExit is informational. Fails if the "⇢ Э{n}·{room}" format,
            //    the +1 floor offset, or the SecretDoor-only clickable gate regresses.
            var portalRoom = new Room { Id = 3, TypeId = 1 };
            portalRoom.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 1, TargetRoomId = 1 });
            portalRoom.Portals.Add(new Portal { Kind = PortalKind.DungeonExit });
            df0.Rooms.Add(portalRoom);
            var portT = InteriorTransitions.For(d, FloorLinkMode.ImplicitDescent, 0, portalRoom);
            var secret = Find(portT, Side, "Э2·1");
            var dexit = Find(portT, Side, "Выход");
            if (portT.Count != 2) { Debug.LogError($"FAIL dungeon-portal: want 2 portal badges, got {portT.Count}"); ok = false; }
            if (secret.Arrow == null || !secret.Clickable || secret.TargetFloorIndex != 1)
            { Debug.LogError("FAIL dungeon-portal: SecretDoor must be ⇢ Э2·1, clickable→floor 1"); ok = false; }
            if (dexit.Arrow == null || dexit.Clickable)
            { Debug.LogError("FAIL dungeon-portal: DungeonExit must be ⇢ Выход, not clickable"); ok = false; }

            // 8. BUILDING explicit+derived DEDUP: floor-1 room B links DOWN to floor-0 room A explicitly, and
            //    floor-0 room A links UP to B. B must show the down badge exactly ONCE (its explicit one), not
            //    also a derived duplicate. Fails with Count 2 if the linkedFloors dedup is removed.
            var bd = new InteriorData { Kind = InteriorKind.Building };
            var bd0 = new InteriorFloor();
            var roomA = new Room { Id = 1, TypeId = 1 };
            roomA.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 1, TargetRoomId = 1 });
            bd0.Rooms.Add(roomA);
            bd.Floors.Add(bd0);
            var bd1 = new InteriorFloor();
            var roomB = new Room { Id = 1, TypeId = 1 };
            roomB.Portals.Add(new Portal { Kind = PortalKind.Stairs, TargetFloorIndex = 0, TargetRoomId = 1 });
            bd1.Rooms.Add(roomB);
            bd.Floors.Add(bd1);
            var dedupT = InteriorTransitions.For(bd, FloorLinkMode.ExplicitStairs, 1, roomB);
            if (!Has(dedupT, Down, "Этаж 1") || dedupT.Count != 1)
            { Debug.LogError($"FAIL building-dedup: room B want exactly one ⬇ Этаж 1, got {dedupT.Count}"); ok = false; }

            // 9. BUILDING non-stair portal is NOT dropped: a hand-authored SecretDoor on a building room still
            //    renders a ⇢ badge (before C5 it did; the stair-only path must not silently drop it).
            var bs = new InteriorData { Kind = InteriorKind.Building };
            var bs0 = new InteriorFloor();
            var secretRoom = new Room { Id = 1, TypeId = 1 };
            secretRoom.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = 2 });
            bs0.Rooms.Add(secretRoom);
            bs0.Rooms.Add(new Room { Id = 2, TypeId = 1 });
            bs.Floors.Add(bs0);
            var bsecretT = InteriorTransitions.For(bs, FloorLinkMode.ExplicitStairs, 0, secretRoom);
            if (!Has(bsecretT, Side, "Э1·2"))
            { Debug.LogError("FAIL building-secret: a SecretDoor on a building room must still render ⇢ Э1·2"); ok = false; }

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
