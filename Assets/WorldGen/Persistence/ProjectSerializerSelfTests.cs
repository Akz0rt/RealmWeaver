using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Notes.Data;
using WorldGen.Rendering.RegionLabels;

namespace WorldGen.Persistence
{
    /// <summary>
    /// Thin MonoBehaviour hosting [ContextMenu] self-tests for ProjectSerializer, matching
    /// this project's convention of self-tests living on a component — ProjectSerializer
    /// itself is a static data-layer class with no natural scene home. Add this component to
    /// any GameObject in the scene to run these tests via the Inspector's right-click menu.
    /// </summary>
    public class ProjectSerializerSelfTests : MonoBehaviour
    {
        [ContextMenu("Self-Test: Project Round-Trip")]
        public void SelfTestRoundTrip()
        {
            var genParams = new GenerationParams { Seed = 42, Width = 100f, Height = 100f };

            var cellA = new VoronoiCell(0, new System.Numerics.Vector2(1f, 2f))
            {
                Polygon = new List<System.Numerics.Vector2> { new(0, 0), new(1, 0), new(1, 1) },
                NeighborIds = new List<int> { 1 },
                Height = 0.6f,
                IsOcean = false,
                RegionId = 2,
                Temperature = 0.4f,
                Humidity = 0.7f,
                TemperatureOverride = 0.9f,
                MoistureOverride = 0.15f,
                ElevationOverride = 0.8f,
                WaterOverride = WaterOverrideType.ForceLake,
                Biome = Biome.Tundra
            };
            var cellB = new VoronoiCell(1, new System.Numerics.Vector2(3f, 4f))
            {
                Polygon = new List<System.Numerics.Vector2> { new(1, 0), new(2, 0), new(2, 1) },
                NeighborIds = new List<int> { 0 },
                Height = 0.2f,
                IsOcean = true,
                Biome = Biome.Ocean
            };
            var cells = new List<VoronoiCell> { cellA, cellB };

            var poiWithIcon = new PoiData
            {
                Type = PoiType.City,
                Name = "Городок",
                Description = "Тестовый город",
                OwnerCellId = 0,
                WorldPosition = new System.Numerics.Vector2(1f, 2f),
                CustomIconBytes = new byte[] { 1, 2, 3, 4 },
                IconScale = 1.5f,
                LabelScale = 0.8f
            };
            var poiPlain = new PoiData { Type = PoiType.Ruin, Name = "Руины", OwnerCellId = 1 };
            var pois = new List<PoiData> { poiWithIcon, poiPlain };

            var page = new NotesPage { Name = "Страница 1" };
            var card = new NoteCardData { Title = "Заметка", Body = "Текст" };
            var image = new ImageObjectData { ImageBytes = new byte[] { 5, 6, 7 } };
            var drawing = new DrawingObjectData(64, 32) { PixelDataPng = new byte[] { 8, 9 } };
            page.Objects.Add(card);
            page.Objects.Add(image);
            page.Objects.Add(drawing);
            page.Links.Add(new LinkData { FromObjectId = card.Id, ToObjectId = image.Id, Directed = true, ControlPointOffset = new System.Numerics.Vector2(5f, 5f) });
            var group = new PageGroup { Title = "Группа", Pages = new List<NotesPage> { page } };
            var notes = new NotesDocument { Groups = new List<PageGroup> { group } };

            string path = Path.Combine(Application.temporaryCachePath, "project_roundtrip_selftest.json");
            ProjectSerializer.Save(path, genParams, cells, pois, notes, new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());
            var result = ProjectSerializer.Load(path);
            File.Delete(path);

            bool ok = result.Success;
            ok &= result.Cells.Count == 2;

            var loadedA = result.Cells.FirstOrDefault(c => c.Id == 0);
            ok &= loadedA != null
                && loadedA.TemperatureOverride == 0.9f
                && loadedA.MoistureOverride == 0.15f
                && loadedA.ElevationOverride == 0.8f
                && loadedA.WaterOverride == WaterOverrideType.ForceLake
                && loadedA.NeighborIds.Count == 1 && loadedA.NeighborIds[0] == 1
                && loadedA.Polygon.Count == 3;

            var loadedB = result.Cells.FirstOrDefault(c => c.Id == 1);
            ok &= loadedB != null && loadedB.TemperatureOverride == null && loadedB.IsOcean;

            ok &= result.Pois.Count == 2;
            var loadedPoiIcon = result.Pois.FirstOrDefault(p => p.Name == "Городок");
            ok &= loadedPoiIcon != null
                && loadedPoiIcon.CustomIconBytes != null
                && loadedPoiIcon.CustomIconBytes.SequenceEqual(new byte[] { 1, 2, 3, 4 })
                && loadedPoiIcon.IconScale == 1.5f;

            ok &= result.Notes.Groups.Count == 1;
            var loadedPage = result.Notes.Groups[0].Pages.FirstOrDefault();
            ok &= loadedPage != null && loadedPage.Objects.Count == 3 && loadedPage.Links.Count == 1;

            var loadedCard = loadedPage?.Objects.OfType<NoteCardData>().FirstOrDefault();
            var loadedImage = loadedPage?.Objects.OfType<ImageObjectData>().FirstOrDefault();
            var loadedDrawing = loadedPage?.Objects.OfType<DrawingObjectData>().FirstOrDefault();
            ok &= loadedCard != null && loadedCard.Title == "Заметка" && loadedCard.Body == "Текст";
            ok &= loadedImage != null && loadedImage.ImageBytes != null && loadedImage.ImageBytes.SequenceEqual(new byte[] { 5, 6, 7 });
            ok &= loadedDrawing != null && loadedDrawing.PixelWidth == 64 && loadedDrawing.PixelHeight == 32
                && loadedDrawing.PixelDataPng != null && loadedDrawing.PixelDataPng.SequenceEqual(new byte[] { 8, 9 });

            var loadedLink = loadedPage?.Links.FirstOrDefault();
            ok &= loadedLink != null && loadedLink.Directed && loadedLink.ControlPointOffset == new System.Numerics.Vector2(5f, 5f);

            Debug.Log(ok
                ? "Self-Test Project Round-Trip: PASS"
                : "Self-Test Project Round-Trip: FAIL — see field checks in SelfTestRoundTrip");
        }

