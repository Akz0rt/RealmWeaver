using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldGen.Persistence
{
    /// <summary>
    /// Serializes UnityEngine.Color as a flat {r,g,b,a} object. Without this, Newtonsoft's
    /// default contract walks Color's COMPUTED properties (.linear/.gamma/.grayscale/…), and
    /// .linear/.gamma each RETURN a Color — so it recurses forever
    /// ("Self referencing loop detected for property 'linear' … Color.linear.linear.linear…").
    /// Only the four float channels are read/written by hand (never a Color-typed value), so
    /// there is no re-entry into this converter. Color is a struct → never JSON-null in practice,
    /// but the null guard keeps a hand-edited file from throwing.
    /// </summary>
    public class ColorJsonConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            var obj = new JObject
            {
                ["r"] = value.r,
                ["g"] = value.g,
                ["b"] = value.b,
                ["a"] = value.a
            };
            obj.WriteTo(writer);
        }

        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return existingValue;
            var obj = JObject.Load(reader);
            return new Color(
                obj["r"]?.Value<float>() ?? 0f,
                obj["g"]?.Value<float>() ?? 0f,
                obj["b"]?.Value<float>() ?? 0f,
                obj["a"]?.Value<float>() ?? 1f);
        }
    }
}
