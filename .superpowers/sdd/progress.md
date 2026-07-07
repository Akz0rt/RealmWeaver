# Map Terrain Raster — Progress Ledger

Base commit (branch start): a049252
Plan: docs/superpowers/plans/2026-07-07-map-terrain-raster.md
Workspace: worktree `.claude/worktrees/map-terrain-raster`, branch `map-terrain-raster` (manually created from local HEAD, not via EnterWorktree's default name-mode, since local main was ahead of origin/main — see memory unity-subagent-driven-dev-lessons)

Task 1: complete (commits 26934d1..5c86556, review clean — Approved)
Task 2: complete (commits 5c86556..bf94641, review clean — Approved; plan's code block was missing `using WorldGen.Generation;`, implementer fixed)
Task 3: complete (commits bf94641..a62f4e7, review clean — Approved; all 112 color-table values machine-diffed against brief, zero discrepancies)
Task 4: complete (commits a62f4e7..935fe38, review clean — Approved, verified bit-for-bit identical delegation)
Task 5: complete (commits 935fe38..0a41c12, review clean — Approved; self-test pixel/cell math hand-verified by reviewer)
Task 6: complete (commits 0a41c12..39ed93b, review clean — Approved on Opus; every load-bearing formula/ordering line-verified against brief, self-test fallback path hand-traced correct)
Task 7: complete (commits 39ed93b..3642432, review clean — Approved on Opus; plan's brief missed 3 of 12 RecolorOnly call sites, implementer found+fixed all, reviewer verified completeness; UV/pixel/cellId mapping cross-checked consistent with MapRasterizer)
Task 8: complete (commits 3642432..1de93a9, review clean — Approved)
Task 9: complete (commits 1de93a9..3479c9f, review clean — Approved)
Task 10: complete (automated checks done; user's live Play Mode pass found 3 real bugs, all fixed — see below; user confirmed on re-test "Да, баги пофикшены, насколько могу судить" — bugs fixed as far as they can tell)

## Live-testing bug fixes (found during Task 10's Play Mode pass, outside the plan's own tasks)
- Crash: degenerate cells (Polygon.Count < 3, never classified during generation, Biome defaults to C# `Ocean`=0 while IsOcean=false) reachable via NearestCellLookup, tripped MapPalette's fail-loud Sea/Lake guard. Fixed: NearestCellLookup excludes Polygon.Count < 3 cells (matches old fan-mesh renderer's own convention).
- Horizontal stripes every ~64 rows: RebakeAllStepped baked cellId+fields+color+vignette per chunk, so each chunk's last row read the next (not-yet-baked) chunk's zero-valued elevation for its hillshade gradient. Fixed: MapRasterizer.RebakeRegion split into BakeFieldsRect (order-independent) + ColorAndVignetteRect (needs neighbor rows already baked); RebakeAllStepped now bakes fields for the whole image once before chunking the color pass.
- Layer toggles (Биом/Рельеф in MapLayersPanel) had zero visual effect in Combined+smoothBorders mode (never wired into MapRasterConfig) while still triggering a full slow RebakeAll(). Fixed: added ShowBiomeLayer/ShowReliefLayer to MapRasterConfig, wired from BuildRasterConfig, honored in ColorForLandPixel/ColorForWaterPixel.
- Commits: 96adfc3 (all three fixes + 3 new self-tests), cd211de (follow-up: independent review found the Polygon<3 guard in 96adfc3 broke 6 self-tests whose fixture cells never set Polygon — fixed with a shared SquarePolygon(site) test helper). Both commits reviewed clean (96adfc3's review found the fixture-polygon issue as Critical; cd211de's re-review confirmed it fully fixed, no other issues, Approved).
- User separately raised a bigger, not-yet-actioned design question: current Combined+smoothBorders look reads as "mostly water/blob" and worse than the old flat-polygon view; wants stronger land/water contrast, optionally flatter per-region fill instead of smooth blending, vector-style coastlines like the mockup, and asked whether the map could be a genuinely vector image (with a separate polygon view for editing/brush, rendered view for display). Not yet resolved — flagged to revisit after bug-fix confirmation, likely needs superpowers:brainstorming given the scope (could affect subprojects 2-6 and the editing workflow).

## Final whole-branch review (opus, range a049252..cd211de)

Verdict: Ready to merge — with fixes. Cross-task composition sound (two-phase bake split, degenerate-cell exclusion, layer-toggle wiring, all 12 RecolorOnly→RebakeAll call sites migrated) all verified consistent; self-tests genuinely exercise the bugs they guard (SelfTestChunkedBakeContinuity confirmed to catch the naive-bake bug, not just pass tautologically).

- **Important (fixed, commit 6378ab8, re-reviewed Approved with no findings):** `rasterTexture` was never `Destroy`d before reassignment in `RebakeAll`/`RebakeAllStepped`, and there was no `OnDestroy` — every mode switch/override/undo/regeneration leaked up to ~16MB of native texture memory (old fan-mesh `RecolorOnly` allocated no texture at all, so this cost is new to this branch). Fixed: `Destroy(oldTexture)` before both reassignments + new `OnDestroy()` destroying `rasterTexture`/`rasterMaterial`. Re-review confirmed completeness (grepped all `rasterTexture=`/`rasterMaterial=` sites, all covered), no double-destroy/use-after-destroy risk, no collateral changes.
## Branch status

Sub-project 1/6 (base raster fill) is functionally complete and reviewed clean: all 10 plan tasks + 3 live-testing bug fixes + final whole-branch review's one Important finding (texture leak), all independently reviewed Approved. User chose "Оставить как есть" (keep as-is) at the finishing-a-development-branch checkpoint — branch `map-terrain-raster` and worktree `.claude/worktrees/map-terrain-raster` intentionally left un-merged/un-pushed, pending the still-open design-direction conversation (contrast/flat-fill/vector-rendering) which may itself produce further changes on this branch before it's finalized.

- **Minor (deferred, not blocking):** `BakeFieldsRect`'s `nearest.Id` deref is unguarded — would NRE only on a pathological all-degenerate-cells map (unreachable at real map scale). `ApplyDarknessRect`'s per-call `Color32[]` alloc is now on the brush hot path but bounded/transient (confirm-and-defer; `texture.Apply(false)` dominates anyway). Cosmetic: dead `ToWorldPos` method, stale `CellOverrideService.cs:17` doc comment referencing removed `RecolorOnly()`, self-tests use `Destroy` instead of `DestroyImmediate` on textures (no-ops harmlessly in Edit mode, logs a console warning).

## Follow-up feature: Coastline contour smoothing (2nd plan on same branch)

Spec: docs/superpowers/specs/2026-07-07-coastline-contour-smoothing-design.md (committed e3c0114)
Plan: docs/superpowers/plans/2026-07-07-coastline-contour-smoothing.md (committed 3dfa9b0) — 4 tasks
Base for this feature's tasks: 3dfa9b0
Pre-flight review done: caught that plan's original Task 3 left the build non-compiling (signature change, prod call sites deferred to Task 4); restructured so Task 3 updates all 8 call sites + prod calls pass existing `corners` field, Task 4 only adds the serialized `coastlineSmoothness` field + config wiring + brush self-test. Both tasks now independently compile/testable.

- Task 1: complete (commit 3dfa9b0..783cacd, review clean — Approved; reviewer stood in as compile-check, hand-traced fixture geometry + Chaikin counts, confirmed non-vacuous asserts. 2 Minor: (a) no test for the degenerate/open-chain skip branch — plan's Step 2 specified exactly one happy-path fixture, defer; (b) AddBoundaryNeighbor's Contains-dedup is unreachable given caller's neighborId<=corner.Id guard — harmless dead defensive code.)
- Task 2: complete (commit 783cacd..4f336c8, review clean — Approved; reviewer hand-traced the even-odd fill on the actual self-test fixture end-to-end + verified partial-rect write scoping + brace balance. 1 Minor: self-test never exercises exact-boundary tie-break cases (all vertices at integer coords, all samples at integer+0.5) — guard logic verified correct by hand, coverage-only gap, defer.)
- Task 3: complete (commit 4f336c8..00a8f73, review clean — Approved on opus; reviewer verified all 12 Bake/RebakeRegion/BakeFieldsRect call sites carry `corners` in the right position + ColorAndVignetteRect correctly untouched + WorldMapRenderer is the only caller file, traced the IsLand-mask categorization swap complete on every painted path, confirmed parity self-test is a real oracle. 1 Minor: `cell`/`cells` params now unused in ColorForLandPixel/BakeFieldsRect/BakePaintedFields — verbatim from brief, predate task, defer to cleanup.)
- Task 4: complete (commit 00a8f73..fd105b1, review clean — Approved; reviewer confirmed field declared/wired exactly once, single-file scope, and hand-traced the brush self-test fixture (pixel (22,10)→world (2.25,1.05) inside the edited cell, inside the second dirty rect, water→land flip real) — non-vacuous. 1 Minor: brush self-test hardcodes CoastlineSmoothness=2 so it validates the dirty-rect mechanism generically, not the new field→config wiring specifically (wiring is a trivial 1-line assignment, visually verified).)
- All 4 tasks complete + reviewed clean. Feature commits: 783cacd, 4f336c8, 00a8f73, fd105b1 (base 3dfa9b0).

### Final whole-feature review (opus, range 3dfa9b0..fd105b1) — Ready to merge: YES, no Critical/Important
Verified end-to-end composition across full/chunked-stepped/brush-partial bakes: signature threading complete (all 14 call sites pass `corners` correctly), categorization DECISION swap complete (every painted-mode land/water decision reads `!IsLand[idx]`; residual EffectiveIsOcean/IsLake reads are legit color-source selection only), rect-scoped RasterizeIsLand provably correct for partial rebakes (scans crossings from row's left edge so parity at rectX is established from crossings outside the rect), chunked-bake seam preserved (BakeFieldsRect runs once whole-image in RebakeAllStepped before any ColorAndVignetteRect chunk), `corners` guaranteed non-null in painted bake, trace is O(corners) called once per bake, degenerate chains skip without throw. 4 Minor findings (reviewer said ship as-is, all within spec's accepted scope):
  1. All-land map renders as all-water: if brush ForceLand removes the LAST water cell, TraceSmoothedLoops returns empty → RasterizeIsLand writes all-false (=all water). Unreachable at generation (edge falloff guarantees ocean border) but reachable via brush. Contrived hard cliff; one-line guard ("no loops AND ≥1 land cell → all land") removes it.
  2. Land pixel whose nearest cell is water with no land cell within SmoothRadius → sumW<=0 fallback takes FamilyColor from the water cell's Biome (ocean slot). Rare (Chaikin biases contour inward), cosmetic fringe at smoothed concave bays past SmoothRadius. Visual spot-check.
  3. Dead param: `ColorForLandPixel` (MapRasterizer.cs:360) no longer reads its `cell` arg; `cells` threaded through BakeFieldsRect→BakePaintedFields unused (latter predates feature). No warning/behavior impact; safe tidy-up (keep `cell` in BakePaintedPixel/ColorForWaterPixel — water path uses it).
  4. RasterizeIsLand scans all loop edges per scanline even for a tiny brush rect: O(rectH × E_total), E_total ~×2^smoothness. Design doc explicitly accepted full-retrace cost (YAGNI). Monitor; if brush lag appears, Y-bucket loop edges or clip to rect X-span, not incremental.
Not compiler-verified (Editor locked) — reviewer hand-traced types/usings/signatures, found nothing that won't compile.

### Post-review cleanup (commit 599fbb8) — findings #1 + #3 applied at user's request
- #1: BakeFieldsRect now guards `loops.Count == 0` — if any cell is land, fills the rect IsLand=true (all-land) instead of letting even-odd write all-water; else all-water. Only triggers when no coastline exists (brush ForceLand over last water cell); normal maps always have a coastline so the else branch (RasterizeIsLand) is unchanged. No existing self-test changes behavior (parity + brush tests both keep a coastline → else branch).
- #3: dropped unused `VoronoiCell cell` param from ColorForLandPixel + its one call site in BakePaintedPixel (kept on ColorForWaterPixel — water path uses it for lake-vs-ocean color).
- Findings #2 (concave-bay color fringe) and #4 (RasterizeIsLand per-scanline edge scan perf) left as-is per review (cosmetic / YAGNI-deferred). Applied changes are manual-review-only until Editor recompiles.

## Follow-up feature: Wide coastline glow (3rd plan on same branch)

Spec: docs/superpowers/specs/2026-07-07-coastline-glow-width-design.md (committed 6b242db)
Plan: docs/superpowers/plans/2026-07-07-coastline-glow-width.md (committed 21a42e1) — 2 tasks
Base for this feature's tasks: 21a42e1
Goal: widen only the water-side light glow (keep the 1px dark land outline) into a broad soft halo via a chamfer coast-distance field (cost independent of glow width). New field coastlineGlowWidth (int 0-64, default 16).
Pre-flight review: clean — Task 1 adds a utility method + CoastDistance buffer (no signature changes, compiles standalone), Task 2 consumes it. No inter-task compile break.

- Task 1: complete (commit 21a42e1..8d13161, review clean — Approved, zero findings; reviewer hand-traced the chamfer math against both fixtures (not just transcription), confirmed neighbor offsets/weights exact, and verified the seam test is genuinely discriminating (sub-rect has no land inside → correctness depends entirely on out-of-rect buffer reads).)
- Task 2: complete (commit 8d13161..fd8008c, review clean — Approved; reviewer verified all 6 edits formula-exact + dark outline untouched (HasNeighborWithWaterStatus still single-called with wantWater:true, old water-side wantWater:false call fully removed, no dead code) + field-name consistency across config/BuildRasterConfig/DT-call/pad + both self-tests non-vacuous (delta-vs-glowWidth=0 technique cancels ripple noise). 1 Minor: ComputeTouchedPixelRect xml-doc still only mentions smoothRadius, not the new glowWidth term — fixed post-review, doc-only.)
- Both tasks complete + reviewed clean. Feature commits: 8d13161, fd8008c (base 21a42e1).

## Minor findings (for final-review triage)
- Task 1 (`Noise.cs:19`): `(uint)h / 4294967296f` narrows to float 24-bit mantissa before dividing, ~6e-8 relative deviation from JS's double-precision result — brief-specified code, not implementer-introduced, harmless for visual terrain gen.
- Task 2 (`NearestCellLookup.cs`): `MaxRingSearch = 128` cap contradicts the "null only if index is empty" doc comment (unreachable in practice at this project's map scale); `FindWithinRadius`'s ring span is one ring wider than strictly necessary (perf-only, errs safe); ring-0 early exit is a no-op except at exact distance 0 (perf-only). All three inherited verbatim from the plan's own code, not implementer choices.
- Task 5 (`MapRasterizer.cs`): `ApplyDarknessRect` allocates a new `Color32[]` on every call, not persisted on `MapRasterBuffers` — could become a GC hot-path once Task 7/8 wire brush-driven partial rebakes through it frequently; worth a look in final review if brush painting feels laggy. Also `Elevation`/`Temperature`/`FamilyColor` buffers are allocated but unused until Task 6 populates them (expected, not dead code once Task 6 lands).
- Task 7 (`WorldMapRenderer.cs`): dead private `ToWorldPos` method left in place (only the removed fan-mesh code called it) — candidate for later cleanup. `CellOverrideService.cs:17`'s doc comment still says "WorldMapRenderer.RecolorOnly()" (method no longer exists) — cosmetic, in a file this task correctly didn't touch. Brush painting won't visually update yet — expected, Task 8 wires `RebakeAffectedCells` into `BrushToolController` next.