        [ContextMenu("Self-Test: POI Type Backward Compat")]
        public void SelfTestPoiTypeBackwardCompat()
        {
            var genParams = new GenerationParams { Seed = 1, Width = 10f, Height = 10f };
            var cells = new List<VoronoiCell>();
            var notes = new NotesDocument();

            // --- Part 1: a pre-existing type (Fortress, int 4) and a newly-appended type
            // (Port, int 10) round-trip together, each preserving its own value. ---
            var poiFortress = new PoiData { Type = PoiType.Fortress, Name = "Крепость", OwnerCellId = 1 };
            var poiPort = new PoiData { Type = PoiType.Port, Name = "Гавань", OwnerCellId = 2 };
            var pois = new List<PoiData> { poiFortress, poiPort };

            string path = Path.Combine(Application.temporaryCachePath, "poi_type_backcompat_selftest.json");
            ProjectSerializer.Save(path, genParams, cells, pois, notes, new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());
            var result = ProjectSerializer.Load(path);
            File.Delete(path);

            bool ok = result.Success && result.Pois.Count == 2;
            var loadedFortress = result.Pois.FirstOrDefault(p => p.Name == "Крепость");
            var loadedPort = result.Pois.FirstOrDefault(p => p.Name == "Гавань");
            ok &= loadedFortress != null && loadedFortress.Type == PoiType.Fortress;
            ok &= loadedPort != null && loadedPort.Type == PoiType.Port;

            // --- Part 2: simulate an "old" save file that predates this enum expansion — its
            // on-disk int (4) must still deserialize to Fortress (guards against a future
            // accidental reorder shifting existing values). ---
            var oldStylePois = new List<PoiData> { new PoiData { Type = PoiType.Port, Name = "Гавань", OwnerCellId = 2 } };
            string newPath = Path.Combine(Application.temporaryCachePath, "poi_type_backcompat_new_selftest.json");
            ProjectSerializer.Save(newPath, genParams, cells, oldStylePois, notes, new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());

            string json = File.ReadAllText(newPath);
            string oldJson = json.Replace("\"Type\": 10", "\"Type\": 4");
            string oldPath = Path.Combine(Application.temporaryCachePath, "poi_type_backcompat_old_selftest.json");
            File.WriteAllText(oldPath, oldJson);

            var oldResult = ProjectSerializer.Load(oldPath);
            File.Delete(newPath);
            File.Delete(oldPath);

            ok &= oldResult.Success && oldResult.Pois.Count == 1 && oldResult.Pois[0].Type == PoiType.Fortress;

            Debug.Log(ok
                ? "Self-Test POI Type Backward Compat: PASS"
                : "Self-Test POI Type Backward Compat: FAIL — see field checks in SelfTestPoiTypeBackwardCompat");
        }

        [ContextMenu("Self-Test: POI Preview Round-Trip")]
        public void SelfTestPoiPreviewRoundTrip()
        {
            bool ok = true;
            var genParams = new GenerationParams { Seed = 1, Width = 10f, Height = 10f };
            var notes = new NotesDocument();

            // ---- 1. Byte-integrity: a POI WITH a Preview round-trips it intact; a POI WITHOUT one loads
            // null. Both share one file, so this checks VALUES only — the JSON-text omission check (part 2
            // below) needs a narrower file, since this POI A here legitimately writes a "Preview" key. -------
            var poiWithPreview = new PoiData { Type = PoiType.City, Name = "Город с превью", OwnerCellId = 0, Preview = new byte[] { 9, 8, 7 } };
            var poiNoPreview = new PoiData { Type = PoiType.Village, Name = "Деревня без превью", OwnerCellId = 1 };
            var pois = new List<PoiData> { poiWithPreview, poiNoPreview };

            string path = Path.Combine(Application.temporaryCachePath, "poi_preview_roundtrip_selftest.json");
            ProjectSerializer.Save(path, genParams, new List<VoronoiCell>(), pois, notes,
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());
            var result = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            if (!result.Success || result.Pois.Count != 2)
            { Debug.LogError("FAIL poi preview: the project did not load back with its two POIs"); ok = false; }
            else
            {
                var loadedWith = result.Pois.FirstOrDefault(p => p.Name == "Город с превью");
                var loadedWithout = result.Pois.FirstOrDefault(p => p.Name == "Деревня без превью");
                if (loadedWith == null || loadedWith.Preview == null || !loadedWith.Preview.SequenceEqual(new byte[] { 9, 8, 7 }))
                { Debug.LogError("FAIL poi preview: POI A's Preview bytes were lost or corrupted"); ok = false; }
                if (loadedWithout == null || loadedWithout.Preview != null)
                { Debug.LogError("FAIL poi preview: POI B (no image) loaded with a non-null Preview"); ok = false; }
            }

            // ---- 2. Omit-on-null, scoped to dodge the Room.Preview key collision: the settlement round-trip
            // test above (line ~583) already proves NullValueHandling.Ignore for the settlement building's
            // "Preview" key, and POI A above legitimately writes the SAME key name for its own image — so
            // neither that file nor this method's part-1 file can be used to prove the OMIT case for a POI
            // without one; a bare json.Contains("\"Preview\"") on either would be a false failure (A) or an
            // ambiguous pass (which "Preview" omitted?). This save is scoped to ONLY POI B — no image, no
            // interiors/settlements, no other POI — so the only field in the whole file that could possibly
            // write a "Preview" key is POI B's own (null) one, making a plain substring check unambiguous. ---
            string minPath = Path.Combine(Application.temporaryCachePath, "poi_preview_nullkey_selftest.json");
            var minPois = new List<PoiData> { new PoiData { Type = PoiType.Village, Name = "Деревня без превью", OwnerCellId = 1 } };
            ProjectSerializer.Save(minPath, genParams, new List<VoronoiCell>(), minPois, notes,
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());
            string minJson = File.ReadAllText(minPath);
            try { File.Delete(minPath); } catch { }

            if (minJson.Contains("\"Preview\""))
            { Debug.LogError("FAIL poi preview: a null POI Preview was written as a key — NullValueHandling.Ignore is not taking effect"); ok = false; }

            Debug.Log(ok
                ? "Self-Test POI Preview Round-Trip: PASS"
                : "Self-Test POI Preview Round-Trip: FAIL — see field checks in SelfTestPoiPreviewRoundTrip");
        }

