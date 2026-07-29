using System.Collections.Generic;

namespace WorldGen.Workspace.Data
{
    /// <summary>What kind of surface a tab shows. WorldMap has no id — there is only one world map, so
    /// SurfaceRef.Id is always empty for it.</summary>
    public enum SurfaceKind { Page = 0, WorldMap = 1, Settlement = 2, BuildingInterior = 3, Dungeon = 4, BattleGrid = 5 }

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
        /// 0.25..0.75 by whatever drives the drag handle — this layer does not clamp it itself, it has no op
        /// that would move it.</summary>
        public float SplitRatio = 0.5f;

        public bool NavigatorCollapsed;

        /// <summary>Pixel width of the navigator when expanded. Default 236, meant to stay clamped to
        /// 160..420 by whatever drives its resize handle, for the same reason SplitRatio is not clamped here.</summary>
        public float NavigatorWidth = 236f;
    }
}
