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

- **Important (fixed, commit below):** `rasterTexture` was never `Destroy`d before reassignment in `RebakeAll`/`RebakeAllStepped`, and there was no `OnDestroy` — every mode switch/override/undo/regeneration leaked up to ~16MB of native texture memory (old fan-mesh `RecolorOnly` allocated no texture at all, so this cost is new to this branch). Fixed: `Destroy(oldTexture)` before both reassignments + new `OnDestroy()` destroying `rasterTexture`/`rasterMaterial`.
- **Minor (deferred, not blocking):** `BakeFieldsRect`'s `nearest.Id` deref is unguarded — would NRE only on a pathological all-degenerate-cells map (unreachable at real map scale). `ApplyDarknessRect`'s per-call `Color32[]` alloc is now on the brush hot path but bounded/transient (confirm-and-defer; `texture.Apply(false)` dominates anyway). Cosmetic: dead `ToWorldPos` method, stale `CellOverrideService.cs:17` doc comment referencing removed `RecolorOnly()`, self-tests use `Destroy` instead of `DestroyImmediate` on textures (no-ops harmlessly in Edit mode, logs a console warning).

## Minor findings (for final-review triage)
- Task 1 (`Noise.cs:19`): `(uint)h / 4294967296f` narrows to float 24-bit mantissa before dividing, ~6e-8 relative deviation from JS's double-precision result — brief-specified code, not implementer-introduced, harmless for visual terrain gen.
- Task 2 (`NearestCellLookup.cs`): `MaxRingSearch = 128` cap contradicts the "null only if index is empty" doc comment (unreachable in practice at this project's map scale); `FindWithinRadius`'s ring span is one ring wider than strictly necessary (perf-only, errs safe); ring-0 early exit is a no-op except at exact distance 0 (perf-only). All three inherited verbatim from the plan's own code, not implementer choices.
- Task 5 (`MapRasterizer.cs`): `ApplyDarknessRect` allocates a new `Color32[]` on every call, not persisted on `MapRasterBuffers` — could become a GC hot-path once Task 7/8 wire brush-driven partial rebakes through it frequently; worth a look in final review if brush painting feels laggy. Also `Elevation`/`Temperature`/`FamilyColor` buffers are allocated but unused until Task 6 populates them (expected, not dead code once Task 6 lands).
- Task 7 (`WorldMapRenderer.cs`): dead private `ToWorldPos` method left in place (only the removed fan-mesh code called it) — candidate for later cleanup. `CellOverrideService.cs:17`'s doc comment still says "WorldMapRenderer.RecolorOnly()" (method no longer exists) — cosmetic, in a file this task correctly didn't touch. Brush painting won't visually update yet — expected, Task 8 wires `RebakeAffectedCells` into `BrushToolController` next.
