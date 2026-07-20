using System;
namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonPropertyAttribute : Attribute { public JsonPropertyAttribute() {} public JsonPropertyAttribute(string n) {} public string PropertyName; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonIgnoreAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonConverterAttribute : Attribute { public JsonConverterAttribute(Type t) {} }
}
