using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>Which surface a piece of the notes document opens, built in ONE place.
    ///
    /// The same argument WorldSurface makes for POIs: WorkspaceOps.SameSurface compares Kind AND Id, so two
    /// hand-built `new SurfaceRef { Kind = Canvas, Id = ... }`s that disagree by one character are not a
    /// visible error — they are a second tab for the same board.
    ///
    /// A null id yields Id="" rather than null, matching SurfaceIds' "never emit a null id" rule: a null would
    /// flow into WorkspaceOps.Serialize (the format WorkspacePrefs stores) and into SameSurface unchecked.</summary>
    public static class NotesSurface
    {
        /// <summary>Fallback tab title for a board the DM has not captioned. An empty tab is a tab they cannot
        /// aim at.</summary>
        public const string UntitledCanvas = "Доска";

        public static SurfaceRef Canvas(string blockId)
            => new SurfaceRef { Kind = SurfaceKind.Canvas, Id = blockId ?? "" };

        /// <summary>The tab title for an expanded board: its caption, which IS DocBlock.Text — there is no
        /// separate title field, deliberately, so renaming the caption in the page renames the tab.</summary>
        public static string TitleOf(DocBlock canvas)
        {
            string caption = canvas != null ? (canvas.Text ?? "").Trim() : "";
            return caption.Length > 0 ? caption : UntitledCanvas;
        }
    }
}
