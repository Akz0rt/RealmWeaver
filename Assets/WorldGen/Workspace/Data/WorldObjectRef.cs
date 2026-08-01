using WorldGen.Notes.Data;

namespace WorldGen.Workspace.Data
{
    /// <summary>One world object as Ctrl+K sees it: an identity, a name to match, and a label to show on
    /// the right of the row. Deliberately NOT PoiData — QuickOpen stays free of WorldGen.Generation so it
    /// keeps running in Tools/notes-harness, and the search index has no use for position or map state.
    /// One mapper builds these for every consumer — WorldObjectSource.Collect, shared by Ctrl+K and the
    /// navigator's «Мир» since Task 10e — which is also what will let settlements/buildings join later
    /// without touching QuickOpen or NavigatorTree at all.</summary>
    public class WorldObjectRef
    {
        public WorldRefKind Kind;
        public string Id = "";
        public string Name = "";
        /// <summary>Shown on the row's right, e.g. «город» — the spec's «Ржавый Якорь — здание · Тихий Брод».</summary>
        public string KindLabel = "";
    }

    /// <summary>Which surface a world object opens — the Task 10c checkpoint ruling, «точка интереса ЕСТЬ
    /// своё меню редактирования», expressed once instead of at each of its three call sites (NavigatorTree's
    /// «Мир» loop, QuickOpenPopup.ChooseIndex's world-object branch, MapScreenController.PoiEditorSurface).
    /// Three hand-built `new SurfaceRef { Kind = PoiEditor, Id = ... }`s would be three chances to disagree
    /// about the id, and WorkspaceOps.SameSurface compares Kind AND Id — a single character of disagreement
    /// is a second tab for the same place, not a visible error.
    ///
    /// Takes the id alone, not a WorldRefKind: every producer of a world object today
    /// (WorldObjectSource.Collect, the only one) emits Kind=Poi, and Building/Room surfaces are not even
    /// addressable by a bare id — they need SurfaceIds.Interior's composite. When they gain a producer, THIS
    /// method grows the switch and the call sites stay as they are. Deliberately not a speculative
    /// kind-switch today: an unhandled kind would have to return null, and every caller would need a skip
    /// branch for a case nothing can produce.
    ///
    /// A null id yields Id="" rather than null, matching SurfaceIds' own "never emit a null id" rule — a null
    /// would flow into WorkspaceOps.Serialize (Task 11's stored format) and SameSurface unchecked.</summary>
    public static class WorldSurface
    {
        public static SurfaceRef PoiEditor(string poiId)
            => new SurfaceRef { Kind = SurfaceKind.PoiEditor, Id = poiId ?? "" };
    }
}
