using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Offline driver for the player-prep rules data layer — see player-harness.csproj for usage.
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
                Console.WriteLine("  selftests  every player-prep data self-test, compiled from Assets/");
                return 2;
        }
    }

    // Runs the REAL Editor self-test methods (the [ContextMenu] ones) by REFLECTION rather than a
    // hand-maintained call list. A hand-maintained list has one failure mode that matters: a self-test that
    // exists, looks like coverage in review, and is never actually invoked. Reflecting over every public
    // parameterless void method whose name starts with "SelfTest" makes that impossible. Sorted by name so
    // the output order is stable between runs.
    //
    // THE GUARANTEE HAS THREE PARTS, and for most of notes-harness's arc only the first was true — which is
    // exactly the "green by absence" shape the reflection exists to prevent, hiding inside the thing
    // preventing it:
    //   1. every *SelfTests TYPE in the namespace is discovered (this method);
    //   2. every FILE in the source directory is compiled at all — sync.ps1 globs `*.cs`, and used to
    //      copy from a hand-written list, so a new suite file was silently never seen by (1);
    //   3. every SelfTest*-NAMED METHOD is either invoked or REPORTED — the filters below are narrow
    //      (public, instance, parameterless, void), and a `public static void SelfTestX()` or a
    //      `bool`-returning one used to be dropped without a word while the run still printed a plausible
    //      count. ReportUninvokedSelfTests closes that: a method that looks like a self-test and cannot be
    //      run is now a failure, not a silence.
    static int SelfTests()
    {
        // Подкладываем источник справочника: слой Data не имеет права читать файлы сам.
        // Путь считается от папки харнесса, как это делает sync.ps1.
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string rulesPath = System.IO.Path.Combine(repo, "Assets", "Resources", "Rules", "srd-5.2-ru.json");
        WorldGen.PlayerPrep.Data.RulesTextSource.Provider =
            () => System.IO.File.ReadAllText(rulesPath, System.Text.Encoding.UTF8);

        // Every *SelfTests type in the player-prep data namespace, not a hand-listed set — adding a
        // suite must never require remembering to register it here, and (since sync.ps1 globs) never
        // requires remembering to register it there either.
        var suiteNamespaces = new HashSet<string> { "WorldGen.PlayerPrep.Data" };
        // Якорь сборки — RulesIntegrity, потому что он появляется в ЭТОЙ задаче и больше никогда не
        // меняется. Ставить якорем SheetMath нельзя: его ещё нет, и харнесс не скомпилируется.
        var suites = typeof(WorldGen.PlayerPrep.Data.RulesIntegrity).Assembly
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
            Console.WriteLine("PLAYER SELF-TESTS: no SelfTest* methods found — an empty suite is a failure, not a pass.");
            return 1;
        }

        // Named like a self-test, shaped so this runner cannot call it. Reported BEFORE anything runs, and as
        // an error rather than a warning: the whole point of reflecting is that a suite method cannot exist
        // and go uninvoked, so a method this loop skips is the guarantee failing, not a style preference.
        int uninvokable = ReportUninvokedSelfTests(suites, methods);
        if (uninvokable > 0) return 1;

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
            Console.WriteLine($"PLAYER SELF-TESTS: {UnityEngine.Debug.Errors} ERROR(S) across {methods.Count} suite method(s)");
            return 1;
        }

        Console.WriteLine($"PLAYER SELF-TESTS: NO ERRORS ({methods.Count} suite method(s))");
        return 0;
    }

    /// <summary>Prints every method on a discovered suite whose NAME says "self-test" but whose SHAPE keeps
    /// SelfTests' filters from invoking it, and returns how many there were.
    ///
    /// Exists because the filters are silent by nature: `GetMethods(Public | Instance)` cannot even see a
    /// `static` method, and the parameterless/void predicate drops the rest without a word — so a suite
    /// method written `public static void SelfTestX()` or `public bool SelfTestX()` would simply not run,
    /// while the summary still printed a plausible count and the gate still said NO ERRORS. That is the same
    /// green-by-absence failure the reflection was written to make impossible, one level down.
    ///
    /// BindingFlags here are deliberately WIDER than the invocation query above (static and non-public
    /// included) — the point is to see what the narrow query cannot. Anything found is a failure the author
    /// fixes by changing the method's shape, which is why the message says what shape is required rather than
    /// just naming the method.</summary>
    static int ReportUninvokedSelfTests(List<Type> suites, List<(Type Suite, MethodInfo Method)> invoked)
    {
        var invokedSet = new HashSet<MethodInfo>(invoked.Select(x => x.Method));
        int count = 0;
        foreach (var suite in suites)
            foreach (var m in suite.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                               | BindingFlags.Instance | BindingFlags.Static)
                         .Where(m => m.Name.StartsWith("SelfTest", StringComparison.Ordinal))
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (invokedSet.Contains(m)) continue;
                count++;
                Console.WriteLine($"NOT INVOKED: {suite.Name}.{m.Name} is named like a self-test but cannot be run "
                                  + $"(public={m.IsPublic}, static={m.IsStatic}, parameters={m.GetParameters().Length}, "
                                  + $"returns={m.ReturnType.Name}) — it must be public, instance, parameterless and void.");
            }
        if (count > 0)
            Console.WriteLine($"PLAYER SELF-TESTS: {count} method(s) named SelfTest* were not invoked — failing rather than reporting a partial count.");
        return count;
    }
}
