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
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static bool Approximately(float a, float b)
            => Math.Abs(b - a) < Math.Max(1e-6f * Math.Max(Math.Abs(a), Math.Abs(b)), 1.121039E-44f * 8f);
        // Реализация зеркалит исходники Unity (UnityCsReference/Runtime/Export/Math/Mathf.cs):
        // RoundToInt/CeilToInt/FloorToInt через System.Math, Lerp через Clamp01, без укорочения.
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        // Значения и реализация сверены с UnityCsReference/Runtime/Export/Math/Mathf.cs:
        // Deg2Rad — та же константа выражением (не переписана десятичным приближением), Cos — прямой
        // проброс в Math.Cos, как и в настоящем Mathf.
        public const float Deg2Rad = (float)(Math.PI * 2) / 360f;
        public static float Cos(float f) => (float)Math.Cos(f);
    }
}
