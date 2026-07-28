namespace WorldGen.Generation
{
    /// <summary>A settlement's SIZE CLASS — what the DM actually picks, replacing the exact building-count
    /// knob (SettlementParams.TargetBuildings) that the geometry could never honour. Stored on
    /// SettlementParams.Size as its int (0/1/2); a pre-v11 save's count is bucketed into one of these by
    /// <see cref="SettlementSizing.FromLegacyTarget"/> at load.</summary>
    public enum SettlementSize { Small = 0, Medium = 1, Large = 2 }

    /// <summary>THE ONE TABLE a settlement's scale comes from. Everything else — the placement contour's
    /// radius, how many gates the wall opens, what the UI promises — derives from these five columns, so a
    /// town cannot end up bigger in one place than in another. Pure, UnityEngine-free.
    ///
    /// EVERY SWITCH HAS A `default` THAT RETURNS THE Medium VALUE. A corrupt or hand-edited save can carry
    /// any int in SettlementParams.Size, and an enum field simply takes it — so an undefined value must
    /// degrade to the middle of the table, never throw and never take the load path down with it. That is
    /// the same "a bad file degrades instead of failing the whole load" rule SettlementFootprint.Decode
    /// keeps.
    ///
    /// WHERE THE RADII COME FROM (MEASURED, Task D — the pre-Task-D doc's algebraic fit against a
    /// 0.63·(π·r²−7.9·r) model is RETIRED; that model was written for recursive subdivision and, worse,
    /// stopped predicting the frontage-street layout's actual yield once arc C.2 shipped: it under/over-shot
    /// by 4-17% at the three sizes and was never re-derived against the real algorithm). The three radii below
    /// are instead the output of SettlementBlocksSelfTests.SelfTestSizeCalibration — 200 FIXED seeds per size
    /// (seed = 1000+k, k in 0..199), run through the REAL production path (WallRadiusNorm + SettlementBlocks.
    /// Generate) — adjusted until each size's MEDIAN achieved count landed within +/-10% of its target:
    ///
    ///     Small:  r = 4.7 cells  -> median 19.0 buildings (target 20,  ratio 0.95)
    ///     Medium: r = 7.0 cells  -> median 53.0 buildings (target 50,  ratio 1.06)
    ///     Large:  r = 10.0 cells -> median 120.0 buildings (target 120, ratio 1.00)
    ///
    /// Small needed no change from its pre-Task-D value (4.7 already measured at ratio 0.95); Medium and Large
    /// both needed to grow (from 6.4 and 9.1) because the retired model's constant (0.63) no longer matches the
    /// frontage layout's actual yield at those two scales — see task-D-report.md for the full iteration table.
    /// `targetBuildings` stays advisory all the way down (SettlementBlocks' class doc — the achieved count is
    /// whatever the geometry yields): these three radii only make the MEDIAN land near target, not every seed.
    ///
    /// THE GUARANTEED MINIMUMS ARE LIKEWISE MEASURED, at floor(0.9 × the observed minimum) over the SAME
    /// 200-seed sweep at the radii above (see GuaranteedMinBuildings for the three observed minimums). The one
    /// property that was NEVER provisional, and that SelfTestSizing pins, is that a guarantee is STRICTLY BELOW
    /// its target — a minimum equal to the target is precisely the lie the old TargetBuildings knob told.
    ///
    /// THE FIELD BOUND IS NOT NEGOTIABLE. WallRadiusNorm(Large) + 0.5 must stay inside the drag clamp's
    /// 0.04..0.96 (DungeonViewController.DragClampMin/Max) — that bound is what forced
    /// SettlementGenerator.BuildingCell down from 0.07 to 0.03, and SelfTestSizing fails if a later edit
    /// grows a radius past it. Large's measured radius (10.0 cells, WallRadiusNorm 0.30) has ample headroom
    /// inside that bound (the field allows up to ~15.33 cells), so Task D did not need to lower
    /// TargetBuildings(Large) to stay inside it. If a target cannot be met inside the field, the TARGET moves
    /// down.</summary>
    public static class SettlementSizing
    {
        /// <summary>Radius of the placement contour, in LATTICE CELLS (not normalized, not tiles) — the unit
        /// the buildable-budget model above is written in, so the number stays meaningful if the pitch ever
        /// moves again.</summary>
        public static float WallRadiusCells(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return 4.7f;
                case SettlementSize.Large: return 10.0f;
                default: return 7.0f;
            }
        }

        /// <summary>The same radius in NORMALIZED 0..1 space, which is what WallContour.Rounded takes. Derived
        /// from the cell radius through the lattice pitch rather than stored separately, so the two can never
        /// disagree about how big a town is.</summary>
        public static float WallRadiusNorm(SettlementSize size) => WallRadiusCells(size) * SettlementFootprint.Pitch;

        /// <summary>How many buildings this size AIMS at. NO CALLER IN THE FILL any more (arc A, task 3
        /// replaced SizeClassFor — the fill's sole reader — with a shape palette that does not consult `size`
        /// at all), which is exactly why it is no longer a knob the DM sets and can be disappointed by. It
        /// survives as the TABLE'S STATED INTENT — what a DM reads off the label — and as the anchor
        /// SelfTestSizeCalibration's 200-seed sweep bands its achieved counts against.</summary>
        public static int TargetBuildings(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return 20;
                case SettlementSize.Large: return 120;
                default: return 50;
            }
        }

        /// <summary>How many gates a WALLED town of this size opens. A wall-less village takes none of them
        /// (SettlementGenerator.BuildFloor drops the layout's gates when HasWall is false).</summary>
        public static int GateCount(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return 2;
                case SettlementSize.Large: return 4;
                default: return 3;
            }
        }

        /// <summary>The count the UI may PROMISE: at or below this, every seed delivers. Strictly below
        /// TargetBuildings by construction (see the class doc). MEASURED (Task D): floor(0.9 x the observed
        /// minimum) over SettlementBlocksSelfTests.SelfTestSizeCalibration's 200-seed sweep (seed = 1000+k,
        /// k in 0..199) at the radii below —
        ///     Small:  observed min 14  -> floor(0.9*14) = 12
        ///     Medium: observed min 42  -> floor(0.9*42) = 37
        ///     Large:  observed min 98  -> floor(0.9*98) = 88
        /// — a 10% margin below the worst seed actually seen, not the ~0.75 x target guess this replaced. The
        /// sweep itself is the contract (SelfTestSizeCalibration asserts every one of the 600 generations meets
        /// its size's guarantee); a future radius or fill change that lowers the true minimum must fail that
        /// sweep before it can ship, which is what turns this table's promise from an assertion into a
        /// measurement.</summary>
        public static int GuaranteedMinBuildings(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return 12;
                case SettlementSize.Large: return 88;
                default: return 37;
            }
        }

        /// <summary>Bucket a PRE-v11 save's stored TargetBuildings into a size class — the v11 load migration's
        /// only use of the retired knob. The boundaries are inclusive on the low side, and they are chosen to
        /// put the two counts this tool ever SHIPPED as defaults (10 for a village, 20 for a city — see
        /// MapScreenController.SettlementDefaults) plus the older 40 where a DM would expect: a 10- or
        /// 20-building town is Small, the 40 that used to be SettlementConfig's own default is Medium.</summary>
        public static SettlementSize FromLegacyTarget(int targetBuildings)
        {
            if (targetBuildings <= 30) return SettlementSize.Small;
            if (targetBuildings <= 80) return SettlementSize.Medium;
            return SettlementSize.Large;
        }

        /// <summary>The Russian label the editor shows for this size.</summary>
        public static string Label(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return "Малый";
                case SettlementSize.Large: return "Большой";
                default: return "Средний";
            }
        }
    }
}
