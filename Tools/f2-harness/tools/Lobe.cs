using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>Task F4: the DM-facing «Комнаты: N из MAX» cap on the reported L-shaped contour, computed with
    /// the packer swapped out. BuildingGenerator.MaxRoomsPackable is hard-wired to CompactLayout, so the
    /// self-test's cap assertion (27/28's fixture) cannot be rebound to a mutant — this replicates the SAME
    /// probe loop (BuildingGenerator.MaxRoomsPackable: MaxRoomsByArea for the budget, ProbeSeeds fixed seeds,
    /// keep the best) against each variant so the "cap before / cap after" numbers in the report are measured,
    /// not asserted. The contour is byte-for-byte the self-tests' LobeContour.</summary>
    public static class Lobe
    {
        const int T = DungeonLayout.TilesPerAxis;
        const int ProbeSeeds = 10;   // BuildingGenerator.ProbeSeeds

        delegate int PackFn(InteriorFloor floor, int columnId, float cx, float cy, InteriorFloor contour, float margin);

        static Room RoomAt(int id, float tileX, float tileY, int w, int h)
            => new Room { Id = id, TypeId = 1, SizeW = w, SizeH = h, X = tileX / T, Y = tileY / T };

        // CompactLayoutSelfTests.LobeContour: arm A 8x4 at (56,62), arm B 1x12 at (60,70).
        public static InteriorFloor Contour()
        {
            var c = new InteriorFloor { NextRoomId = 3 };
            c.Rooms.Add(RoomAt(1, 56, 62, 8, 4));
            c.Rooms.Add(RoomAt(2, 60, 70, 1, 12));
            return c;
        }

        static int CapWith(PackFn pack, InteriorFloor contour, float cx, float cy, int colW, int colH)
        {
            int budget = BuildingGenerator.MaxRoomsByArea(contour, colW, colH);
            int best = 1;
            for (int s = 0; s < ProbeSeeds; s++)
            {
                var f = Sweep.StairGraph(new Random(s), budget, colW, colH);
                int n = pack(f, f.Rooms[0].Id, cx, cy, contour, FloorFootprint.ContourMargin);
                if (n > best) best = n;
            }
            return best;
        }

        public static void Run()
        {
            var contour = Contour();
            float cx = 54f, cy = 62f;
            int colW = 4, colH = 4;
            Console.WriteLine($"L contour (arm A [50.5,61.5]x[58.5,65.5] + arm B [58,62]x[62.5,77.5]), column 4x4 pinned at ({cx},{cy})");
            Console.WriteLine($"  area budget for the probe: {BuildingGenerator.MaxRoomsByArea(contour, colW, colH)}");

            var rows = new List<(string name, PackFn pack)>
            {
                ("PreReviewLayout  (the packer at dd6e3dc)", PreReviewLayout.PackAroundColumnWithinFootprint),
                ("MutNoSlide       (shipped minus the slide)", MutNoSlide.PackAroundColumnWithinFootprint),
                ("MutNoDoorBound   (slide, no overlap bound)", MutNoDoorBound.PackAroundColumnWithinFootprint),
                ("CompactLayout    (SHIPPED)", CompactLayout.PackAroundColumnWithinFootprint),
            };
            foreach (var (name, pack) in rows)
                Console.WriteLine($"  MaxRoomsPackable | {name,-42} | {CapWith(pack, contour, cx, cy, colW, colH),3}");
        }
    }
}
