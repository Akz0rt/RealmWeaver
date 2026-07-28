using System;
using System.Collections.Generic;

namespace WorldGen.Generation
{
    /// <summary>NON-VACUITY harness: run the REAL self-test suites (Column Packing, and Battle Grid) against
    /// copies of themselves that are bound to a MUTANT (one rule removed each) or to an older/partial
    /// pipeline. Every assertion in a suite claims to pin some rule down; a mutant that removes that rule
    /// must make the suite FAIL. A row reading "0 errors" for a mutant means the suite does not actually
    /// test what that mutant broke.</summary>
    public static class Mutants
    {
        public static void Run()
        {
            var cases = new List<(string name, string what, Action run)>
            {
                ("MutAnchorOuter",    "SeatAgainstAnyPlaced loops swapped to anchor-outer/distance-inner",
                    () => new WorldGen.MutantTests.MutAnchorOuterSelfTests().SelfTestColumnPacking()),
                ("MutNoLinkPref",     "already-linked anchors no longer tried first",
                    () => new WorldGen.MutantTests.MutNoLinkPrefSelfTests().SelfTestColumnPacking()),
                ("MutTightBounds",    "margin term dropped from the bbox pre-test",
                    () => new WorldGen.MutantTests.MutTightBoundsSelfTests().SelfTestColumnPacking()),
                ("MutTightCut",       "MaxUsefulDistance's cut-off tightened by one tile (best -= 1f)",
                    () => new WorldGen.MutantTests.MutTightCutSelfTests().SelfTestColumnPacking()),
                ("MutOneSideCut",     "MaxUsefulDistance's four-side max collapsed to a single side (up only)",
                    () => new WorldGen.MutantTests.MutOneSideCutSelfTests().SelfTestColumnPacking()),
                ("MutNoDedup",        "AddLinkIfAbsent's duplicate check removed",
                    () => new WorldGen.MutantTests.MutNoDedupSelfTests().SelfTestColumnPacking()),
                ("MutFwdDedupOnly",   "AddLinkIfAbsent keeps only the FORWARD half of the pair test",
                    () => new WorldGen.MutantTests.MutFwdDedupOnlySelfTests().SelfTestColumnPacking()),
                ("MutNoSlide",        "F4's lateral slide removed (candidate offsets collapse to {0})",
                    () => new WorldGen.MutantTests.MutNoSlideSelfTests().SelfTestColumnPacking()),
                ("MutNoDoorBound",    "the slide's DoorGapTiles shared-wall bound removed (slides to a corner kiss)",
                    () => new WorldGen.MutantTests.MutNoDoorBoundSelfTests().SelfTestColumnPacking()),
                ("MutSlideFarFirst",  "the offset ladder runs largest-magnitude-first (centre no longer wins)",
                    () => new WorldGen.MutantTests.MutSlideFarFirstSelfTests().SelfTestColumnPacking()),
                ("SpreadOnlyLayout",  "the COMPACT run deleted (spread pipeline only)",
                    () => new WorldGen.MutantTests.SpreadOnlyLayoutSelfTests().SelfTestColumnPacking()),
                ("CompactOnlyLayout", "the SPREAD run deleted (compact pipeline only)",
                    () => new WorldGen.MutantTests.CompactOnlyLayoutSelfTests().SelfTestColumnPacking()),
                ("PreReviewLayout",   "the packer exactly as reviewed at dd6e3dc",
                    () => new WorldGen.MutantTests.PreReviewLayoutSelfTests().SelfTestColumnPacking()),
                ("PreSlideLayout",    "the packer exactly as SHIPPED before F4 (e409a9c) — no lateral slide",
                    () => new WorldGen.MutantTests.PreSlideLayoutSelfTests().SelfTestColumnPacking()),

                ("MutNoRing",         "BattleGridGenerator.Generate's wall-ring condition forced to false (no ring at all)",
                    () => new WorldGen.MutantTests.MutNoRingSelfTests().SelfTestGenerator()),
                ("MutNoYFlip",        "BattleGridGenerator.AlongVertical's top-relative numerator flipped to bottom-relative",
                    () => new WorldGen.MutantTests.MutNoYFlipSelfTests().SelfTestDoors()),
                ("MutFirstTouch",     "BattleGridStroke.Paint's first-touch guard replaced with an unconditional record",
                    () => new WorldGen.MutantTests.MutFirstTouchSelfTests().SelfTestOps()),
                ("MutFillDiagonal",   "BattleGridOps.Fill gains the four diagonal Enqueue calls (leaks through a diagonal pinch)",
                    () => new WorldGen.MutantTests.MutFillDiagonalSelfTests().SelfTestOps()),

                ("MutNoInsideFilter",  "SettlementGenerator.PlaceBuildings drops the wall.Contains(cx, cy) term from the keep condition",
                    () => new WorldGen.MutantTests.MutNoInsideFilterSelfTests().SelfTestBuildings()),
                ("MutNoWallClearance", "SettlementGenerator.PlaceBuildings drops the wall.DistanceToEdge(...) >= half term",
                    () => new WorldGen.MutantTests.MutNoWallClearanceSelfTests().SelfTestBuildings()),
                ("MutGateAtCentre",    "SettlementGenerator.PointAtArcLength returns the wall centre (0.5,0.5) instead of the interpolated point",
                    () => new WorldGen.MutantTests.MutGateAtCentreSelfTests().SelfTestGates()),
                ("MutStreetsNoGrowth", "SettlementStreets.GenerateStreets' Prim-style growth loop skipped (trunks only)",
                    () => new WorldGen.MutantTests.MutStreetsNoGrowthSelfTests().SelfTestStreets()),
                ("MutStreetsNoHub",    "SettlementStreets.GenerateStreets' gate-less hub is never marked connected (growth seeds from nothing)",
                    () => new WorldGen.MutantTests.MutStreetsNoHubSelfTests().SelfTestVillage()),
                ("MutNoActiveMark",    "SettlementGenerator.BuildFloor's active/dummy marking neutered (IsDummy = false, every building stays active)",
                    () => new WorldGen.MutantTests.MutNoActiveMarkSelfTests().SelfTestActiveBuildings()),
                ("MutActiveBuildingsPrefix", "SettlementGenerator.BuildFloor's active pick reverts to a prefix of emission order (IsDummy = i >= activeCount) — the DM-reported one-corner clustering bug",
                    () => new WorldGen.MutantTests.MutActiveBuildingsPrefixSelfTests().SelfTestActiveBuildings()),
                ("MutRoadsNoAvoid",     "SettlementRoads' obstacle mask never marks a cell (roads route through houses)",
                    () => new WorldGen.MutantTests.MutRoadsNoAvoidSelfTests().SelfTestRoads()),
                ("MutRoadsNoReuse",     "SettlementRoads' reuse discount removed (branches never merge into arterial lanes)",
                    () => new WorldGen.MutantTests.MutRoadsNoReuseSelfTests().SelfTestRoadJunctions()),
                ("MutRoadsNoClearance", "SettlementRoads' obstacle-mask inflation drops its + RoadClearanceTiles term (roads may hug buildings)",
                    () => new WorldGen.MutantTests.MutRoadsNoClearanceSelfTests().SelfTestRoads()),
                ("MutRoadsNoCarveRetry", "SettlementRoads.Build's permissive retry removed (an endpoint buried under another building's rect falls straight to the diagonal centre-to-centre line)",
                    () => new WorldGen.MutantTests.MutRoadsNoCarveRetrySelfTests().SelfTestRoads()),
                ("MutRoadsNoArterials", "SettlementStreets' gate-gate arterial pass skipped (gates fall back to seed points)",
                    () => new WorldGen.MutantTests.MutRoadsNoArterialsSelfTests().SelfTestStreets()),

                ("MutNoOwnedCleanup",  "InteriorOps' single-node RemoveOwnedInteriors(all, poiId, roomId) overload always returns 0 (node deletion never cleans the owned interior)",
                    () => new WorldGen.MutantTests.MutNoOwnedCleanupSelfTests().SelfTestInteriorOps()),

                ("MutFootprintNoCorridors", "FloorFootprint.ExpandedRects never folds the routed corridor legs into the arrangement (the building contour ignores corridors)",
                    () => new WorldGen.MutantTests.MutFootprintNoCorridorsSelfTests().SelfTestBuildingFootprintCorridors()),

                ("MutUpperFloorNoGap", "BuildingGenerator's upper-floor pack bound reverted to the full drawn-contour margin (rooms pack flush against the wall again)",
                    () => new WorldGen.MutantTests.MutUpperFloorNoGapSelfTests().SelfTestBuilding()),

                ("MutFenceNoFill",  "SettlementFence.InsideFromOutsideFill's final classification collapsed to the raw pre-fill town raster (an enclosed pocket stays a literal hole)",
                    () => new WorldGen.MutantTests.MutFenceNoFillSelfTests().SelfTestFence()),
                ("MutFenceNoGates", "SettlementFence's gate-cell rasterization write neutered (a gate's centre cell is never marked town)",
                    () => new WorldGen.MutantTests.MutFenceNoGatesSelfTests().SelfTestFence()),
                ("MutFenceNoRoads", "SettlementFence's road-ribbon rasterization call skipped (a routed road never marks a cell)",
                    () => new WorldGen.MutantTests.MutFenceNoRoadsSelfTests().SelfTestFence()),

                ("MutTileGridNoFloodFill", "SettlementTileGrid's outside flood-fill result never consulted — inside collapses to the raw pre-fill occupied raster (an enclosed pocket stays a literal hole)",
                    () => new WorldGen.MutantTests.MutTileGridNoFloodFillSelfTests().SelfTestWallRing()),
                ("MutTileGridNoWallRing", "SettlementTileGrid.Build's Wall assignment neutered (0 wall cells)",
                    () => new WorldGen.MutantTests.MutTileGridNoWallRingSelfTests().SelfTestWallRing()),
                ("MutTileGridNoVoid", "SettlementTileGrid.Build's Void assignment neutered (courtyard cells stay None)",
                    () => new WorldGen.MutantTests.MutTileGridNoVoidSelfTests().SelfTestWallRing()),
                ("MutTileGridNoGates", "SettlementTileGrid.Build's gate-reclassify write neutered (a gate never turns its nearest Wall cell into Gate)",
                    () => new WorldGen.MutantTests.MutTileGridNoGatesSelfTests().SelfTestRoadsAndGates()),
                ("MutTileGridRoadIgnoresBuilding", "SettlementTileGrid.Build's road marking drops the Building/Wall precedence guard (a road overwrites whatever tile is already there); named for the Building half since the Wall half is provably unreachable and is not what this mutant is caught by",
                    () => new WorldGen.MutantTests.MutTileGridRoadIgnoresBuildingSelfTests().SelfTestRoadsAndGates()),
                ("MutGridStreetsNotSeeded", "SettlementTileGrid.Build marks the stored street cells Road but never folds them into the wall ring's occupied seed (the wall stops wrapping a street)",
                    () => new WorldGen.MutantTests.MutGridStreetsNotSeededSelfTests().SelfTestRoadsAndGates()),

                ("MutGridOneCellPerRoom", "SettlementTileGrid.Build writes only a footprint's REPRESENTATIVE cell instead of every cell (a building is a point again)",
                    () => new WorldGen.MutantTests.MutGridOneCellPerRoomSelfTests().SelfTestFootprintTiles()),
                ("MutGridExtentIgnoresFootprint", "SettlementTileGrid.Allocate folds only a footprint's REPRESENTATIVE cell into the extent (far cells fall out of bounds and are silently dropped by the InBounds guards)",
                    () => new WorldGen.MutantTests.MutGridExtentIgnoresFootprintSelfTests().SelfTestFootprintTiles()),
                ("MutFootprintNoNullFallback", "SettlementTileGrid.FootprintOf's rule (a) disabled (cells.Count == 0 never falls back to the room's point; a footprint-less room draws nothing)",
                    () => new WorldGen.MutantTests.MutFootprintNoNullFallbackSelfTests().SelfTestFootprintTiles()),
                ("MutFootprintStaleNotRederived", "SettlementTileGrid.FootprintOf's rule (b) disabled (a stale single-cell footprint that disagrees with the room's point is trusted verbatim instead of being re-derived)",
                    () => new WorldGen.MutantTests.MutFootprintStaleNotRederivedSelfTests().SelfTestFootprintTiles()),

                ("MutDepthKeyNoRowSort", "SettlementTileGrid.DepthKey rewritten column-major (i primary, j secondary) instead of row-major (drops near-occludes-far entirely)",
                    () => new WorldGen.MutantTests.MutDepthKeyNoRowSortSelfTests().SelfTestDepth()),

                ("MutHeightConstant", "SettlementTileGrid.BuildingHeight's FNV term dropped (always returns BuildingHeightMin — every building the same height)",
                    () => new WorldGen.MutantTests.MutHeightConstantSelfTests().SelfTestHeight()),

                ("MutFootprintNoConnectivity", "SettlementFootprint.IsConnected4 returns true unconditionally (any cell set reads as one piece, diagonals included)",
                    () => new WorldGen.MutantTests.MutFootprintNoConnectivitySelfTests().SelfTestFootprint()),
                ("MutFootprintRoundNotFloor", "SettlementFootprint.CellOf rounds instead of flooring (a cell stops being the half-open span [i*Pitch,(i+1)*Pitch))",
                    () => new WorldGen.MutantTests.MutFootprintRoundNotFloorSelfTests().SelfTestFootprint()),
                ("MutMigrationSkipsFootprint", "SettlementFootprint.EnsureFootprints writes no cells (a v9 settlement building loads with no footprint at all)",
                    () => new WorldGen.MutantTests.MutMigrationSkipsFootprintSelfTests().SelfTestFootprintMigration()),

                ("MutBlocksNoRingStreet", "SettlementBlocks.Generate lays no ring street just inside the wall (so no gate can open on it either)",
                    () => new WorldGen.MutantTests.MutBlocksNoRingStreetSelfTests().SelfTestBlocks()),
                ("MutBlocksOverlapAllowed", "SettlementBlocks' fill drops the not-already-claimed term from Available (footprints overlap)",
                    () => new WorldGen.MutantTests.MutBlocksOverlapAllowedSelfTests().SelfTestBlocks()),

                ("MutBlocksNoArterials", "SettlementBlocks.Arterials lays nothing (no road runs inward from a gate, none reaches the town centre)",
                    () => new WorldGen.MutantTests.MutBlocksNoArterialsSelfTests().SelfTestFrontage()),
                ("MutBlocksNoFrontageFill", "SettlementBlocks.FrontageFill paves nothing (core cells are left with no street 4-neighbour)",
                    () => new WorldGen.MutantTests.MutBlocksNoFrontageFillSelfTests().SelfTestFrontage()),
                ("MutBlocksFillIgnoresNetwork", "SettlementBlocks' frontage strips no longer have to touch the existing network (street islands)",
                    () => new WorldGen.MutantTests.MutBlocksFillIgnoresNetworkSelfTests().SelfTestFrontage()),
                ("MutBlocksGatesAdjacent", "SettlementBlocks.PlaceGateCells drops the MinGateSeparationCells term (two gates in one doorway)",
                    () => new WorldGen.MutantTests.MutBlocksGatesAdjacentSelfTests().SelfTestFrontage()),

                ("MutFenceIgnoresFootprint", "DungeonLayout.LinkNodeFor projects a settlement building as its footprint's REPRESENTATIVE cell instead of its whole cell bbox (the fence wraps a point again, so a long footprint's far cells fall outside it)",
                    () => new WorldGen.MutantTests.MutFenceIgnoresFootprintSelfTests().SelfTestFence()),

                ("MutValidatorNoOverlapRule", "DungeonValidator's settlement disjointness rule dropped (two buildings may share a cell unreported — the data-side twin of SettlementVolumeRenderer.AreCellsFree, which Rendering puts out of the harness's reach)",
                    () => new WorldGen.MutantTests.MutValidatorNoOverlapRuleSelfTests().SelfTestSettlementValidation()),
                ("MutValidatorNoStreetRule", "DungeonValidator's settlement street-coincidence rule dropped (a building standing in its own street is never reported)",
                    () => new WorldGen.MutantTests.MutValidatorNoStreetRuleSelfTests().SelfTestSettlementValidation()),
                ("MutValidatorFootprintScopeAllRooms", "DungeonValidator's settlement rules widened past TypeId == 1 to every room, so they judge GATES too — whose one cell IS a street cell by construction, making the street rule fire on every gate of every town",
                    () => new WorldGen.MutantTests.MutValidatorFootprintScopeAllRoomsSelfTests().SelfTestSettlementValidation()),
                ("MutValidatorEmptyViaFootprintOf", "DungeonValidator's settlement shape rules read SettlementTileGrid.FootprintOf instead of the STORED Cells array — rule (a) there substitutes the room's point cell, so the non-empty rule becomes structurally incapable of firing",
                    () => new WorldGen.MutantTests.MutValidatorEmptyViaFootprintOfSelfTests().SelfTestSettlementValidation()),

                ("MutGateOpeningNoGates", "SettlementTileGrid's gate-reclassify write neutered, caught on GENERATED towns instead of a fixture (no town opens its wall at any gate)",
                    () => new WorldGen.MutantTests.MutGateOpeningNoGatesSelfTests().SelfTestGateOpening()),
                ("MutGateOpeningStreetsNotSeeded", "SettlementTileGrid's wall ring dilated from the buildings alone, caught on GENERATED towns (the outermost stored street cells fall outside the ring and read None instead of Road)",
                    () => new WorldGen.MutantTests.MutGateOpeningStreetsNotSeededSelfTests().SelfTestGateOpening()),

                ("MutPoiMigrationNoop", "PoiMigration.NormalizeLegacyTypes returns immediately (a legacy Village POI is never rewritten to City)",
                    () => new WorldGen.MutantTests.MutPoiMigrationNoopSelfTests().SelfTestPoiLegacyTypes()),

                ("MutSizingLargeOverflowsField", "SettlementSizing.WallRadiusCells(Large) blown up past the field (a Large town's wall leaves the 0.04..0.96 drag clamp)",
                    () => new WorldGen.MutantTests.MutSizingLargeOverflowsFieldSelfTests().SelfTestSizing()),
                ("MutSizingGuaranteeTooHigh", "SettlementSizing.GuaranteedMinBuildings returns TargetBuildings for every size (a guarantee equal to the target — precisely the lie the pre-Task-D provisional table used to tell, which the 200-seed calibration sweep exists to catch)",
                    () => new WorldGen.MutantTests.MutSizingGuaranteeTooHighSelfTests().SelfTestSizeCalibration()),
                ("MutMigrationNoRecentre", "SettlementMigration.RecentreFloor returns immediately (a pre-v11 town is left in the corner the finer lattice put it in)",
                    () => new WorldGen.MutantTests.MutMigrationNoRecentreSelfTests().SelfTestSizeMigration()),
                ("MutMigrationTruncatingHalf", "SettlementMigration.RecentreFloor's FloorHalf reverted to C#'s truncating / (a bbox summing odd AND negative recentres one cell off, then moves again on the next load)",
                    () => new WorldGen.MutantTests.MutMigrationTruncatingHalfSelfTests().SelfTestSizeMigration()),
                ("MutMigrationCurrentPitch", "SettlementFootprint.EnsureFootprints ignores legacyLattice and always uses CellOf (a LEGACY point read on the current lattice)",
                    () => new WorldGen.MutantTests.MutMigrationCurrentPitchSelfTests().SelfTestSizeMigration()),
                ("MutMigrationAlwaysLegacyPitch", "SettlementFootprint.EnsureFootprints ignores legacyLattice and always uses LegacyCellOf (a v11 point read on the legacy lattice — wrong data at rest that nothing repairs)",
                    () => new WorldGen.MutantTests.MutMigrationAlwaysLegacyPitchSelfTests().SelfTestSizeMigration()),
            };

            Console.WriteLine("Baseline: the shipped packer against the shipped suite");
            UnityEngine.Debug.Errors = 0;
            var quiet = Console.Out;
            new WorldGen.Rendering.CompactLayoutSelfTests().SelfTestColumnPacking();
            Console.WriteLine($"  -> {UnityEngine.Debug.Errors} errors (must be 0)");
            Console.WriteLine();

            foreach (var (name, what, run) in cases)
            {
                Console.WriteLine($"--- {name}: {what}");
                UnityEngine.Debug.Errors = 0;
                run();
                Console.WriteLine($"  -> {UnityEngine.Debug.Errors} errors" + (UnityEngine.Debug.Errors == 0
                    ? "   *** NOT DETECTED — an assertion is vacuous ***" : ""));
                Console.WriteLine();
            }
        }
    }
}
