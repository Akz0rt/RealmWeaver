using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Rendering.RegionLabels;

namespace WorldGen.Persistence
{
    public class ProjectLoadResult
    {
        public bool Success;
        public string ErrorMessage;    // set only when Success == false
        public string WarningMessage;  // set when Success == true but something is worth flagging (e.g. newer format version)
        public GenerationParams GenerationParams;
        public List<VoronoiCell> Cells;
        public List<PoiData> Pois;
        public NotesDocument Notes;
        public List<RegionLabelData> RegionLabels;
        public List<RegionData> Regions;
        public List<InteriorData> Dungeons;
    }

    /// <summary>
    /// Reads/writes a full project (map cells + POIs + notes document) to a single JSON
    /// file. Pure data-layer code (no MonoBehaviour dependency) so it can be exercised
    /// directly by ProjectSerializerSelfTests without a running scene.
    /// </summary>
    public static class ProjectSerializer
    {
        public const int CurrentFormatVersion = 11;  // 11: the settlement SIZE CLASS replaces the exact
                                                     // building-count knob (SettlementParams.Size, an int
                                                     // 0/1/2; the old "TargetBuildings" key is read back once
                                                     // through SettlementParams.LegacyTargetBuildings, bucketed
                                                     // by SettlementSizing.FromLegacyTarget, then dropped off
                                                     // the wire), AND the building-cell lattice pitch shrinks
                                                     // 0.07 -> 0.03 so the largest size fits the 0..1 field.
                                                     // The pitch is what makes this a MIGRATION rather than a
                                                     // key rename: a v10 town's stored cell indices still
                                                     // describe its shape exactly, but on the new lattice they
                                                     // land at 3/7 of their old normalized position, i.e. off
                                                     // in a corner — so SettlementMigration.RecentreFloor
                                                     // translates every cell in town back onto the field's
                                                     // centre and RederivePositions rewrites every room's
                                                     // point from its own cells. Gates gain a stored cell here
                                                     // too, or they would be the one node left behind.
                                                     //
                                                     // 10: Room.Cells (a settlement building's footprint on the
                                                     // FIXED building-cell lattice, absolute indices) +
                                                     // SettlementParams.StreetCells. Both are absent from a v9
                                                     // file and deserialize to null; SettlementFootprint.
                                                     // EnsureFootprints then gives every settlement building a
                                                     // SINGLE-CELL footprint at the cell its stored point falls
                                                     // in, so an existing town keeps one building per cell and
                                                     // nothing the DM authored is restructured. Bumped so a
                                                     // v0.4.0 build WARNS on open instead of silently dropping
                                                     // every footprint on its next save.
                                                     //
                                                     // 9: Room.Preview (settlement building preview image)
                                                     // + InteriorFloor.Wall (settlement wall contour — that
                                                     // field was LATER removed in the fence rework; the fence
                                                     // is now derived, and any "Wall" key in an existing v9
                                                     // file is simply ignored on load, MissingMemberHandling
                                                     // being Ignore by default).
                                                     // Reading v8 needs no migration — both keys are
                                                     // absent in a v8 file and deserialize to null.
                                                     // Bumped anyway so a v0.3.7 build WARNS on open
                                                     // instead of silently dropping every settlement wall
                                                     // and preview image on its next save.

