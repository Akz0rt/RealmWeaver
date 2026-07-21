using System;
using System.Collections.Generic;
using WorldGen.Generation;

// Offline driver for the F2 packer work — see f2-harness.csproj for usage.
static class Program
{
    static void Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "help";
        switch (cmd)
        {
            case "selftests": SelfTests(); break;
            case "sweep": Sweep.RunPacks(); Console.WriteLine(); Sweep.RunCaps(); break;
            case "packs": Sweep.RunPacks(); break;
            case "caps": Sweep.RunCaps(); break;
            case "perf": Perf.Run(); break;
            case "design": Design.Run(); break;
            case "hunt": Design.Hunt(); break;
            case "huntmut": Design.HuntMutant(args.Length > 1 ? args[1] : ""); break;
            case "mutants": Mutants.Run(); break;
            case "lobecap": Lobe.Run(); break;
            case "optcheck": OptCheck.Run(); break;
            case "capmemo": CapMemoCheck.Run(); break;
            default:
                Console.WriteLine("usage: dotnet run -c Release -- <cmd>   where cmd is one of:");
                Console.WriteLine("  selftests  the real Editor self-test suites, compiled from Assets/");
                Console.WriteLine("  sweep      three-variant corpus sweep: old packer / spread-only / max (also: packs, caps)");
                Console.WriteLine("  mutants    non-vacuity check: the suite re-run against each one-rule-removed packer");
                Console.WriteLine("  perf       MaxRoomsPackable + regen timings on realistic floor-0 contours");
                Console.WriteLine("  lobecap    the «из N» cap on F4's L-shaped contour, per packer variant");
                Console.WriteLine("  optcheck   F4's two fill-sweep skips vs the same pipelines at e409a9c, position by position");
                Console.WriteLine("  capmemo    I-1: mutation ladder against a live probed-cap memo + a TypeId-dropped negative control");
                Console.WriteLine("  design     dump every self-test fixture under every variant and mutant");
                Console.WriteLine("  hunt       search for fixtures where the compact and spread runs disagree");
                Console.WriteLine("  huntmut <anchor-outer|link-pref|tight-bounds>   search for mutant-discriminating fixtures");
                break;
        }
    }

    // Runs the REAL Editor self-test suites (the [ContextMenu] methods), compiled from Assets/.
    static void SelfTests()
    {
        var t = new WorldGen.Rendering.CompactLayoutSelfTests();
        t.SelfTestCompact();
        t.SelfTestNudgeOffOverlaps();
        t.SelfTestFloorFootprint();
        t.SelfTestNewRoomPlacement();
        t.SelfTestColumnPacking();
        var b = new WorldGen.Rendering.BuildingGeneratorSelfTests();
        b.SelfTestBuilding();
        b.SelfTestFloorRemoval();
        b.SelfTestDragSettleOrdering();
        b.SelfTestAuthoredLinkFlag();
        new WorldGen.Rendering.DungeonGraphSelfTests().SelfTestDungeonUnaffectedByRewire();
        var battle = new WorldGen.Rendering.BattleGridSelfTests();
        battle.SelfTestCodec();
        battle.SelfTestGenerator();
        battle.SelfTestDoors();
        Console.WriteLine(UnityEngine.Debug.Errors == 0
            ? "EDITOR SELF-TESTS: NO ERRORS"
            : $"EDITOR SELF-TESTS: {UnityEngine.Debug.Errors} ERRORS");
        Environment.ExitCode = UnityEngine.Debug.Errors == 0 ? 0 : 1;
    }
}
