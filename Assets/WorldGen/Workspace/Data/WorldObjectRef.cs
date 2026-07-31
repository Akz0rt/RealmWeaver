using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>One world object as Ctrl+K sees it: an identity, a name to match, and a label to show on
    /// the right of the row. Deliberately NOT PoiData — QuickOpen stays free of WorldGen.Generation so it
    /// keeps running in Tools/notes-harness, and the search index has no use for position or map state.
    /// The caller maps its own types into this (see QuickOpenPopup), which is also what will let
    /// settlements/buildings join later without touching QuickOpen at all.</summary>
    public class WorldObjectRef
    {
        public WorldRefKind Kind;
        public string Id = "";
        public string Name = "";
        /// <summary>Shown on the row's right, e.g. «город» — the spec's «Ржавый Якорь — здание · Тихий Брод».</summary>
        public string KindLabel = "";
    }
}