        static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new CanvasObjectDataConverter(), new ColorJsonConverter() }
        };

        public static void Save(string path, GenerationParams genParams, IReadOnlyList<VoronoiCell> cells,
                                 IReadOnlyList<PoiData> pois, NotesDocument notes,
                                 IReadOnlyList<RegionLabelData> regionLabels,
                                 IReadOnlyList<RegionData> regions,
                                 IReadOnlyList<InteriorData> dungeons)
        {
            var data = new ProjectSaveData
            {
                FormatVersion = CurrentFormatVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                GenerationParams = genParams,
                Cells = new List<VoronoiCell>(cells),
                Pois = new List<PoiData>(pois),
                Notes = notes,
                RegionLabels = new List<RegionLabelData>(regionLabels),
                Regions = new List<RegionData>(regions ?? new List<RegionData>()),
                Dungeons = new List<InteriorData>(dungeons ?? new List<InteriorData>())
            };

            string json = JsonConvert.SerializeObject(data, BuildSettings());
            File.WriteAllText(path, json);
        }

        public static ProjectLoadResult Load(string path)
        {
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return new ProjectLoadResult { Success = false, ErrorMessage = $"Не удалось прочитать файл: {ex.Message}" };
            }

            ProjectSaveData data;
            try
            {
                data = JsonConvert.DeserializeObject<ProjectSaveData>(json, BuildSettings());
            }
            catch (Exception ex)
            {
                return new ProjectLoadResult { Success = false, ErrorMessage = $"Файл повреждён или имеет неверный формат: {ex.Message}" };
            }

            if (data == null)
                return new ProjectLoadResult { Success = false, ErrorMessage = "Файл повреждён или имеет неверный формат." };

            var result = new ProjectLoadResult
            {
                Success = true,
                GenerationParams = data.GenerationParams,
                Cells = data.Cells ?? new List<VoronoiCell>(),
                Pois = data.Pois ?? new List<PoiData>(),
                Notes = data.Notes ?? new NotesDocument(),
                RegionLabels = data.RegionLabels ?? new List<RegionLabelData>(),
                Regions = data.Regions ?? new List<RegionData>(),
                // FormatVersion 5 replaced the tile dungeon with a room-graph. Older tile dungeons cannot be
                // migrated (a graph can't be recovered from the old floor "blob"), so they are dropped; the rest
                // of the project loads normally.
                Dungeons = data.FormatVersion >= 5
                    ? (data.Dungeons ?? new List<InteriorData>())
                    : new List<InteriorData>()
            };

            // Three UNGATED, idempotent normalizations, deliberately not guarded on FormatVersion: each one
            // only fires on a field it finds unset (or, for the third, legacy), so re-running it over an
            // already-normalized project is a no-op, whereas a version guard is a thing the NEXT format bump
            // forgets to widen.
            //   • ApplyDefaults  — a room with a non-positive tile footprint gets its type default (v5-era).
            //   • EnsureFootprints — a SETTLEMENT BUILDING OR GATE with no lattice footprint gets a
            //     single-cell one, at the cell its stored point falls in (v10, widened to gates in v11).
            //     Never overwrites an existing footprint, never touches SizeW/SizeH (those are TILES; one
            //     lattice cell is 3.84 tiles — see that method's doc), and never touches a dungeon or a
            //     building interior.
            //
            //     ITS PITCH FOLLOWS THE FILE, even though the PASS does not. Recovering a cell from a point
            //     needs to know which lattice that point was authored on, and THIS is the only place that
            //     knows: a file at format <= 10 was laid out on the 0.07 pitch, a v11 file on 0.03. Passing
            //     the wrong one writes a cell index that is silently 2.33x out and that nothing later
            //     repairs — RecentreFloor/RederivePositions below are version-gated OFF at v11 — while
            //     SettlementTileGrid.FootprintOf rule (b) masks it at render, so it would be wrong data at
            //     rest with no visible symptom. The pass itself stays ungated (it is idempotent and fires
            //     only on a room it finds footprint-less); only this argument is version-derived.
            //   • NormalizeLegacyTypes — a POI whose stored type is the removed PoiType.Village (legacy id 5)
            //     becomes a City. Not version-gated for the same reason as its two neighbours above: it fires
            //     only on data it finds legacy, and a version guard is what the next bump forgets to widen.
            bool legacyLattice = data.FormatVersion <= 10;
            PoiMigration.NormalizeLegacyTypes(result.Pois);
            foreach (var d in result.Dungeons)
            {
                RoomSizing.ApplyDefaults(d);
                SettlementFootprint.EnsureFootprints(d, legacyLattice);
            }

            // v11 lattice migration. VERSION-GATED, unlike its three neighbours above, and BOTH passes are:
            // recentring is a one-time repair of coordinates authored on the old 0.07 pitch, not a
            // fill-in-the-blank normalization, and re-running it on a town the DM has since dragged to one
            // side would move the town without being asked.
            //
            // RederivePositions IS GATED TOO — a DELIBERATE DEVIATION from this task's brief, which called it
            // ungated on the reasoning that "cells are the source of truth at every version". They are not,
            // for a settlement building the DM has MOVED: dragging writes Room.X/Y from eight editor call
            // sites and never rewrites Room.Cells (SettlementTileGrid.FootprintOf's rule (b) exists precisely
            // to re-derive the stale single-cell footprint from the point at render time, and its own doc
            // says so). So on a v11 file an ungated pass would read the STALE cell and write the point back
            // to it — silently undoing every drag the DM made, on every load. Gated, it has nothing to do on
            // a v11 file anyway: the generator writes cells and points together, and DungeonOps.AddRoom does
            // the same for a hand-added building, so a v11 town's points already agree with its cells.
            //
            // ORDER IS LOAD-BEARING: EnsureFootprints (the loop above) must have run first, or a v9/v10 room
            // has no cells for RecentreFloor to move and the town would recentre around whatever subset
            // happened to be footprinted. The `> 0` guard on the legacy count is what makes the size
            // bucketing safe to run on a floor that never carried the old knob at all.
            if (legacyLattice)
                foreach (var d in result.Dungeons)
                {
                    if (d.Kind != InteriorKind.Settlement) continue;
                    foreach (var f in d.Floors)
                    {
                        if (f.SettlementParams != null && f.SettlementParams.LegacyTargetBuildings > 0)
                        {
                            f.SettlementParams.Size = SettlementSizing.FromLegacyTarget(f.SettlementParams.LegacyTargetBuildings);
                            f.SettlementParams.LegacyTargetBuildings = 0;   // consumed; keeps it off the next save's wire
                        }
                        SettlementMigration.RecentreFloor(f);
                    }
                    SettlementMigration.RederivePositions(d);
                }

            if (data.FormatVersion > CurrentFormatVersion)
                result.WarningMessage = "Файл сохранён более новой версией инструмента — часть данных может не загрузиться.";

            // Legacy migration (v1 ONLY): cells stored a per-cell BiomeOverride, removed in v2. Convert any
            // land-biome override to the canonical climate levels that produce it; water/invalid values are
            // ignored (the cell reclassifies from its stored climate). Best-effort — never fail the load
            // over migration.
            //
            // Guard on the literal 2, NOT CurrentFormatVersion: every line inside this block targets a field
            // that has not existed since v2, so gating it on "older than today's format" makes EVERY v(N-1)
            // file re-run a full second JObject.Parse of the whole (possibly multi-megabyte) save on every
            // load, for a migration that then finds no BiomeOverride key and does nothing. That is exactly
            // what happened once CurrentFormatVersion reached 8: every v7 file (i.e. every project any user
            // had) paid the cost on every open. Do not "simplify" this back to CurrentFormatVersion — the
            // cost returns the next time the format version is bumped.
            if (data.FormatVersion < 2)
            {
                try
                {
                    var cellsToken = JObject.Parse(json)["Cells"] as JArray;
                    if (cellsToken != null)
                    {
                        for (int i = 0; i < cellsToken.Count && i < result.Cells.Count; i++)
                        {
                            var bo = cellsToken[i]?["BiomeOverride"];
                            if (bo == null || bo.Type == JTokenType.Null) continue;
                            var rep = BiomeMatrix.RepresentativeClimate((Biome)bo.Value<int>());
                            if (rep.HasValue)
                            {
                                result.Cells[i].TemperatureOverride = BiomeMatrix.LevelCenter(rep.Value.t);
                                result.Cells[i].MoistureOverride    = BiomeMatrix.LevelCenter(rep.Value.m);
                            }
                        }
                    }
                }
                catch { /* migration is best-effort */ }
            }

            // Legacy migration (pre-v3): regions used to be pure cell membership (VoronoiCell.RegionId)
            // with no RegionData metadata (name/colour) at all - RegionData/RegionManager didn't exist
            // yet. If the file carries no region metadata but its cells still reference region ids,
            // synthesize a default RegionData per distinct id so borders/fill/names work after loading
            // (same fantasy-name + palette-colour scheme GenerateRegionsOnly uses for freshly-generated
            // regions). Keyed off the data itself (empty Regions + assigned cells), not FormatVersion,
            // so it also recovers a would-be-inconsistent v3 file. Best-effort - never fail the load.
            if (result.Regions.Count == 0)
            {
                try
                {
                    var distinctIds = new SortedSet<int>();
                    foreach (var c in result.Cells)
                        if (c.RegionId >= 0) distinctIds.Add(c.RegionId);

                    if (distinctIds.Count > 0)
                    {
                        int seed = data.GenerationParams?.Seed ?? 0;
                        foreach (int id in distinctIds)
                        {
                            string name = RegionLabelNames.ContinentName(seed, id);
                            result.Regions.Add(new RegionData(id, name, WorldGen.Rendering.RegionColorPalette.GetRegionColor(id)));
                        }
                    }
                }
                catch { /* migration is best-effort */ }
            }

            return result;
        }
    }
}
