using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Offline driver for the notes document layer — see notes-harness.csproj for usage.
static class Program
{
    static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "help";
        switch (cmd)
        {
            case "selftests": return SelfTests();
            default:
                Console.WriteLine("usage: dotnet run -c Release -- <cmd>   where cmd is one of:");
                Console.WriteLine("  selftests  every NotesDocOps self-test, compiled from Assets/");
                return 2;
        }
    }

    // Runs the REAL Editor self-test methods (the [ContextMenu] ones) by REFLECTION rather than a
    // hand-maintained call list. A hand-maintained list has one failure mode that matters: a self-test that
    // exists, looks like coverage in review, and is never actually invoked. Reflecting over every public
    // parameterless method whose name starts with "SelfTest" makes that impossible. Sorted by name so the
    // output order is stable between runs.
    static int SelfTests()
    {
        // Every *SelfTests type in the notes or workspace-shell namespace, not a hand-listed set — adding a
        // suite must never require remembering to register it here.
        var suiteNamespaces = new HashSet<string> { "WorldGen.Notes.Data", "WorldGen.Workspace.Data" };
        var suites = typeof(WorldGen.Notes.Data.NotesDocOps).Assembly
            .GetTypes()
            .Where(t => t.Namespace != null && suiteNamespaces.Contains(t.Namespace)
                        && t.Name.EndsWith("SelfTests", StringComparison.Ordinal)
                        && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var methods = new List<(Type Suite, MethodInfo Method)>();
        foreach (var suite in suites)
            foreach (var m in suite.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name.StartsWith("SelfTest", StringComparison.Ordinal)
                                     && m.GetParameters().Length == 0
                                     && m.ReturnType == typeof(void))
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
                methods.Add((suite, m));

        if (methods.Count == 0)
        {
            Console.WriteLine("NOTES SELF-TESTS: no SelfTest* methods found — an empty suite is a failure, not a pass.");
            return 1;
        }

        foreach (var (suite, m) in methods)
        {
            try { m.Invoke(Activator.CreateInstance(suite), null); }
            catch (TargetInvocationException ex)
            {
                UnityEngine.Debug.LogError($"{suite.Name}.{m.Name} THREW: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            }
        }

        Console.WriteLine();
        if (UnityEngine.Debug.Errors > 0)
        {
            Console.WriteLine($"NOTES SELF-TESTS: {UnityEngine.Debug.Errors} ERROR(S) across {methods.Count} suite method(s)");
            return 1;
        }

        Console.WriteLine($"NOTES SELF-TESTS: NO ERRORS ({methods.Count} suite method(s))");
        return 0;
    }
}
