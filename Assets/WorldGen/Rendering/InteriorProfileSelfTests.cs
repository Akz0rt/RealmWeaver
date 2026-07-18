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

            ok &= dun.RoomTypes.Length == 3 && bld.RoomTypes.Length == 5;
            ok &= dun.RoomTypes[0].Label == "Вход" && bld.RoomTypes[0].Label == "Вход"; // index 0 = entrance

            // Pixel-identity lock: reproduces DungeonFlatRenderer's pre-profile TypeRole/LabelRole switches.
            ok &= dun.RoomTypes[0].Role == ThemeRole.Accent
                && dun.RoomTypes[1].Role == ThemeRole.Elev
                && dun.RoomTypes[2].Role == ThemeRole.Danger;
            ok &= dun.RoomTypes[1].LabelRole == ThemeRole.Txt
                && dun.RoomTypes[2].LabelRole == ThemeRole.AccentInk;

            ok &= dun.Layout == LayoutMode.Spread && bld.Layout == LayoutMode.Compact;
            ok &= dun.HasBossRule && !bld.HasBossRule;
            ok &= dun.FloorLinks == FloorLinkMode.ImplicitDescent && bld.FloorLinks == FloorLinkMode.ExplicitStairs;
            ok &= bld.TypeOf(99).Label == "Вход"; // out-of-range clamps to entrance, never throws

            Debug.Log(ok ? "Self-Test Interior Profiles: PASS" : "Self-Test Interior Profiles: FAIL");
        }
    }
}