        [ContextMenu("Self-Test: Project Corrupt File Handling")]
        public void SelfTestCorruptFile()
        {
            string path = Path.Combine(Application.temporaryCachePath, "project_corrupt_selftest.json");
            File.WriteAllText(path, "{ this is not valid json ][");

            var result = ProjectSerializer.Load(path);
            File.Delete(path);

            bool ok = !result.Success && !string.IsNullOrEmpty(result.ErrorMessage);
            Debug.Log(ok
                ? "Self-Test Project Corrupt File Handling: PASS"
                : $"Self-Test Project Corrupt File Handling: FAIL (Success={result.Success}, ErrorMessage='{result.ErrorMessage}')");
        }

        [ContextMenu("Self-Test: Region Labels Round-Trip")]
        public void SelfTestRegionLabelsRoundTrip()
        {
            var regionLabels = new System.Collections.Generic.List<WorldGen.Rendering.RegionLabels.RegionLabelData>
            {
                new WorldGen.Rendering.RegionLabels.RegionLabelData
                { Text = "Мои Земли", WorldPosition = new System.Numerics.Vector2(12.5f, 34.5f),
                  SeedFamily = WorldGen.Rendering.MapRaster.BiomeFamily.Forest },
            };
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "region_labels_selftest.json");
            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10f, Height = 10f },
                new System.Collections.Generic.List<VoronoiCell>(),
                new System.Collections.Generic.List<PoiData>(),
                new NotesDocument(), regionLabels, new System.Collections.Generic.List<RegionData>(),
                new System.Collections.Generic.List<InteriorData>());
            var result = ProjectSerializer.Load(path);
            // old-save compat: a JSON with no RegionLabels field -> empty list (not null).
            string legacy = System.IO.File.ReadAllText(path).Replace("\"RegionLabels\"", "\"RegionLabelsRenamed\"");
            System.IO.File.WriteAllText(path, legacy);
            var legacyResult = ProjectSerializer.Load(path);
            System.IO.File.Delete(path);

            bool ok = result.Success
                && result.RegionLabels.Count == 1
                && result.RegionLabels[0].Text == "Мои Земли"
                && result.RegionLabels[0].WorldPosition == new System.Numerics.Vector2(12.5f, 34.5f)
                && legacyResult.Success && legacyResult.RegionLabels != null && legacyResult.RegionLabels.Count == 0;
            Debug.Log(ok ? "Self-Test Region Labels Round-Trip: PASS" : "Self-Test Region Labels Round-Trip: FAIL");
        }

        [ContextMenu("Self-Test: Legacy BiomeOverride Migration")]
        public void SelfTestLegacyBiomeOverrideMigration()
        {
            // Savanna == BiomeMatrix.Get(3,2). Its enum ordinal (int) is what v1 saved.
            int savannaOrdinal = (int)Biome.Savanna;
            string json =
                "{ \"FormatVersion\": 1, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [ { \"Id\": 0, \"Height\": 0.2, \"Temperature\": 0.1, \"Humidity\": 0.1, " +
                $"\"BiomeOverride\": {savannaOrdinal} }} ], \"Pois\": [], \"RegionLabels\": [] }}";
            string path = Path.Combine(Application.temporaryCachePath, "migration_test.dndproj");
            File.WriteAllText(path, json);

            var res = ProjectSerializer.Load(path);
            var rep = BiomeMatrix.RepresentativeClimate(Biome.Savanna).Value;
            var c = res.Cells[0];
            bool ok = res.Success
                   && Mathf.Approximately(c.TemperatureOverride ?? -1f, BiomeMatrix.LevelCenter(rep.t))
                   && Mathf.Approximately(c.MoistureOverride ?? -1f, BiomeMatrix.LevelCenter(rep.m));
            // After a classify pass the derived biome is Savanna on flat land.
            CellOverrideService.ElevationTempDrop = 0.4f;
            CellOverrideService.ClassifyAll(res.Cells, 0f);
            ok &= c.Biome == Biome.Savanna;
            Debug.Log(ok ? "Self-Test Legacy Migration: PASS" : "Self-Test Legacy Migration: FAIL");
        }

        [ContextMenu("Self-Test: Regions Round-Trip")]
        public void SelfTestRegionsRoundTrip()
        {
            var genParams = new GenerationParams { Seed = 7, Width = 10f, Height = 10f };
            var cellA = new VoronoiCell(0, new System.Numerics.Vector2(1f, 1f)) { RegionId = 0 };
            var cellB = new VoronoiCell(1, new System.Numerics.Vector2(2f, 2f)) { RegionId = 1 };
            var cells = new List<VoronoiCell> { cellA, cellB };
            var regions = new List<RegionData>
            {
                new RegionData(0, "Вэлдрим", new Color(0.2f, 0.4f, 0.6f, 1f)),
                new RegionData(1, "Каэрхолд", new Color(0.8f, 0.1f, 0.3f, 1f)),
            };

            string path = Path.Combine(Application.temporaryCachePath, "regions_roundtrip_selftest.json");
            ProjectSerializer.Save(path, genParams, cells, new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), regions, new List<InteriorData>());
            var result = ProjectSerializer.Load(path);
            File.Delete(path);

            bool ok = result.Success && result.Regions != null && result.Regions.Count == 2;
            var r0 = result.Regions?.FirstOrDefault(r => r.Id == 0);
            var r1 = result.Regions?.FirstOrDefault(r => r.Id == 1);
            ok &= r0 != null && r0.Name == "Вэлдрим" && r0.Color == new Color(0.2f, 0.4f, 0.6f, 1f);
            ok &= r1 != null && r1.Name == "Каэрхолд" && r1.Color == new Color(0.8f, 0.1f, 0.3f, 1f);

            Debug.Log(ok
                ? "Self-Test Regions Round-Trip: PASS"
                : "Self-Test Regions Round-Trip: FAIL — see field checks in SelfTestRegionsRoundTrip");
        }

        [ContextMenu("Self-Test: Legacy Region Migration")]
        public void SelfTestLegacyRegionMigration()
        {
            var genParams = new GenerationParams { Seed = 13, Width = 10f, Height = 10f };
            // v2-style save: cells carry RegionId membership, but no RegionData metadata (regions
            // existed as pure cell membership before RegionData/RegionManager - see T1 of this arc).
            // The unassigned cell (RegionId == -1) must NOT get a synthesized region.
            var cellA = new VoronoiCell(0, new System.Numerics.Vector2(1f, 1f)) { RegionId = 0 };
            var cellB = new VoronoiCell(1, new System.Numerics.Vector2(2f, 2f)) { RegionId = 2 };
            var cellC = new VoronoiCell(2, new System.Numerics.Vector2(3f, 3f)) { RegionId = -1 };
            var cells = new List<VoronoiCell> { cellA, cellB, cellC };

            string path = Path.Combine(Application.temporaryCachePath, "legacy_region_migration_selftest.json");
            ProjectSerializer.Save(path, genParams, cells, new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData>());

            // Also simulate a genuinely old file that predates the "Regions" property entirely (not
            // just an empty array) - same idiom as SelfTestRegionLabelsRoundTrip's legacy check above.
            string missingPropertyJson = File.ReadAllText(path).Replace("\"Regions\"", "\"RegionsRenamed\"");
            File.WriteAllText(path, missingPropertyJson);

            var result = ProjectSerializer.Load(path);
            File.Delete(path);

            bool ok = result.Success && result.Regions != null && result.Regions.Count == 2; // ids 0 and 2 only, not -1
            var r0 = result.Regions?.FirstOrDefault(r => r.Id == 0);
            var r2 = result.Regions?.FirstOrDefault(r => r.Id == 2);
            ok &= r0 != null && r0.Name == RegionLabelNames.ContinentName(genParams.Seed, 0)
                              && r0.Color == WorldGen.Rendering.RegionColorPalette.GetRegionColor(0);
            ok &= r2 != null && r2.Name == RegionLabelNames.ContinentName(genParams.Seed, 2)
                              && r2.Color == WorldGen.Rendering.RegionColorPalette.GetRegionColor(2);
            ok &= result.Regions.All(r => r.Id != -1);

            Debug.Log(ok
                ? "Self-Test Legacy Region Migration: PASS"
                : "Self-Test Legacy Region Migration: FAIL — see field checks in SelfTestLegacyRegionMigration");
        }

        [ContextMenu("Self-Test: Dungeon Graph Round-Trip")]
        public void SelfTestDungeonGraphRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "dungeon_graph_roundtrip_test.dndproj");

            var lvl = new InteriorFloor { NextRoomId = 4 };
            var entrance = new Room { Id = 1, TypeId = 0, Title = "Вход", X = 0.5f, Y = 0.1f };
            var mid      = new Room { Id = 2, TypeId = 1, Title = "Зал", X = 0.5f, Y = 0.5f };
            var boss     = new Room { Id = 3, TypeId = 2, Title = "Логово", Body = "дракон", X = 0.5f, Y = 0.9f };
            boss.Portals.Add(new Portal { Kind = PortalKind.DungeonExit, Bidirectional = false, Label = "трещина" });
            mid.Portals.Add(new Portal { Kind = PortalKind.SecretDoor, TargetFloorIndex = 0, TargetRoomId = 3, Bidirectional = true });
            lvl.Rooms.Add(entrance); lvl.Rooms.Add(mid); lvl.Rooms.Add(boss);
            lvl.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            lvl.Links.Add(new Link { RoomA = 2, RoomB = 3 });
            var dungeon = new InteriorData { OwnerPoiId = "poi-xyz", Floors = { lvl } };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { dungeon });
            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            bool ok = r.Success && r.Dungeons != null && r.Dungeons.Count == 1;
            var d = r.Dungeons != null && r.Dungeons.Count == 1 ? r.Dungeons[0] : null;
            var l = d != null && d.Floors.Count == 1 ? d.Floors[0] : null;
            ok &= d != null && d.OwnerPoiId == "poi-xyz";
            ok &= l != null && l.Rooms.Count == 3 && l.Links.Count == 2 && l.NextRoomId == 4;
            var b = l?.GetRoom(3);
            ok &= b != null && b.TypeId == 2 && b.Title == "Логово" && b.Body == "дракон"
                  && b.Portals.Count == 1 && b.Portals[0].Kind == PortalKind.DungeonExit && !b.Portals[0].Bidirectional;
            var m = l?.GetRoom(2);
            ok &= m != null && m.Portals.Count == 1 && m.Portals[0].Kind == PortalKind.SecretDoor
                  && m.Portals[0].TargetFloorIndex == 0 && m.Portals[0].TargetRoomId == 3 && m.Portals[0].Bidirectional;
            var e = l?.GetRoom(1);
            ok &= e != null && e.TypeId == 0 && Mathf.Approximately(e.Y, 0.1f);

            Debug.Log(ok ? "Self-Test Dungeon Graph Round-Trip: PASS" : "Self-Test Dungeon Graph Round-Trip: FAIL");
        }

        [ContextMenu("Self-Test: Link Authored Round-Trip")]
        public void SelfTestLinkAuthoredRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "link_authored_roundtrip_test.dndproj");

            // A mixed floor: one hand-drawn («Связать») link and one generator-made link, so the round-trip
            // must tell them apart rather than round-tripping a single hardcoded value both ways.
            var lvl = new InteriorFloor { NextRoomId = 4 };
            lvl.Rooms.Add(new Room { Id = 1, TypeId = 1 });
            lvl.Rooms.Add(new Room { Id = 2, TypeId = 1 });
            lvl.Rooms.Add(new Room { Id = 3, TypeId = 1 });
            lvl.Links.Add(new Link { RoomA = 1, RoomB = 2, Authored = true });
            lvl.Links.Add(new Link { RoomA = 2, RoomB = 3, Authored = false });
            var dungeon = new InteriorData { OwnerPoiId = "poi-auth", Floors = { lvl } };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { dungeon });
            var r = ProjectSerializer.Load(path);

            bool ok = r.Success && r.Dungeons != null && r.Dungeons.Count == 1 && r.Dungeons[0].Floors.Count == 1;
            var f = ok ? r.Dungeons[0].Floors[0] : null;
            var link12 = f?.Links.Find(l => l.RoomA == 1 && l.RoomB == 2);
            var link23 = f?.Links.Find(l => l.RoomA == 2 && l.RoomB == 3);
            ok &= link12 != null && link12.Authored;
            ok &= link23 != null && !link23.Authored;

            // --- Legacy simulation: a file saved before Authored existed must load with Authored == false —
            // NOT just "false because it round-tripped false", but "false because the property is genuinely
            // absent". Same rename trick as SelfTestRegionLabelsRoundTrip/SelfTestLegacyRegionMigration above:
            // renaming the JSON key means the deserializer finds no matching member for it and keeps the C#
            // field initializer's default, exactly what an old file missing the property would produce. ---
            string legacyJson = File.ReadAllText(path).Replace("\"Authored\"", "\"AuthoredRenamed\"");
            string legacyPath = Path.Combine(Path.GetTempPath(), "link_authored_legacy_test.dndproj");
            File.WriteAllText(legacyPath, legacyJson);
            var legacyResult = ProjectSerializer.Load(legacyPath);
            try { File.Delete(path); } catch { }
            try { File.Delete(legacyPath); } catch { }

            bool legacyOk = legacyResult.Success && legacyResult.Dungeons != null && legacyResult.Dungeons.Count == 1
                && legacyResult.Dungeons[0].Floors.Count == 1;
            var legacyFloor = legacyOk ? legacyResult.Dungeons[0].Floors[0] : null;
            var legacyLink12 = legacyFloor?.Links.Find(l => l.RoomA == 1 && l.RoomB == 2);
            // On disk this link was saved Authored:true, but with the property renamed away it must come
            // back false — proving the field truly defaults rather than merely happening to match.
            ok &= legacyOk && legacyLink12 != null && !legacyLink12.Authored;

            Debug.Log(ok
                ? "Self-Test Link Authored Round-Trip: PASS"
                : "Self-Test Link Authored Round-Trip: FAIL — see field checks in SelfTestLinkAuthoredRoundTrip");
        }

        [ContextMenu("Self-Test: Old-Format Dungeons Dropped")]
        public void SelfTestOldFormatDungeonsDropped()
        {
            // A minimal FormatVersion-4 file that carries a (tile-era) dungeon. On load it must be
            // discarded, but the rest of the project (here: one POI) must still load.
            string json =
                "{ \"FormatVersion\": 4, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [], \"Pois\": [ { \"Type\": 0, \"Name\": \"Город\", \"OwnerCellId\": 0 } ], " +
                "\"Dungeons\": [ { \"OwnerPoiId\": \"poi-old\", \"Levels\": [ { \"Width\": 48, \"Height\": 48, \"Tiles\": [0,1,0], \"Chambers\": [ { \"Number\": 1, \"Title\": \"старое\" } ] } ] } ] }";
            string path = Path.Combine(Path.GetTempPath(), "dungeon_old_format_test.dndproj");
            File.WriteAllText(path, json);

            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            bool ok = r.Success
                && r.Dungeons != null && r.Dungeons.Count == 0     // dropped
                && r.Pois != null && r.Pois.Count == 1 && r.Pois[0].Name == "Город";   // rest survived
            Debug.Log(ok ? "Self-Test Old-Format Dungeons Dropped: PASS" : "Self-Test Old-Format Dungeons Dropped: FAIL");
        }

        [ContextMenu("Self-Test: Dungeon Room Size Round-Trip")]
        public void SelfTestDungeonRoomSizeRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "dungeon_size_roundtrip_test.dndproj");
            var lvl = new InteriorFloor { NextRoomId = 3 };
            lvl.Rooms.Add(new Room { Id = 1, TypeId = 0, SizeW = 4, SizeH = 3 });
            lvl.Rooms.Add(new Room { Id = 2, TypeId = 2, SizeW = 6, SizeH = 5 });
            var dungeon = new InteriorData { OwnerPoiId = "poi-sz", Floors = { lvl } };
            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { dungeon });
            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }
            bool ok = r.Success && r.Dungeons.Count == 1;
            var l = r.Dungeons.Count == 1 ? r.Dungeons[0].Floors[0] : null;
            ok &= l != null && l.GetRoom(1).SizeW == 4 && l.GetRoom(1).SizeH == 3
                  && l.GetRoom(2).SizeW == 6 && l.GetRoom(2).SizeH == 5;
            Debug.Log(ok ? "Self-Test Dungeon Room Size Round-Trip: PASS" : "Self-Test Dungeon Room Size Round-Trip: FAIL");
        }

        [ContextMenu("Self-Test: Dungeon v5 Size Migration")]
        public void SelfTestDungeonV5SizeMigration()
        {
            // A FormatVersion-5 file whose rooms predate Size (no SizeW/SizeH). On load they must get
            // the type default (Entrance 7x5, Boss 10x10, Normal 6x6 — see RoomSizing.Default), not stay 0x0.
            string json =
                "{ \"FormatVersion\": 5, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [], \"Pois\": [], \"Dungeons\": [ { \"OwnerPoiId\": \"p\", \"Levels\": [ { \"NextRoomId\": 3, " +
                "\"Rooms\": [ { \"Id\": 1, \"Type\": 0 }, { \"Id\": 2, \"Type\": 2 } ], \"Corridors\": [] } ] } ] }";
            string path = Path.Combine(Path.GetTempPath(), "dungeon_v5_size_migration.dndproj");
            File.WriteAllText(path, json);
            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }
            var l = r.Success && r.Dungeons.Count == 1 ? r.Dungeons[0].Floors[0] : null;
            bool ok = l != null
                && l.GetRoom(1).SizeW == 7 && l.GetRoom(1).SizeH == 5    // Entrance default (RoomSizing.Default(0))
                && l.GetRoom(2).SizeW == 10 && l.GetRoom(2).SizeH == 10; // Boss default (RoomSizing.Default(2))
            Debug.Log(ok ? "Self-Test Dungeon v5 Size Migration: PASS" : "Self-Test Dungeon v5 Size Migration: FAIL");
        }

        [ContextMenu("Self-Test: Battle Grid Round-Trip")]
        public void SelfTestBattleGridRoundTrip()
        {
            bool ok = true;
            string path = Path.Combine(Path.GetTempPath(), "battle_grid_roundtrip_test.dndproj");

            // ---- 1. A painted map survives save -> load, cell for cell ---------------------------------
            var buf = new GridBuffer(5, 4);
            buf.Set(3, 2, GridCell.Chasm);
            var lvl = new InteriorFloor { NextRoomId = 3 };
            lvl.Rooms.Add(new Room { Id = 1, TypeId = 1, SizeW = 6, SizeH = 6, Grid = buf.ToModel() });
            lvl.Rooms.Add(new Room { Id = 2, TypeId = 1, SizeW = 6, SizeH = 6 });
            var dungeon = new InteriorData { OwnerPoiId = "poi-grid", Floors = { lvl } };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { dungeon });
            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            if (!r.Success || r.Dungeons.Count != 1)
            { Debug.LogError("FAIL grid save: the project did not load back with its one interior"); ok = false; }
            else
            {
                var back = r.Dungeons[0].Floors[0];
                var reborn = GridBuffer.FromModel(back.GetRoom(1).Grid);
                if (reborn == null)
                { Debug.LogError("FAIL grid save: room 1's Grid did not survive the round trip"); ok = false; }
                else if (reborn.Get(3, 2) != GridCell.Chasm)
                { Debug.LogError($"FAIL grid save: (3,2) came back as {reborn.Get(3, 2)}, want Chasm"); ok = false; }
                else if (reborn.Width != 5 || reborn.Height != 4)
                { Debug.LogError($"FAIL grid save: dimensions came back {reborn.Width}x{reborn.Height}, want 5x4"); ok = false; }

                // ---- 2. A room WITHOUT a map comes back with none ---------------------------------------
                // This is what keeps projects that never use battle maps from growing.
                if (back.GetRoom(2).Grid != null)
                { Debug.LogError("FAIL grid save: a room with no battle map loaded with a non-null Grid"); ok = false; }
            }

            // ---- 3. A v7 file (no Grid key anywhere) loads its rooms with Grid == null ------------------
            // Reading v7 must need NO migration: the absent key deserializes to null on its own.
            string legacyJson =
                "{ \"FormatVersion\": 7, \"GenerationParams\": { \"Seed\": 1, \"Width\": 10, \"Height\": 10 }, " +
                "\"Cells\": [], \"Pois\": [], \"Dungeons\": [ { \"OwnerPoiId\": \"p\", \"Levels\": [ { \"NextRoomId\": 2, " +
                "\"Rooms\": [ { \"Id\": 1, \"Type\": 1, \"SizeW\": 6, \"SizeH\": 6 } ], \"Corridors\": [] } ] } ] }";
            string legacyPath = Path.Combine(Path.GetTempPath(), "battle_grid_v7_test.dndproj");
            File.WriteAllText(legacyPath, legacyJson);
            var lr = ProjectSerializer.Load(legacyPath);
            try { File.Delete(legacyPath); } catch { }

            if (!lr.Success || lr.Dungeons.Count != 1)
            { Debug.LogError("FAIL grid save: the v7 file did not load"); ok = false; }
            else if (lr.Dungeons[0].Floors[0].GetRoom(1).Grid != null)
            { Debug.LogError("FAIL grid save: a v7 room without the Grid key loaded with a non-null Grid"); ok = false; }
            else if (!string.IsNullOrEmpty(lr.WarningMessage))
            { Debug.LogError($"FAIL grid save: loading a v7 file warned '{lr.WarningMessage}' — only NEWER files may warn"); ok = false; }

            if (ok) Debug.Log("Battle Grid Round-Trip: PASS");
        }

        [ContextMenu("Self-Test: Settlement Round-Trip")]
        public void SelfTestSettlementRoundTrip()
        {
            bool ok = true;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "settlement_roundtrip_test.dndproj");

            var floor = new InteriorFloor { NextRoomId = 4 };
            floor.Wall = WallContour.Rounded(1, 0.5f, 0.5f, 0.3f, 8, 0.1f);
            floor.Rooms.Add(new Room { Id = 1, TypeId = 0, X = 0.8f, Y = 0.5f });                          // gate
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = 0.5f, Y = 0.5f, Preview = new byte[]{9,8,7} }); // building w/ image
            floor.Rooms.Add(new Room { Id = 3, TypeId = 1, X = 0.45f, Y = 0.55f });                        // building no image
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            var town = new InteriorData { OwnerPoiId = "poi-town", Kind = InteriorKind.Settlement, Floors = { floor } };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new System.Collections.Generic.List<VoronoiCell>(), new System.Collections.Generic.List<PoiData>(),
                new NotesDocument(), new System.Collections.Generic.List<RegionLabelData>(),
                new System.Collections.Generic.List<RegionData>(),
                new System.Collections.Generic.List<InteriorData> { town });
            var r = ProjectSerializer.Load(path);
            try { System.IO.File.Delete(path); } catch { }

            if (!r.Success || r.Dungeons.Count != 1)
            { Debug.LogError("FAIL settlement save: the town did not load back"); ok = false; }
            else
            {
                var f = r.Dungeons[0].Floors[0];
                if (r.Dungeons[0].Kind != InteriorKind.Settlement)
                { Debug.LogError($"FAIL settlement save: Kind came back {r.Dungeons[0].Kind}, want Settlement"); ok = false; }
                if (f.Wall == null || !f.Wall.IsClosedSane())
                { Debug.LogError("FAIL settlement save: the wall did not survive the round trip"); ok = false; }
                var b2 = f.Rooms.Find(x => x.Id == 2);
                var b3 = f.Rooms.Find(x => x.Id == 3);
                if (b2 == null || b2.Preview == null || b2.Preview.Length != 3 || b2.Preview[0] != 9)
                { Debug.LogError("FAIL settlement save: building 2's Preview bytes were lost"); ok = false; }
                if (b3 != null && b3.Preview != null)
                { Debug.LogError("FAIL settlement save: building 3 (no image) loaded with a non-null Preview"); ok = false; }
            }

            // ---- 2. Null keys are OMITTED from the file, not merely null-safe on load -------------------
            // The assertions above pass with or WITHOUT [JsonProperty(NullValueHandling.Ignore)] on Wall/
            // Preview: a serialized "Wall": null / "Preview": null round-trips to null just as well as an
            // absent key, and the town above always has a non-null Wall, so neither Ignore attribute is
            // actually exercised by scenario 1. This proves the "a project with no settlements/images does
            // not grow in size" contract directly: a minimal, wall-less (open village — commit 7ec6185) town
            // whose only building has no Preview must not write EITHER key at all. Confirmed no global
            // NullValueHandling on ProjectSerializer.BuildSettings and no camelCase contract resolver
            // (PascalCase keys, same as every other self-test's json.Contains/Replace checks in this file),
            // so a literal "\"Wall\"" / "\"Preview\"" substring check is exactly what a leaked key looks like.
            string minPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "settlement_nullkeys_selftest.json");
            var minFloor = new InteriorFloor { NextRoomId = 2 };
            minFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f });   // building, Preview stays null
            // minFloor.Wall is left null — a legitimate wall-less village, not an omission.
            var village = new InteriorData { OwnerPoiId = "poi-village", Kind = InteriorKind.Settlement, Floors = { minFloor } };

            ProjectSerializer.Save(minPath, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new System.Collections.Generic.List<VoronoiCell>(), new System.Collections.Generic.List<PoiData>(),
                new NotesDocument(), new System.Collections.Generic.List<RegionLabelData>(),
                new System.Collections.Generic.List<RegionData>(),
                new System.Collections.Generic.List<InteriorData> { village });
            string minJson = System.IO.File.ReadAllText(minPath);
            try { System.IO.File.Delete(minPath); } catch { }

            if (minJson.Contains("\"Wall\""))
            { Debug.LogError("FAIL settlement save: a null Wall was written as a key — NullValueHandling.Ignore is not taking effect"); ok = false; }
            if (minJson.Contains("\"Preview\""))
            { Debug.LogError("FAIL settlement save: a null Preview was written as a key — NullValueHandling.Ignore is not taking effect"); ok = false; }

            Debug.Log(ok ? "Settlement Round-Trip: PASS" : "Settlement Round-Trip: FAIL");
        }

        [ContextMenu("Self-Test: Settlement Fields Round-Trip")]
        public void SelfTestSettlementFieldsRoundTrip()
        {
            bool ok = true;
            string path = Path.Combine(Path.GetTempPath(), "settlement_fields_roundtrip_test.dndproj");

            // ---- 1. A mix of active/dummy buildings + SettlementParams survive the round trip ------------
            var floor = new InteriorFloor { NextRoomId = 4 };
            floor.SettlementParams = new SettlementParams { TargetBuildings = 20, ActiveBuildings = 5, HasWall = true };
            floor.Rooms.Add(new Room { Id = 1, TypeId = 0, X = 0.8f, Y = 0.5f });                    // gate
            floor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = 0.5f, Y = 0.5f });                    // active (IsDummy default false)
            floor.Rooms.Add(new Room { Id = 3, TypeId = 1, X = 0.45f, Y = 0.55f, IsDummy = true });  // dummy
            floor.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            var town = new InteriorData { OwnerPoiId = "poi-town-fields", Kind = InteriorKind.Settlement, Floors = { floor } };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { town });
            var r = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            if (!r.Success || r.Dungeons.Count != 1)
            { Debug.LogError("FAIL settlement fields: the town did not load back"); ok = false; }
            else
            {
                var f = r.Dungeons[0].Floors[0];
                var active = f.Rooms.Find(x => x.Id == 2);
                var dummy = f.Rooms.Find(x => x.Id == 3);
                if (active == null)
                { Debug.LogError("FAIL settlement fields: active building (room id 2) did not load back"); ok = false; }
                else if (active.IsDummy)
                { Debug.LogError("FAIL settlement fields: room 2 (saved active) loaded IsDummy=true, want false"); ok = false; }
                if (dummy == null)
                { Debug.LogError("FAIL settlement fields: dummy building (room id 3) did not load back"); ok = false; }
                else if (!dummy.IsDummy)
                { Debug.LogError("FAIL settlement fields: room 3 (saved dummy) loaded IsDummy=false, want true"); ok = false; }

                if (f.SettlementParams == null)
                { Debug.LogError("FAIL settlement fields: floor.SettlementParams did not survive the round trip (null)"); ok = false; }
                else if (f.SettlementParams.TargetBuildings != 20 || f.SettlementParams.ActiveBuildings != 5)
                { Debug.LogError($"FAIL settlement fields: SettlementParams came back {f.SettlementParams.TargetBuildings}/{f.SettlementParams.ActiveBuildings}, want 20/5"); ok = false; }
                else if (f.SettlementParams.HasWall != true)
                { Debug.LogError($"FAIL settlement fields: SettlementParams.HasWall came back {f.SettlementParams.HasWall}, want true"); ok = false; }
            }

            // ---- 2. Reusing the settlement round-trip's null-key text scenario: a floor whose only building
            // is active (IsDummy == false, the default) and whose SettlementParams is null must OMIT BOTH keys
            // from the JSON text entirely — the DefaultValueHandling.Ignore (IsDummy) / NullValueHandling.Ignore
            // (SettlementParams) proof, not merely "loads back correctly either way". Same idiom and same
            // reasoning as SelfTestSettlementRoundTrip's Wall/Preview check above: PascalCase keys, no global
            // NullValueHandling/DefaultValueHandling on ProjectSerializer.BuildSettings, so a literal
            // "\"IsDummy\""/"\"SettlementParams\"" substring check is exactly what a leaked key looks like. -----
            string minPath = Path.Combine(Path.GetTempPath(), "settlement_fields_nullkeys_selftest.json");
            var minFloor = new InteriorFloor { NextRoomId = 2 };
            minFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f });  // active; IsDummy stays false (default)
            // minFloor.SettlementParams is left null.
            var village = new InteriorData { OwnerPoiId = "poi-village-fields", Kind = InteriorKind.Settlement, Floors = { minFloor } };

            ProjectSerializer.Save(minPath, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { village });
            string minJson = File.ReadAllText(minPath);
            try { File.Delete(minPath); } catch { }

            if (minJson.Contains("\"IsDummy\""))
            { Debug.LogError("FAIL settlement fields: an active (IsDummy==false) building wrote the IsDummy key — DefaultValueHandling.Ignore is not taking effect"); ok = false; }
            if (minJson.Contains("\"SettlementParams\""))
            { Debug.LogError("FAIL settlement fields: a null SettlementParams was written as a key — NullValueHandling.Ignore is not taking effect"); ok = false; }

            // ---- 3. HasWall omit-on-false, scoped so SettlementParams itself IS present (unlike part 2's
            // null SettlementParams, which trivially omits every nested key including HasWall) — this is the
            // only way to prove DefaultValueHandling.Ignore on HasWall specifically. Repo-grep
            // ('"HasWall"\|HasWall' under Assets/WorldGen, see task-4-report.md) found only
            // SettlementConfig.HasWall (a Generation-side, non-serialized runtime knob) sharing the name, so
            // a literal "\"HasWall\"" substring check is exactly what a leaked key looks like. -------
            string wallPath = Path.Combine(Path.GetTempPath(), "settlement_fields_haswall_default_selftest.json");
            var wallFloor = new InteriorFloor { NextRoomId = 2 };
            wallFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f });  // active
            wallFloor.SettlementParams = new SettlementParams { TargetBuildings = 8, ActiveBuildings = 8 };  // HasWall left false (default)
            var openTown = new InteriorData { OwnerPoiId = "poi-open-fields", Kind = InteriorKind.Settlement, Floors = { wallFloor } };

            ProjectSerializer.Save(wallPath, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { openTown });
            string wallJson = File.ReadAllText(wallPath);
            try { File.Delete(wallPath); } catch { }

            if (wallJson.Contains("\"HasWall\""))
            { Debug.LogError("FAIL settlement fields: a default (false) HasWall was written as a key — DefaultValueHandling.Ignore is not taking effect"); ok = false; }

            Debug.Log(ok ? "Settlement Fields Round-Trip: PASS" : "Settlement Fields Round-Trip: FAIL");
        }

        [ContextMenu("Self-Test: Interior OwnerRoomId Round-Trip")]
        public void SelfTestInteriorOwnerRoomIdRoundTrip()
        {
            bool ok = true;
            string path = Path.Combine(Path.GetTempPath(), "interior_ownerroomid_roundtrip_test.dndproj");

            // ---- 1. Byte-integrity: an interior hierarchy with both a town (OwnerRoomId = 0, unset) and
            // a building interior (OwnerRoomId = 4) round-trips both values intact. The same file carries
            // both values, so this proves VALUES only — the JSON-text omission check (part 2 below) needs
            // a narrower file to prove the key is truly omitted when default. -------
            var townFloor = new InteriorFloor { NextRoomId = 3 };
            townFloor.Rooms.Add(new Room { Id = 1, TypeId = 0, X = 0.5f, Y = 0.5f });     // gate
            townFloor.Rooms.Add(new Room { Id = 2, TypeId = 1, X = 0.6f, Y = 0.5f });     // building node
            townFloor.Links.Add(new Link { RoomA = 1, RoomB = 2 });
            var town = new InteriorData { OwnerPoiId = "poi-settlement", Kind = InteriorKind.Settlement, Floors = { townFloor } };

            var bldFloor = new InteriorFloor { NextRoomId = 2 };
            bldFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f });
            var building = new InteriorData { OwnerPoiId = "poi-settlement", OwnerRoomId = 4, Kind = InteriorKind.Building, Floors = { bldFloor } };

            var interiors = new List<InteriorData> { town, building };

            ProjectSerializer.Save(path, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), interiors);
            var result = ProjectSerializer.Load(path);
            try { File.Delete(path); } catch { }

            if (!result.Success || result.Dungeons.Count != 2)
            { Debug.LogError("FAIL interior ownerroomid: the project did not load back with both interiors"); ok = false; }
            else
            {
                var loadedTown = result.Dungeons.FirstOrDefault(d => d.OwnerRoomId == 0);
                var loadedBldg = result.Dungeons.FirstOrDefault(d => d.OwnerRoomId == 4);
                if (loadedTown == null || loadedTown.OwnerPoiId != "poi-settlement" || loadedTown.Kind != InteriorKind.Settlement)
                { Debug.LogError("FAIL interior ownerroomid: town (OwnerRoomId=0) did not load back correctly"); ok = false; }
                if (loadedBldg == null || loadedBldg.OwnerPoiId != "poi-settlement" || loadedBldg.Kind != InteriorKind.Building)
                { Debug.LogError("FAIL interior ownerroomid: building (OwnerRoomId=4) did not load back correctly"); ok = false; }
            }

            // ---- 2. Omit-on-zero, scoped to dodge any other interior's OwnerRoomId in the file:
            // the round-trip above proves NullValueHandling for the hierarchy, but this interior also has
            // Kind and other fields. To prove the OMIT case unambiguously (that a 0/default OwnerRoomId
            // is truly absent from JSON, not merely null-safe on load), this save is scoped to ONLY a town
            // with OwnerRoomId unset (default 0) — no other interiors with non-zero OwnerRoomId, so the
            // only field in the whole file that could possibly write an "OwnerRoomId" key is the town's
            // own (default 0) one, making a plain substring check unambiguous. DefaultValueHandling.Ignore
            // means the default value (0) should not serialize at all. Confirmed no global DefaultValueHandling
            // on ProjectSerializer.BuildSettings and PascalCase keys, so a literal "\"OwnerRoomId\"" check
            // is exactly what a leaked key looks like. -------
            string minPath = Path.Combine(Path.GetTempPath(), "interior_ownerroomid_default_selftest.json");
            var minFloor = new InteriorFloor { NextRoomId = 2 };
            minFloor.Rooms.Add(new Room { Id = 1, TypeId = 1, X = 0.5f, Y = 0.5f });
            var minTown = new InteriorData { OwnerPoiId = "poi-solo", Kind = InteriorKind.Settlement, Floors = { minFloor } };

            ProjectSerializer.Save(minPath, new GenerationParams { Seed = 1, Width = 10, Height = 10 },
                new List<VoronoiCell>(), new List<PoiData>(), new NotesDocument(),
                new List<RegionLabelData>(), new List<RegionData>(), new List<InteriorData> { minTown });
            string minJson = File.ReadAllText(minPath);
            try { File.Delete(minPath); } catch { }

            if (minJson.Contains("\"OwnerRoomId\""))
            { Debug.LogError("FAIL interior ownerroomid: a default (0) OwnerRoomId was written as a key — DefaultValueHandling.Ignore is not taking effect"); ok = false; }

            Debug.Log(ok
                ? "Self-Test Interior OwnerRoomId Round-Trip: PASS"
                : "Self-Test Interior OwnerRoomId Round-Trip: FAIL — see field checks in SelfTestInteriorOwnerRoomIdRoundTrip");
        }
    }
}
