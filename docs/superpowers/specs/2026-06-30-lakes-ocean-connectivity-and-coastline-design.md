# Lakes: ocean-connectivity + inland-lake coastline — Design

**Date:** 2026-06-30
**Status:** Approved (design), implementing inline on `feature/combined-map-visualization`

## Goal

Make inland lakes read as part of their political region, and reclassify lakes that touch the ocean as ocean.

Two related changes:
1. **Generation:** a water cell connected to the ocean (through other water cells) becomes ocean.
2. **Rendering:** the coastline layer outlines only the ocean shore, not inland lakes.

## Context (current state)

- Cell water status comes from corners in `CellWaterAssigner.AssignFromCorners`: a cell is water if ≥ `waterFractionThreshold` of its corners are water; ocean if ≥ half its water corners are ocean. A coastal cell can end up classified as a *lake* even when adjacent to ocean cells (fraction thresholds).
- Lake-ness downstream is derived in `CellClimateAverager.ApplyToCells`: `isLake = !cell.IsOcean && waterCorners > 0 && oceanCorners < waterCorners`; biome via `BiomeClassifier.Classify(..., cell.IsOcean, isLake, ...)`. So setting `cell.IsOcean = true` before this step makes a cell classify as Ocean.
- Region growing (`WorldGenerator` line 168) uses `landCells = cells.Where(c => !c.IsOcean)` and treats ocean as a wall. Lakes (`IsOcean == false`) already receive a `RegionId` (growth flows through them).
- Borders: `MapBorderBuilder.ClassifyBorderEdges` currently marks a coastline edge when exactly one side is water (ocean **or** lake), so every inland lake gets a closed shoreline ring.

## Part A — ocean-connected water becomes ocean

In `CellWaterAssigner`, after assigning per-cell water/ocean status from corners, collect the set of water cell ids, then run a flood:

`PromoteOceanConnectedWater(List<VoronoiCell> cells, HashSet<int> waterCellIds)` — BFS from cells already `IsOcean`, flowing through `NeighborIds` to neighbors that are in `waterCellIds` and not yet ocean, setting them `IsOcean = true`. Land cells are walls. Genuine inland lakes (water not reachable from the ocean through water) keep `IsOcean == false`.

Extracted as a public static method so it can be exercised by a self-check independent of corner data.

Downstream effects (all correct):
- Region growing skips promoted cells (ocean = wall) — they are sea now.
- `CellClimateAverager` classifies them as Ocean biome (uses `cell.IsOcean`).
- Combined/Region rendering treats them as ocean; the ocean shoreline now includes them.

Known minor limitation (out of scope): moisture is computed at the corner level (fresh water = lake corners). Promoting a cell to ocean does not reclassify its corners, so a former lake-now-ocean may still contribute slight freshwater moisture nearby. Acceptable.

## Part B — coastline = ocean shore only

In `MapBorderBuilder.ClassifyBorderEdges`, change the coastline test from "exactly one side is water" to "exactly one side is **ocean** (`EffectiveIsOcean`)". Region-border rule unchanged (both sides land — not water — and different `RegionId`).

Resulting edge classification:
- ocean ↔ land → coastline
- lake ↔ land → nothing (inland lake not outlined)
- lake ↔ lake, ocean ↔ ocean, land ↔ land(same region) → nothing
- land ↔ land(diff region) → region border

Combined with Part A: lakes that touched the ocean are now ocean and get the normal ocean shoreline; genuine inland lakes are unoutlined and blend into their region's fill (they remain blue water by biome color).

## Testing (ContextMenu self-checks, matching project convention; run by human in Unity)

- `SelfTestBorderClassification` (extend): add a case land ↔ lake → 0 coast edges, 0 region edges; keep land ↔ ocean → 1 coast edge.
- `SelfTestOceanConnectivity` (new): ocean–lake–lake chain → all become ocean; an isolated lake (only land neighbors) stays a lake.

## Files

- Modify: `Assets/WorldGen/Generation/CellWaterAssigner.cs` (collect water cell ids + `PromoteOceanConnectedWater`)
- Modify: `Assets/WorldGen/Rendering/MapBorderBuilder.cs` (coastline rule)
- Modify: `Assets/WorldGen/Rendering/WorldMapRenderer.cs` (extend border self-check + new connectivity self-check)

## Out of scope

- Corner-level reclassification of promoted lakes (moisture).
- Changing lake color or the region-assignment rule (lakes already get a RegionId).
