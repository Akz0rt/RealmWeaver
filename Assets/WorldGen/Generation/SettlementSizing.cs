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
    /// WHERE THE RADII COME FROM (PROVISIONAL — Task D replaces them with MEASURED values). A town's
    /// buildable cell budget is its interior disk MINUS what the streets take: the one-cell ring plus the
    /// subdivision strips scale with the perimeter, and the fill then converts what is left into houses at
    /// less than 1 house per cell (SettlementBlocks.PickSize rolls multi-cell rects). The spec's fit of that
    /// shape is
    ///
    ///     0.63 · (π·r² − 7.9·r) = target        [r in CELLS]
    ///
    /// which the three radii below solve for 20 / 50 / 120:
    ///     r = 4.7  →  0.63 · (69.4 − 37.1) =  20.3
    ///     r = 6.4  →  0.63 · (128.7 − 50.6) =  49.2
    ///     r = 9.1  →  0.63 · (260.2 − 71.9) = 118.6
    /// These are a MODEL, not a measurement: `targetBuildings` is advisory all the way down (see
    /// SettlementBlocks' class doc — the achieved count is whatever the geometry yields), so Task D sweeps
    /// seeds and re-derives both this column and GuaranteedMinBuildings from what actually comes out. Do not
    /// tune them by eye in the meantime.
    ///
    /// THE GUARANTEED MINIMUMS ARE LIKEWISE PROVISIONAL, at ~0.75 × target. They exist so the UI can promise
    /// something it can keep; Task D replaces each with the measured minimum over its seed sweep. The one
    /// property that is NOT provisional, and that SelfTestSizing pins, is that a guarantee is STRICTLY BELOW
    /// its target — a minimum equal to the target is precisely the lie the old TargetBuildings knob told.
    ///
    /// THE FIELD BOUND IS NOT NEGOTIABLE. WallRadiusNorm(Large) + 0.5 must stay inside the drag clamp's
    /// 0.04..0.96 (DungeonViewController.DragClampMin/Max) — that bound is what forced
    /// SettlementGenerator.BuildingCell down from 0.07 to 0.03, and SelfTestSizing fails if a later edit
    /// grows a radius past it. If a target cannot be met inside the field, the TARGET moves down.</summary>
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
                case SettlementSize.Large: return 9.1f;
                default: return 6.4f;
            }
        }

        /// <summary>The same radius in NORMALIZED 0..1 space, which is what WallContour.Rounded takes. Derived
        /// from the cell radius through the lattice pitch rather than stored separately, so the two can never
        /// disagree about how big a town is.</summary>
        public static float WallRadiusNorm(SettlementSize size) => WallRadiusCells(size) * SettlementFootprint.Pitch;

        /// <summary>How many buildings this size AIMS at. Advisory all the way down (SettlementBlocks.Generate
        /// consults it only through SizeClassFor, to decide how BIG the houses come out), which is exactly why
        /// it is no longer a knob the DM sets and can be disappointed by.</summary>
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
        /// TargetBuildings by construction (see the class doc). PROVISIONAL at ~0.75 × target until Task D
        /// replaces each with the measured minimum over a seed sweep.</summary>
        public static int GuaranteedMinBuildings(SettlementSize size)
        {
            switch (size)
            {
                case SettlementSize.Small: return 15;
                case SettlementSize.Large: return 90;
                default: return 38;
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
