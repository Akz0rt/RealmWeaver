using System.Collections.Generic;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Rendering.RegionLabels;

namespace WorldGen.Persistence
{
    /// <summary>Top-level shape of a saved project file. GenerationParams is stored for
    /// reference only — Cells is the authoritative map state on load (see
    /// docs/superpowers/specs/2026-07-04-project-save-export-import-design.md).</summary>
    public class ProjectSaveData
    {
        public int FormatVersion = 1;
        public string SavedAtUtc;
        public GenerationParams GenerationParams;
        public List<VoronoiCell> Cells = new List<VoronoiCell>();
        public List<PoiData> Pois = new List<PoiData>();
        public NotesDocument Notes;
        public List<RegionLabelData> RegionLabels = new List<RegionLabelData>();
        /// <summary>Political-region metadata (id/name/colour) — membership itself lives on
        /// VoronoiCell.RegionId (see Cells above). Added in FormatVersion 3; older saves have
        /// this empty and get default RegionData synthesized on load (see ProjectSerializer.Load).</summary>
        public List<RegionData> Regions = new List<RegionData>();
        /// <summary>Cave dungeons, one per owning POI (InteriorData.OwnerPoiId == PoiData.Id).
        /// Added in FormatVersion 4; older saves have this empty (a POI with no dungeon is valid).</summary>
        public List<InteriorData> Dungeons = new List<InteriorData>();
    }
}
