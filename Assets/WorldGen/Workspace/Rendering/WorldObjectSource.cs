using System.Collections.Generic;
using WorldGen.Notes.Data;
using WorldGen.Rendering;
using WorldGen.Workspace.Data;

namespace WorldGen.Workspace.Rendering
{
    /// <summary>
    /// The one mapper from the live world into the pure WorldObjectRef DTO the Data layer works in (see
    /// WorldObjectRef's own doc for why PoiData itself never crosses that line). Task 10b wrote this loop
    /// inside QuickOpenPopup because Ctrl+K was its only consumer; Task 10e gave «Мир» the same contents, and
    /// a second copy in NavigatorView would have been two places to remember when settlements/buildings join
    /// the world list — so it moved here and BOTH call it.
    ///
    /// A static helper rather than a component: it holds no state, and the two callers each discover their
    /// own PoiManager (QuickOpenPopup.Attach at attach time, NavigatorView.ResolvePoiManager on every miss)
    /// for their own lifecycle reasons. Nothing here is cached — the list is rebuilt per call, which is what
    /// keeps a freshly placed or renamed POI correct with no invalidation protocol; both callers rebuild at
    /// human speed (a keystroke, a layout change, an OnPoisChanged) over at most a few hundred POIs, the same
    /// cost QuickOpen.Search itself already pays walking every page.
    ///
    /// Lives in Workspace/Rendering, not Workspace/Data, precisely BECAUSE it touches PoiManager and
    /// PoiInfoPopup: Data must stay free of UnityEngine and WorldGen.Generation to keep running in
    /// Tools/notes-harness. This file is not synced there and does not need to be — everything it produces is
    /// a plain WorldObjectRef the harness constructs directly in its own fixtures.
    /// </summary>
    public static class WorldObjectSource
    {
        /// <summary>Every POI as a world object, in PoiManager's own order (which is placement order — the
        /// order «Мир» then shows, per Task 10e's "in the order given"). Null when there is no PoiManager at
        /// all: that is the pre-generation state, or a scene with no map, and both consumers treat it exactly
        /// like "no POIs exist" (QuickOpen's W5 guard; NavigatorTree's own `world != null` check) — so this
        /// returns null rather than an empty list, keeping the two states indistinguishable by construction
        /// instead of by convention.</summary>
        public static List<WorldObjectRef> Collect(PoiManager poiManager)
        {
            if (poiManager == null) return null;

            var pois = poiManager.GetAllPois();
            var world = new List<WorldObjectRef>(pois.Count);
            foreach (var poi in pois)
            {
                if (poi == null) continue;
                world.Add(new WorldObjectRef
                {
                    Kind = WorldRefKind.Poi,
                    Id = poi.Id,
                    // MapScreenController.PoiTitle, not poi.Name raw: a nameless POI must read «Без названия»
                    // in «Мир» and in Ctrl+K exactly as it does in its own tab strip, which is what that
                    // method's doc has always promised (and what NotesDocOps.EnsurePageFor's E3 fallback used
                    // to deliver on the page path this task removed). Raw Name would have left a blank row
                    // with nothing to click on the label of.
                    Name = MapScreenController.PoiTitle(poi),
                    // PoiInfoPopup.TypeLabel — the SAME Russian label switch that popup's own type line shows,
                    // made public static in Task 10b for exactly this reuse; not copied here.
                    KindLabel = PoiInfoPopup.TypeLabel(poi.Type),
                });
            }
            return world;
        }
    }
}
