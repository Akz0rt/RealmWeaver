using UnityEngine;
using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>[ContextMenu] self-test for InteriorProfile/Profiles. Locks the dungeon's exact ThemeRoles
    /// so a future edit can't silently break pixel-identity with the pre-profile DungeonFlatRenderer.</summary>
    public class InteriorProfileSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Interior Profiles")]
        public void SelfTestProfiles()
        {
            bool ok = true;
            var dun = Profiles.For(InteriorKind.Dungeon);
            var bld = Profiles.For(InteriorKind.Building);

            ok &= dun.RoomTypes.Length == 3 && bld.RoomTypes.Length == 3;   // building = {Вход, Комната, Лестница}
            ok &= dun.RoomTypes[0].Label == "Вход" && bld.RoomTypes[0].Label == "Вход"; // index 0 = entrance
            if (bld.RoomTypes.Length >= 3)   // guard the index access so a shrunk palette logs FAIL, not a throw
            {
                ok &= bld.RoomTypes[1].Label == "Комната" && bld.RoomTypes[1].CardLabel == "Комната"; // the plain room
                ok &= bld.RoomTypes[2].Label == "Лестница"; // index 2 = the stairwell column
            }

            // Pixel-identity lock: reproduces DungeonFlatRenderer's pre-profile TypeRole/LabelRole switches.
            ok &= dun.RoomTypes[0].Role == ThemeRole.Accent
                && dun.RoomTypes[1].Role == ThemeRole.Elev
                && dun.RoomTypes[2].Role == ThemeRole.Danger;
            ok &= dun.RoomTypes[0].LabelRole == ThemeRole.AccentInk
                && dun.RoomTypes[1].LabelRole == ThemeRole.Txt
                && dun.RoomTypes[2].LabelRole == ThemeRole.AccentInk;
            // Node-card labels (used by DungeonFlatRenderer.NodeLabel for untitled rooms). Normal's card
            // label "Комната" differs from its picker label "Обычная" — reproduces the pre-profile TypeLabel.
            ok &= dun.RoomTypes[0].CardLabel == "Вход"
                && dun.RoomTypes[1].CardLabel == "Комната"
                && dun.RoomTypes[2].CardLabel == "Босс";

            // FloorLinks IS read (DungeonFlatRenderer -> DungeonBadgeStrip -> InteriorTransitions), so this
            // pins a real branch. The former Layout/HasBossRule assertions on this line went with the fields:
            // nothing read them, so they only re-stated the literals in Build().
            ok &= dun.FloorLinks == FloorLinkMode.ImplicitDescent && bld.FloorLinks == FloorLinkMode.ExplicitStairs;
            // Screen title (DungeonEditorScreen's top strip) — the two interiors must NOT read the same.
            ok &= dun.TermInterior == "Подземелье" && bld.TermInterior == "Здание";
            ok &= bld.TypeOf(99).Label == "Вход"; // out-of-range clamps to entrance, never throws

            Debug.Log(ok ? "Self-Test Interior Profiles: PASS" : "Self-Test Interior Profiles: FAIL");
        }
    }
}
