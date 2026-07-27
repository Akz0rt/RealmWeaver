using System;
namespace Newtonsoft.Json
{
    public enum NullValueHandling { Include, Ignore }
    public enum DefaultValueHandling { Include, Ignore, Populate, IgnoreAndPopulate }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonPropertyAttribute : Attribute { public JsonPropertyAttribute() {} public JsonPropertyAttribute(string n) {} public string PropertyName; public NullValueHandling NullValueHandling; public DefaultValueHandling DefaultValueHandling; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonIgnoreAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class JsonConverterAttribute : Attribute { public JsonConverterAttribute(Type t) {} }
}
