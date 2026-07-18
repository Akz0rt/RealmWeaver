using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>Layout strategy for the interior's node canvas. Spread (dungeon) lets rooms sprawl loosely;
    /// Compact (building) packs rooms tighter (later sub-project wires the renderer to read this).</summary>
    public enum LayoutMode { Spread = 0, Compact = 1 }

    /// <summary>How floors connect. ImplicitDescent (dungeon) links floors via descent/ascend badges derived
    /// from portals; ExplicitStairs (building) requires an authored Stairs/Ladder portal to move floors.</summary>
    public enum FloorLinkMode { ImplicitDescent = 0, ExplicitStairs = 1 }

    /// <summary>A room type's label + theme roles. Colors are NEVER stored raw -- they come from the
    /// ThemeRole system (ThemeService) so the dungeon stays theme-aware AND pixel-identical to the
    /// pre-profile renderer. Role paints the card; LabelRole paints the room-name text on that card.</summary>
    public struct RoomTypeDef
    {
        public string Label;
        public ThemeRole Role;
        public ThemeRole LabelRole;

        public RoomTypeDef(string label, ThemeRole role, ThemeRole labelRole)
        {
            Label = label;
            Role = role;
            LabelRole = labelRole;
        }
    }

    /// <summary>Everything that differs between a dungeon interior and a building interior: room-type
    /// palette, layout strategy, floor-linking convention, boss-room rule, and UI terminology. Convention:
    /// RoomTypes[0] is always the entrance. TypeOf() never throws -- out-of-range ids clamp to index 0.</summary>
    public class InteriorProfile
    {
        public InteriorKind Kind;
        public RoomTypeDef[] RoomTypes;
        public LayoutMode Layout;
        public FloorLinkMode FloorLinks;
        public bool HasBossRule;
        public string TermFloor;
        public string TermRoom;
        public string TermGenerate;
        public bool ShowTemplates;

        public RoomTypeDef TypeOf(int id)
        {
            if (id < 0 || id >= RoomTypes.Length) return RoomTypes[0];
            return RoomTypes[id];
        }
    }

    /// <summary>Dungeon interior profile. Room-type roles reproduce DungeonFlatRenderer's pre-profile
    /// TypeRole/LabelRole switches EXACTLY (0=Accent/AccentInk, 1=Elev/Txt, 2=Danger/AccentInk) --
    /// this is the pixel-identity gate for the Task 3 renderer migration.</summary>
    public static class DungeonProfile
    {
        public static InteriorProfile Build()
        {
            return new InteriorProfile
            {
                Kind = InteriorKind.Dungeon,
                RoomTypes = new[]
                {
                    new RoomTypeDef("Вход",    ThemeRole.Accent, ThemeRole.AccentInk),
                    new RoomTypeDef("Обычная", ThemeRole.Elev,   ThemeRole.Txt),
                    new RoomTypeDef("Босс",    ThemeRole.Danger, ThemeRole.AccentInk),
                },
                Layout = LayoutMode.Spread,
                FloorLinks = FloorLinkMode.ImplicitDescent,
                HasBossRule = true,
                TermFloor = "Этаж",
                TermRoom = "Комната",
                TermGenerate = "Сгенерировать",
                ShowTemplates = false,
            };
        }
    }

    /// <summary>Building interior profile. Distinct readable room-type roles (RoomCommon/RoomPrivate/
    /// RoomService/RoomSpecial) added to ThemeService for this purpose; provisional hex values, tuned
    /// visually once the building renderer ships.</summary>
    public static class BuildingProfile
    {
        public static InteriorProfile Build()
        {
            return new InteriorProfile
            {
                Kind = InteriorKind.Building,
                RoomTypes = new[]
                {
                    new RoomTypeDef("Вход",      ThemeRole.Accent,      ThemeRole.AccentInk),
                    new RoomTypeDef("Общая",     ThemeRole.RoomCommon,  ThemeRole.Txt),
                    new RoomTypeDef("Приватная", ThemeRole.RoomPrivate, ThemeRole.Txt),
                    new RoomTypeDef("Служебная", ThemeRole.RoomService, ThemeRole.Txt),
                    new RoomTypeDef("Особая",    ThemeRole.RoomSpecial, ThemeRole.Txt),
                },
                Layout = LayoutMode.Compact,
                FloorLinks = FloorLinkMode.ExplicitStairs,
                HasBossRule = false,
                TermFloor = "Этаж",
                TermRoom = "Комната",
                TermGenerate = "Сгенерировать",
                ShowTemplates = true,
            };
        }
    }

    /// <summary>Per-kind singleton profiles, built once.</summary>
    public static class Profiles
    {
        static readonly InteriorProfile Dungeon = DungeonProfile.Build();
        static readonly InteriorProfile Building = BuildingProfile.Build();

        public static InteriorProfile For(InteriorKind k)
        {
            return k == InteriorKind.Building ? Building : Dungeon;
        }

        public static InteriorProfile ForRoom(InteriorData d) => For(d.Kind);
    }
}
