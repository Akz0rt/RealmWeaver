using System.Collections.Generic;

namespace WorldGen.Workspace.Data
{
    /// <summary>What kind of surface a tab shows. WorldMap has no id — there is only one world map, so
    /// SurfaceRef.Id is always empty for it.
    ///
    /// PoiEditor (Task 10c) is NOT in the Р1 spec's surface TABLE, and is added deliberately rather than by
    /// oversight — the spec's own screen-layer section names it («`MapEditor`, `PoiEditor`, `Dungeon` and
    /// `BattleGrid` stop being screens and become surfaces a tab hosts»), and without it Р1 would ship with
    /// settlements and dungeons UNREACHABLE: PoiEditorScreen's «КАРТА ЛОКАЦИИ» row (PoiEditorScreen.cs:744 ->
    /// OnOpenDungeonRequested, assigned at MapScreenController.cs:95) is the only path into the interior
    /// editor in the whole project, and the spec explicitly defers the page-side replacement («showing the
    /// settlement, «Открыть город», the inspector link — stays in Р3»). So the POI editor keeps existing as a
    /// surface until Р5 redesigns it away — and since Task 10e it is what EVERY gesture aimed at a place
    /// opens: the popup's «Редактировать», a double-click on the map, a «Мир» row in the navigator and a
    /// world row in Ctrl+K all produce this one SurfaceRef (WorldSurface.PoiEditor). «Точка интереса ЕСТЬ
    /// своё меню редактирования»; a note about a place is a separate object, opened as a Page like any other
    /// note. Before that ruling the double-click opened the place's PAGE, which is the model this paragraph
    /// used to describe.
    ///
    /// APPENDED at the end, never renumbered: WorkspaceOps.Serialize writes the enum's NAME (not its ordinal)
    /// and TryParseSurfaceKind reads it back by name, so the numbers are not themselves a wire format — but
    /// Task 11's WorkspacePrefs stores those payloads in PlayerPrefs across versions, so keeping additions
    /// purely additive means an older payload keeps parsing unchanged.</summary>
    public enum SurfaceKind { Page = 0, WorldMap = 1, Settlement = 2, BuildingInterior = 3, Dungeon = 4, BattleGrid = 5, PoiEditor = 6 }

    /// <summary>What a tab points at. Two refs name the same surface when Kind AND Id match — see
    /// WorkspaceOps.SameSurface, the one place that comparison is made.</summary>
    public class SurfaceRef
    {
        public SurfaceKind Kind;
        public string Id = "";
    }

    public class TabState
    {
        public SurfaceRef Surface;
        public string Title = "";
    }

    public class PaneState
    {
        public List<TabState> Tabs = new List<TabState>();

        /// <summary>Index of the active tab. -1 exactly when Tabs is empty — never a stale index into a
        /// shorter list. WorkspaceOps.FixActiveIndexAfterRemoval is the one place that keeps this true.</summary>
        public int ActiveIndex = -1;
    }

    /// <summary>The whole navigator+panes layout. A plain class with public fields, not a record — records
    /// need init accessors (System.Runtime.CompilerServices.IsExternalInit), which .NET Standard 2.1 lacks,
    /// so they do not compile under Unity 2022.3.</summary>
    public class WorkspaceLayout
    {
        /// <summary>Always non-null. There is never a moment with zero panes: closing the last tab leaves an
        /// EMPTY Primary, not a null one (WorkspaceOps.NormalizeSplit).</summary>
        public PaneState Primary = new PaneState();

        /// <summary>null = no split. Deliberately nullable rather than paired with an "IsSplit" flag, so the
        /// two can never disagree about whether the split is showing.</summary>
        public PaneState Secondary;

        /// <summary>0 = Primary, 1 = Secondary. Only ever names a pane that exists — see WorkspaceOps.Focus.</summary>
        public int FocusedPane;

        /// <summary>Fraction of width given to Primary when split. Default 0.5, meant to stay clamped to
        /// 0.25..0.75 by whatever drives the drag handle — no op in this layer moves it, so nothing here
        /// clamps it on write. WorkspaceOps.TryDeserialize DOES clamp it on READ, since a stored value comes
        /// from PlayerPrefs, which is a plain user-writable file and cannot be trusted.</summary>
        public float SplitRatio = 0.5f;

        public bool NavigatorCollapsed;

        /// <summary>Pixel width of the navigator when expanded. Default 236, meant to stay clamped to
        /// 160..420 by whatever drives its resize handle, for the same reason SplitRatio is not clamped on
        /// write here — and, like SplitRatio, clamped on READ by WorkspaceOps.TryDeserialize.</summary>
        public float NavigatorWidth = 236f;
    }
}
