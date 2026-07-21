using System;
namespace Newtonsoft.Json
{
    public enum NullValueHandling { Include, Ignore }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonPropertyAttribute : Attribute { public JsonPropertyAttribute() {} public JsonPropertyAttribute(string n) {} public string PropertyName; public NullValueHandling NullValueHandling; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonIgnoreAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonConverterAttribute : Attribute { public JsonConverterAttribute(Type t) {} }
}
