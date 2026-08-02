using System;

namespace UnityEngine
{
    public class MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ContextMenuAttribute : Attribute { public ContextMenuAttribute(string s) { } }
    public static class Debug
    {
        public static int Errors;
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogError(object o) { Errors++; Console.WriteLine("ERR: " + o); }
        public static void LogWarning(object o) => Console.WriteLine("WARN: " + o);
    }
    /// <summary>NOT USED BY ANY SYNCED SOURCE — it exists so the gate can FAIL. UnityEngine really does
    /// declare a Vector2, and the pure notes layer really does use System.Numerics.Vector2, so a file that
    /// imports both namespaces and writes a bare `Vector2` is ambiguous (CS0104) in the Editor. While this
    /// stub had no Vector2 at all, that file compiled clean here and broke the real build — a self-test suite
    /// shipped green through the gate and failed on the DM's machine. A stub that is MISSING a type does not
    /// merely fail to test it; it actively hides a conflict the real assembly would raise.</summary>
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static bool Approximately(float a, float b)
            => Math.Abs(b - a) < Math.Max(1e-6f * Math.Max(Math.Abs(a), Math.Abs(b)), 1.121039E-44f * 8f);
    }
}
