using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldGen.Notes.Data;

namespace WorldGen.Persistence
{
    /// <summary>
    /// Handles the polymorphic CanvasObjectData hierarchy (NoteCardData/ImageObjectData/
    /// DrawingObjectData) via an explicit "Kind" discriminator instead of Newtonsoft's
    /// TypeNameHandling (which would embed .NET assembly-qualified type names in the file).
    ///
    /// WriteJson/ReadJson build/read every field BY HAND instead of delegating to
    /// serializer.Serialize/Populate for the object as a whole. This matters because
    /// Newtonsoft resolves which converter applies to a List&lt;CanvasObjectData&gt; element by
    /// that ELEMENT'S RUNTIME TYPE (e.g. NoteCardData) — so CanConvert (left at its default,
    /// IsAssignableFrom-based behavior; do NOT override it) must match concrete subtypes too,
    /// or this converter would silently never fire for real list elements. But that also
    /// means asking the SAME serializer to (de)serialize a CanvasObjectData-typed value from
    /// inside this converter (e.g. via JObject.FromObject(value, serializer) or
    /// serializer.Populate) would immediately re-enter this converter and recurse forever.
    /// Only individual FIELD values (string/byte[]/Vector2) are ever handed to
    /// JToken.FromObject/ToObject below — none of their types match CanvasObjectData, so
    /// that's safe.
    /// </summary>
    public class CanvasObjectDataConverter : JsonConverter<CanvasObjectData>
    {
        public override void WriteJson(JsonWriter writer, CanvasObjectData value, JsonSerializer serializer)
        {
            var obj = new JObject
            {
                ["Kind"] = KindFor(value),
                ["Id"] = value.Id,
                ["Position"] = JToken.FromObject(value.Position, serializer),
                ["Size"] = JToken.FromObject(value.Size, serializer)
            };

            switch (value)
            {
                case NoteCardData card:
                    obj["Title"] = card.Title;
                    obj["Body"] = card.Body;
                    break;
                case ImageObjectData image:
                    obj["ImageBytes"] = image.ImageBytes != null ? JToken.FromObject(image.ImageBytes) : JValue.CreateNull();
                    break;
                case DrawingObjectData drawing:
                    obj["PixelDataPng"] = drawing.PixelDataPng != null ? JToken.FromObject(drawing.PixelDataPng) : JValue.CreateNull();
                    obj["PixelWidth"] = drawing.PixelWidth;
                    obj["PixelHeight"] = drawing.PixelHeight;
                    break;
            }

            obj.WriteTo(writer);
        }

        public override CanvasObjectData ReadJson(JsonReader reader, Type objectType, CanvasObjectData existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            string kind = obj["Kind"]?.Value<string>();

            CanvasObjectData result;
            switch (kind)
            {
                case "NoteCard":
                    result = new NoteCardData
                    {
                        Title = obj["Title"]?.Value<string>() ?? "",
                        Body = obj["Body"]?.Value<string>() ?? ""
                    };
                    break;
                case "Image":
                    result = new ImageObjectData
                    {
                        ImageBytes = obj["ImageBytes"] != null && obj["ImageBytes"].Type != JTokenType.Null
                            ? obj["ImageBytes"].ToObject<byte[]>()
                            : null
                    };
                    break;
                case "Drawing":
                {
                    int pixelWidth = obj["PixelWidth"]?.Value<int>() ?? 0;
                    int pixelHeight = obj["PixelHeight"]?.Value<int>() ?? 0;
                    result = new DrawingObjectData(pixelWidth, pixelHeight)
                    {
                        PixelDataPng = obj["PixelDataPng"] != null && obj["PixelDataPng"].Type != JTokenType.Null
                            ? obj["PixelDataPng"].ToObject<byte[]>()
                            : null
                    };
                    break;
                }
                default:
                    throw new JsonSerializationException($"Unknown CanvasObjectData Kind: '{kind}'");
            }

            result.Id = obj["Id"]?.Value<string>() ?? result.Id;
            result.Position = obj["Position"] != null ? obj["Position"].ToObject<System.Numerics.Vector2>() : default;
            result.Size = obj["Size"] != null ? obj["Size"].ToObject<System.Numerics.Vector2>() : result.Size;
            return result;
        }

        static string KindFor(CanvasObjectData value) => value switch
        {
            NoteCardData => "NoteCard",
            ImageObjectData => "Image",
            DrawingObjectData => "Drawing",
            _ => throw new JsonSerializationException($"Unknown CanvasObjectData subtype: {value.GetType()}")
        };
    }
}
