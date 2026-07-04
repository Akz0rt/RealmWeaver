using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WorldGen.Generation;
using WorldGen.Notes.Data;

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
                BiomeOverride = Biome.Tundra,
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
            ProjectSerializer.Save(path, genParams, cells, pois, notes);
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
                && loadedA.BiomeOverride == Biome.Tundra
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
    }
}
