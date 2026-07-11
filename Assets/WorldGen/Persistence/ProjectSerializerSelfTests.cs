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
            ProjectSerializer.Save(path, genParams, cells, pois, notes, new List<RegionLabelData>());
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
            ProjectSerializer.Save(path, genParams, cells, pois, notes, new List<RegionLabelData>());
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
            ProjectSerializer.Save(newPath, genParams, cells, oldStylePois, notes, new List<RegionLabelData>());

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
                new NotesDocument(), regionLabels);
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
    }
}
