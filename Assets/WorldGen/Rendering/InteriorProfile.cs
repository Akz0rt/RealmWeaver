using WorldGen.Generation;
using WorldGen.Rendering.Theme;

namespace WorldGen.Rendering
{
    /// <summary>How floors connect. ImplicitDescent (dungeon) links floors via descent/ascend badges derived
    /// from portals; ExplicitStairs (building) requires an authored Stairs/Ladder portal to move floors.</summary>
    public enum FloorLinkMode { ImplicitDescent = 0, ExplicitStairs = 1 }

    /// <summary>A room type's label + theme roles. Colors are NEVER stored raw -- they come from the
    /// ThemeRole system (ThemeService) so the dungeon stays theme-aware AND pixel-identical to the
    /// pre-profile renderer. Role paints the card; LabelRole paints the room-name text on that card.</summary>
    public struct RoomTypeDef
    {
        public string Label;      // the type-picker button label in the inspector (e.g. "Обычная")
        public ThemeRole Role;
        public ThemeRole LabelRole;
        public string CardLabel;  // the node-card label for an UNtitled room (e.g. "Комната") — may differ from Label

        // cardLabel defaults to label; pass it only when the card fallback text differs from the picker label
        // (the dungeon's Normal type: picker "Обычная" vs card "Комната", preserving the pre-profile renderer).
        public RoomTypeDef(string label, ThemeRole role, ThemeRole labelRole, string cardLabel = null)
        {
            Label = label;
            Role = role;
            LabelRole = labelRole;
            CardLabel = cardLabel ?? label;
        }
    }

    /// <summary>Everything that differs between a dungeon interior and a building interior: room-type
    /// palette, floor-linking convention, and UI terminology. Convention: RoomTypes[0] is always the
    /// entrance. TypeOf() never throws -- out-of-range ids clamp to index 0.
    ///
    /// EVERY field here is READ by shipped code -- kept that way on purpose. Four speculative ones
    /// (Layout/LayoutMode, HasBossRule, TermGenerate, ShowTemplates) were dropped in the pre-merge cleanup:
    /// nothing but InteriorProfileSelfTests ever read them, so they asserted only that the two Build()
    /// methods still contained the literals they had been given. HasBossRule in particular was a SECOND
    /// source of truth for a rule DungeonValidator already derives from InteriorData.Kind -- and could not
    /// have been wired in there anyway, since the validator is headless and this file is not.</summary>
    public class InteriorProfile
    {
        public InteriorKind Kind;
        public RoomTypeDef[] RoomTypes;
        public FloorLinkMode FloorLinks;
        public string TermFloor;      // "Этаж"  -- the floor tabs and the +/x floor buttons
        public string TermRoom;       // "Комната" -- the free-edit toolbar's «+ <room>»
        public string TermInterior;   // "Подземелье" / "Здание" -- the editor's top-strip title

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
                    new RoomTypeDef("Обычная", ThemeRole.Elev,   ThemeRole.Txt, cardLabel: "Комната"), // card shows "Комната", picker shows "Обычная" (pre-profile parity)
                    new RoomTypeDef("Босс",    ThemeRole.Danger, ThemeRole.AccentInk),
                },
                FloorLinks = FloorLinkMode.ImplicitDescent,
                TermFloor = "Этаж",
                TermRoom = "Комната",
                TermInterior = "Подземелье",
            };
        }
    }

    /// <summary>Building interior profile (user 2026-07-19). Three types: an outside entrance, a plain room,
    /// and the stairwell that connects floors vertically. The stairwell column is user-placed on floor 0 and
    /// sits at the SAME (x,y) on every floor (<see cref="Generation.BuildingGenerator"/>). RoomCommon paints
    /// the plain room; the stair reuses RoomSpecial provisionally (tunable). Legacy saves with the old
    /// TypeId 3/4 are collapsed by
    /// <see cref="Generation.BuildingGenerator.NormalizeTypes"/> on load.</summary>
    public static class BuildingProfile
    {
        public static InteriorProfile Build()
        {
            return new InteriorProfile
            {
                Kind = InteriorKind.Building,
                RoomTypes = new[]
                {
                    new RoomTypeDef("Вход",     ThemeRole.Accent,      ThemeRole.AccentInk),
                    new RoomTypeDef("Комната",  ThemeRole.RoomCommon,  ThemeRole.Txt),
                    new RoomTypeDef("Лестница", ThemeRole.RoomSpecial, ThemeRole.Txt),
                },
                FloorLinks = FloorLinkMode.ExplicitStairs,
                TermFloor = "Этаж",
                TermRoom = "Комната",
                TermInterior = "Здание",
            };
        }
    }

    /// <summary>Settlement profile (Ц1). Two node types: a Gate on the wall and a Building. No floors — the
    /// editor's floor machinery is Kind-gated off. Terminology reads as a town, not a dungeon.</summary>
    public static class SettlementProfile
    {
        public static InteriorProfile Build()
        {
            return new InteriorProfile
            {
                Kind = InteriorKind.Settlement,
                RoomTypes = new[]
                {
                    new RoomTypeDef("Ворота", ThemeRole.Accent,     ThemeRole.AccentInk),
                    new RoomTypeDef("Здание", ThemeRole.RoomCommon,  ThemeRole.Txt),
                },
                FloorLinks = FloorLinkMode.ImplicitDescent,   // unused — settlements have one floor
                TermFloor = "Этаж",                            // unused — floor UI is Kind-gated off
                TermRoom = "Здание",
                TermInterior = "Поселение",
            };
        }
    }

    /// <summary>Per-kind singleton profiles, built once.</summary>
    public static class Profiles
    {
        static readonly InteriorProfile Dungeon = DungeonProfile.Build();
        static readonly InteriorProfile Building = BuildingProfile.Build();
        static readonly InteriorProfile Settlement = SettlementProfile.Build();

        public static InteriorProfile For(InteriorKind k)
        {
            if (k == InteriorKind.Building) return Building;
            if (k == InteriorKind.Settlement) return Settlement;
            return Dungeon;
        }

        public static InteriorProfile ForRoom(InteriorData d) => For(d.Kind);

        /// <summary>Maps a POI type to the interior kind it opens (Task 6): Dungeon POIs get a dungeon;
        /// Tower/Temple/Fortress/Ruin get a building; City/Village get a settlement; everything else
        /// (Camp/Port/Encounter/Unknown) has no interior in this sub-project.</summary>
        public static InteriorKind? InteriorKindForPoiType(PoiType t)
        {
            switch (t)
            {
                case PoiType.Dungeon: return InteriorKind.Dungeon;
                case PoiType.Tower:
                case PoiType.Temple:
                case PoiType.Fortress:
                case PoiType.Ruin: return InteriorKind.Building;
                case PoiType.City:
                case PoiType.Village: return InteriorKind.Settlement;
                default: return null;   // Camp/Port/Encounter/Unknown → no interior (this sub-project)
            }
        }
    }
}
