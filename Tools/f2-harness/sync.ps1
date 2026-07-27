# Copies the REAL Unity sources into gen/ and derives the packer variants used by the sweep.
# Nothing under gen/ is edited by hand — re-run this after every source change.
#
#   powershell -File sync.ps1
#
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$src  = Join-Path $repo 'Assets\WorldGen\Generation'
$gen  = Join-Path $here 'gen'

if (Test-Path $gen) { Remove-Item -Recurse -Force $gen }
New-Item -ItemType Directory -Path $gen | Out-Null

$files = @(
  'CompactLayout.cs', 'BuildingGenerator.cs', 'FloorFootprint.cs', 'DungeonProjection.cs',
  'DungeonLayout.cs', 'RoomSizing.cs', 'RoomLinkGeometry.cs', 'DungeonGraphGenerator.cs',
  'DungeonValidator.cs', 'DungeonData.cs', 'DungeonOps.cs', 'CompactLayoutSelfTests.cs',
  'BuildingGeneratorSelfTests.cs', 'DungeonGraphSelfTests.cs',
  'BattleGridData.cs', 'BattleGridGenerator.cs', 'BattleGridOps.cs', 'BattleGridUndo.cs', 'BattleGridSelfTests.cs',
  'WallContour.cs', 'SettlementGenerator.cs', 'SettlementStreets.cs', 'SettlementRoads.cs', 'SettlementFence.cs', 'SettlementSelfTests.cs',
  'InteriorOps.cs', 'InteriorOpsSelfTests.cs',
  'SettlementTileGrid.cs', 'SettlementTileGridSelfTests.cs', 'SettlementFootprint.cs',
  'SettlementBlocks.cs', 'SettlementBlocksSelfTests.cs',
  'PoiData.cs', 'PoiMigrationSelfTests.cs', 'PoiMigration.cs'
)
foreach ($f in $files) { Copy-Item (Join-Path $src $f) (Join-Path $gen $f) }

# ---- derive the packer variants -----------------------------------------------------------------
# Variant (b) SPREAD-ONLY  : the old seeding (increasing-d rays against the BFS parent) + phases 2/3.
# Variant (c') COMPACT-ONLY: flush-only seeding + phases 2/3 (the run the review asked us to justify).
# Both are produced by a two-line rewrite of the real CompactLayout.cs so they can never drift from it.
$layout = Get-Content (Join-Path $src 'CompactLayout.cs') -Raw -Encoding UTF8

# The whole run-selection block (from "var compact = RunPhases(" through the third run's "chosen = plain;") is
# replaced by a SINGLE RunPhases call, so each variant runs exactly one pipeline and nothing else about the
# packer changes. The tail guard below is load-bearing: when F4 added a third run, a rewrite that stopped at
# "var chosen = ...;" silently left that run appended to every variant, so (b)/(c') stopped being single
# pipelines and quietly scored as best-of-two.
#
# $noCuts additionally DISABLES F4's two fill-sweep skips (see the optcheck section below), so the SAME
# single pipeline can be run with and without them.
function New-Variant([string]$className, [string]$seedMaxDistance, [string]$slide, [string]$outFile,
                     [bool]$noCuts = $false) {
  $t = $layout -replace 'public static class CompactLayout', "public static class $className"
  $one = "var chosen = RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds, seedMaxDistance: $seedMaxDistance, slide: $slide);"
  $t = $t -replace '(?s)var compact = RunPhases\(.*?if \(plain != null && plain\.Ids\.Count > chosen\.Ids\.Count\) chosen = plain;', $one
  if ($t -notmatch [regex]::Escape($one)) { throw "variant rewrite failed for $className" }
  if ($t -match '(?s)var compact = RunPhases\(') { throw "variant rewrite left the run-selection block in $className" }
  if ($t -match 'RunPhases\(' -and ([regex]::Matches($t, 'RunPhases\(')).Count -ne 2) {
    throw "variant $className should call RunPhases exactly twice (its declaration + the single run)"
  }
  if ($noCuts) {
    # Cut 1 OFF: never skip the anchors this room has already been tried against — always start at anchor 0.
    $cut1 = 'int minSeq;\r?\n\s*if \(!triedUpTo\.TryGetValue\(room\.Id, out minSeq\)\) minSeq = 0;'
    if ($t -notmatch $cut1) { throw "no-cuts rewrite 1 (triedUpTo) did not match for $className" }
    $t = $t -replace $cut1, 'int minSeq = 0;   // NO-CUTS variant: F4 cut 1 (per-room anchor high-water mark) disabled'
    # Cut 2 OFF: phase 3 re-walks the whole d == 0 flush search phase 2 already took to a fixpoint.
    $cut2 = 'flushDoneSeq: res\.Placed\.Count'
    if ($t -notmatch $cut2) { throw "no-cuts rewrite 2 (flushDoneSeq) did not match for $className" }
    $t = $t -replace $cut2, 'flushDoneSeq: 0'
  }
  Set-Content -Path (Join-Path $gen $outFile) -Value $t -Encoding UTF8
}

New-Variant 'SpreadOnlyLayout'     'DungeonLayout.TilesPerAxis' 'false' 'SpreadOnlyLayout.cs'
New-Variant 'CompactOnlyLayout'    '0'                          'true'  'CompactOnlyLayout.cs'
# Slide-free single pipelines, for the F4 optimization check below: these must agree with the SAME single
# pipelines cut out of the pre-F4 source, ROOM POSITION BY ROOM POSITION.
New-Variant 'CompactNoSlideLayout' '0'                          'false' 'CompactNoSlideLayout.cs'
# The SLIDE-ENABLED compact pipeline with both cuts REMOVED. This is the pair that actually exercises what the
# cuts are for: in a slide-free pipeline `maxSlide` is 0 over the flush-filtered lists, so cut 2's "phase 3 skips
# a d == 0 pass that would have been a SLID pass" case never even arises. Compared against CompactOnlyLayout
# (the same pipeline WITH the cuts) by `optcheck`.
New-Variant 'CompactSlideNoCuts'   '0'                          'true'  'CompactSlideNoCuts.cs' $true

# The packer AS REVIEWED (commit dd6e3dc, before the review fixes) — the perf baseline for finding I5.
$pre = git -C $repo show 'dd6e3dc:Assets/WorldGen/Generation/CompactLayout.cs'
if (-not $pre) { throw 'could not read CompactLayout.cs at dd6e3dc' }
$pre = ($pre -join "`n") -replace 'public static class CompactLayout', 'public static class PreReviewLayout'
# F4 added CompactLayout.DoorGapTiles (the door-opening width the slide's bound reads). The rebound test copies
# rewrite every "CompactLayout." to "<variant>.", so this historical snapshot needs the constant to compile. It
# is inert here — dd6e3dc has no slide to bound.
$pre = $pre -replace 'const float TouchEps = 0\.02f;', "public const float DoorGapTiles = 1.4f;   // injected by sync.ps1 (F4)`n        const float TouchEps = 0.02f;"

# The packer as SHIPPED IMMEDIATELY BEFORE task F4 (commit e409a9c) — the before/after control for the lateral
# slide: same machine, same run, so the perf table's before/after cannot be blamed on noise, and the rebound
# self-test copy shows the REAL previous build failing exactly the assertions F4 adds.
$preSlide = git -C $repo show 'e409a9c:Assets/WorldGen/Generation/CompactLayout.cs'
if (-not $preSlide) { throw 'could not read CompactLayout.cs at e409a9c' }
$preSlide = ($preSlide -join "`n") -replace 'public static class CompactLayout', 'public static class PreSlideLayout'
$preSlide = $preSlide -replace 'const float TouchEps = 0\.02f;', "public const float DoorGapTiles = 1.4f;   // injected by sync.ps1 (F4)`n        const float TouchEps = 0.02f;"
Set-Content -Path (Join-Path $gen 'PreSlideLayout.cs') -Value $preSlide -Encoding UTF8

# F4 added two SKIP optimizations to the fill sweeps (don't re-try a room against an anchor it already failed
# against; don't re-run the flush pass phase 2 already took to a fixpoint). Both are claimed EXACT — they must
# change how long the search takes and nothing else. These four single-pipeline variants let `optcheck` verify
# that claim the only way that means anything: the same pipeline, with and without the optimizations, must
# produce the same kept set AND the same X/Y for every room over the whole corpus.
function New-PreSlideVariant([string]$className, [string]$seedMaxDistance, [string]$outFile) {
  $t = $preSlide -replace 'public static class PreSlideLayout', "public static class $className"
  $one = "var chosen = RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds, seedMaxDistance: $seedMaxDistance);"
  $t = $t -replace '(?s)var compact = RunPhases\(.*?var chosen = [^;]*;', $one
  if ($t -notmatch [regex]::Escape($one)) { throw "pre-slide variant rewrite failed for $className" }
  # Same residue guard New-Variant carries, and for the same reason: a rewrite that silently leaves part of the
  # run-selection block behind turns a "single pipeline" column into a best-of-N one without saying so.
  if ($t -match '(?s)var compact = RunPhases\(') { throw "pre-slide variant rewrite left the run-selection block in $className" }
  Set-Content -Path (Join-Path $gen $outFile) -Value $t -Encoding UTF8
}
New-PreSlideVariant 'PreSlideSpreadOnly'  'DungeonLayout.TilesPerAxis' 'PreSlideSpreadOnly.cs'
New-PreSlideVariant 'PreSlideCompactOnly' '0'                          'PreSlideCompactOnly.cs'
Set-Content -Path (Join-Path $gen 'PreReviewLayout.cs') -Value $pre -Encoding UTF8

# The shipped packer MINUS run 3 (the slide-free compact fallback) — i.e. F4's slid runs on their own. This is
# what re-derives the "34 of 1200 contours end with a LOWER «из N»" figure that justifies run 3's existence and
# is quoted in CompactLayout's class doc; the sweep reports it as column (f) with an "(f) vs (e)" tally.
$noPlain = $layout -replace 'public static class CompactLayout', 'public static class NoPlainRunLayout'
$dropRun3 = '(?s)\r?\n\s*// RUN 3 .*?if \(plain != null && plain\.Ids\.Count > chosen\.Ids\.Count\) chosen = plain;'
if ($noPlain -notmatch $dropRun3) { throw 'NoPlainRunLayout rewrite failed: run-3 block not found' }
$noPlain = $noPlain -replace $dropRun3, ''
if (([regex]::Matches($noPlain, 'RunPhases\(')).Count -ne 3) {
  throw 'NoPlainRunLayout should call RunPhases exactly three times (its declaration + runs 1 and 2)'
}
Set-Content -Path (Join-Path $gen 'NoPlainRunLayout.cs') -Value $noPlain -Encoding UTF8

# ---- MUTANTS: each removes exactly one rule the new self-test assertions are supposed to pin down. -------
# A new assertion is non-vacuous iff the corresponding mutant makes it FAIL (harness command: "mutants").
function New-Mutant([string]$className, [string]$pattern, [string]$replacement, [string]$outFile) {
  $t = $layout -replace 'public static class CompactLayout', "public static class $className"
  if ($t -notmatch $pattern) { throw "mutant pattern did not match for $className" }
  $t = $t -replace $pattern, $replacement
  Set-Content -Path (Join-Path $gen $outFile) -Value $t -Encoding UTF8
}

# M-AnchorOuter: swap SeatAgainstAnyPlaced's loops to anchor-outer / distance-inner (kills assertion 23). The
# single-anchor TrySeatAtDistance still runs the full offset ladder, so this mutant changes ONLY the nesting.
# Both F4 cost cuts are PRESERVED so it stays single-rule: cut 1 already lives in the linked/other split this
# replacement reads (anchors below minSeq were never added), and cut 2 is restored explicitly by starting the
# distance loop at 1 for anchors phase 2 already flush-tested — the earlier version of this mutant routed d == 0
# through the UNFILTERED lists and so quietly removed cut 2 as well as inverting the nesting.
New-Mutant 'MutAnchorOuter' `
  '(?s)int maxSlide = ctx\.Slide \? Larger\(MaxSlideTiles.*?\r?\n            \}\r?\n            return null;' `
  @'
foreach (var anchor in ctx.LinkedAnchors)
            {
                int aSeq0;
                if (!ctx.Seq.TryGetValue(anchor.Id, out aSeq0)) aSeq0 = 0;
                for (int d = aSeq0 < ctx.FlushDoneSeq ? 1 : 0; d <= limit; d++)
                    if (TrySeatAtDistance(room, anchor, d, ctx.Placed, ctx.ContourFloor, ctx.Margin, ctx.Bounds, ctx.Slide)) return anchor;
            }
            foreach (var anchor in ctx.OtherAnchors)
            {
                int aSeq1;
                if (!ctx.Seq.TryGetValue(anchor.Id, out aSeq1)) aSeq1 = 0;
                for (int d = aSeq1 < ctx.FlushDoneSeq ? 1 : 0; d <= limit; d++)
                    if (TrySeatAtDistance(room, anchor, d, ctx.Placed, ctx.ContourFloor, ctx.Margin, ctx.Bounds, ctx.Slide)) return anchor;
            }
            return null;
'@ `
  'MutAnchorOuter.cs'

# M-NoLinkPref: drop the "already-linked anchors first" preference (kills assertion 25). Every anchor goes into
# the "other" bucket, so the two preference groups collapse to one plain ascending-id list; the anchor filters
# and the rest of the search are untouched.
New-Mutant 'MutNoLinkPref' `
  'if \(isLinked\) ctx\.Linked(Anchors|Flush)\.Add\(anchor\); else ctx\.Other(Anchors|Flush)\.Add\(anchor\);' `
  'ctx.Other$2.Add(anchor);' `
  'MutNoLinkPref.cs'

# ---- F4 mutants: one per rule the lateral slide introduced. ---------------------------------------------
# M-NoSlide: candidate offsets collapse to {0} — i.e. the pre-F4 centre-aligned plus-lattice (kills 27 and the
# reachable half of 30). Both offset ladders (phase 1's single-anchor one and the fill phases' anchor-spanning
# one) are pinned by the same assignment shape, so this removes the slide everywhere at once.
New-Mutant 'MutNoSlide' 'int maxK = [^;]+;' 'int maxK = 0;   // MUTANT: no lateral slide' 'MutNoSlide.cs'

# M-NoDoorBound: keep the slide but drop the DoorGapTiles term, so a slide may run until the shared span hits
# ZERO (a corner kiss) instead of stopping while a door still fits (kills the FAR half of assertion 30).
New-Mutant 'MutNoDoorBound' `
  'float lim = \(pExtent \+ cExtent\) \* 0\.5f - DoorGapTiles;' `
  'float lim = (pExtent + cExtent) * 0.5f;   // MUTANT: no door-overlap bound' `
  'MutNoDoorBound.cs'

# M-SlideFarFirst: try the LARGEST offset magnitude first instead of the centred slot (kills assertion 29 —
# centre-first ordering — without changing which candidates are reachable at all).
New-Mutant 'MutSlideFarFirst' 'for \(int k = 0; k <= maxK; k\+\+\)' 'for (int k = maxK; k >= 0; k--)' 'MutSlideFarFirst.cs'

# M-TightBounds: strip the margin term from the bbox pre-test (kills assertion 24).
New-Mutant 'MutTightBounds' 'float g = margin \+ BoundsSlack;' 'float g = BoundsSlack;' 'MutTightBounds.cs'

# M-TightCut: tighten MaxUsefulDistance's cut-off by one tile (kills assertion 26 — the boundary sits at
# EXACTLY d=11 on its fixture, so shaving the limit to 10 makes that one slot unreachable and drops the room).
New-Mutant 'MutTightCut' 'best \+= BoundsSlack;' "best += BoundsSlack;`n            best -= 1f;" 'MutTightCut.cs'

# M-OneSideCut: collapse MaxUsefulDistance's four-side max down to a single side, "up" (kills assertion 26 the
# other way — on its fixture only the RIGHT term is positive, so keeping just "up" alone goes negative and the
# distance loop never runs at all).
New-Mutant 'MutOneSideCut' `
  '(?s)if \(r > best\) best = r;\r?\n\s*if \(l > best\) best = l;\r?\n\s*if \(dn > best\) best = dn;\r?\n\s*if \(up > best\) best = up;' `
  'best = up;' `
  'MutOneSideCut.cs'

# M-NoDedup: add the fill-phase link unconditionally — removes the ENTIRE duplicate-link guard (both
# directions of the pair test at once, not just the reverse half — MutFwdDedupOnly below is what isolates
# that), so EVERY anchor/room pair offered a second time gains a duplicate edge regardless of which order the
# pre-existing link happens to be stored in (kills assertion 18, both the named pair and the general sweep).
New-Mutant 'MutNoDedup' `
  '(?s)foreach \(var l in floor\.Links\)\r?\n\s*if \(\(l\.RoomA == a && l\.RoomB == b\) \|\| \(l\.RoomA == b && l\.RoomB == a\)\) return;' `
  '' `
  'MutNoDedup.cs'

# M-FwdDedupOnly: keep only the FORWARD half of the dedup test (kills assertion 18 when the fixture's
# pre-existing link is stored in the reverse order).
New-Mutant 'MutFwdDedupOnly' `
  '\(\(l\.RoomA == a && l\.RoomB == b\) \|\| \(l\.RoomA == b && l\.RoomB == a\)\)' `
  '(l.RoomA == a && l.RoomB == b)' `
  'MutFwdDedupOnly.cs'

# TRACING copies: same code, plus a Console line per placement and per run choice. Used only to derive (and
# double-check) the "which anchor at which distance" numbers quoted in the self-test comments.
function New-Trace([string]$body, [string]$className, [string]$outFile) {
$tr = $body -replace 'public static class CompactLayout', "public static class $className"
$tr = $tr -replace '(?s)(\s*)res\.LinkA\.Add\(anchor\.Id\); res\.LinkB\.Add\(room\.Id\);',
  '$1System.Console.WriteLine($"    FILL(maxD={maxDistance}) room {room.Id} -> anchor {anchor.Id} at ({ToTile(room.X):F1},{ToTile(room.Y):F1})");$1res.LinkA.Add(anchor.Id); res.LinkB.Add(room.Id);'
$tr = $tr -replace '(?s)(\s*)res\.Placed\.Add\(child\);(\s*)res\.Ids\.Add\(nb\);',
  '$1System.Console.WriteLine($"    SEED(maxD={seedMaxDistance}) room {child.Id} -> parent {cur.Id} at ({ToTile(child.X):F1},{ToTile(child.Y):F1})");$1res.Placed.Add(child);$2res.Ids.Add(nb);'
$tr = $tr -replace '(\s*)var chosen = spread != null',
  '$1System.Console.WriteLine($"    RUNS compact={compact.Ids.Count} spread={(spread == null ? -1 : spread.Ids.Count)}");$1var chosen = spread != null'
$tr = $tr -replace '(\s*)var compact = RunPhases', '$1System.Console.WriteLine("  == COMPACT run ==");$1var compact = RunPhases'
$tr = $tr -replace '(\s*): RunPhases\(floor, column, ordered, adj, contourFloor, margin, bounds,', '$1: RunPhasesTraced(floor, column, ordered, adj, contourFloor, margin, bounds,'
$tr = $tr -replace '(?s)(static PackResult RunPhases\()', @'
static PackResult RunPhasesTraced(InteriorFloor floor, Room column, List<Room> ordered,
            Dictionary<int, List<int>> adj, InteriorFloor contourFloor, float margin,
            (float minX, float minY, float maxX, float maxY) bounds, int seedMaxDistance, bool slide)
        {
            System.Console.WriteLine("  == SPREAD run ==");
            return RunPhases(floor, column, ordered, adj, contourFloor, margin, bounds, seedMaxDistance, slide);
        }

        $1
'@
Set-Content -Path (Join-Path $gen $outFile) -Value $tr -Encoding UTF8
}

$anchorOuterBody = Get-Content (Join-Path $gen 'MutAnchorOuter.cs') -Raw -Encoding UTF8
$anchorOuterBody = $anchorOuterBody -replace 'public static class MutAnchorOuter', 'public static class CompactLayout'
New-Trace $layout          'TraceLayout'            'TraceLayout.cs'
New-Trace $anchorOuterBody 'TraceAnchorOuterLayout' 'TraceAnchorOuterLayout.cs'

# ---- MUTANT-BOUND SELF-TESTS ---------------------------------------------------------------------------
# The real CompactLayoutSelfTests, rebound to each mutant packer. Running these proves the new assertions are
# NON-VACUOUS: a mutant that removes the rule an assertion pins must make that assertion FAIL.
$tests = Get-Content (Join-Path $src 'CompactLayoutSelfTests.cs') -Raw -Encoding UTF8
foreach ($mn in @('MutAnchorOuter', 'MutNoLinkPref', 'MutTightBounds', 'MutTightCut', 'MutOneSideCut',
                  'MutNoDedup', 'MutFwdDedupOnly', 'MutNoSlide', 'MutNoDoorBound', 'MutSlideFarFirst',
                  'SpreadOnlyLayout', 'CompactOnlyLayout', 'PreReviewLayout', 'PreSlideLayout')) {
  # NOTE: one flat namespace with distinct class names — a namespace named after the mutant would SHADOW the
  # mutant class itself (the same name/namespace shadowing that has bitten this project before).
  $t = $tests -replace 'namespace WorldGen\.Rendering', 'namespace WorldGen.MutantTests'
  $t = $t -replace 'class CompactLayoutSelfTests', "class ${mn}SelfTests"
  $t = $t -replace 'CompactLayout\.', "WorldGen.Generation.$mn."
  Set-Content -Path (Join-Path $gen "SelfTests_$mn.cs") -Value $t -Encoding UTF8
}

# ---- BATTLE GRID MUTANTS: same discipline, four rules pinned by BattleGridGenerator/BattleGridOps. --------
# Mirrors New-Mutant's shape exactly: read straight from the real source, throw if the pattern is gone,
# re-namespace, write to gen/. Each mutant re-reads its OWN source file fresh, so the two mutants that share
# a source file (BattleGridGenerator.cs, or BattleGridOps.cs) never step on each other.
function New-BattleMutant([string]$srcFile, [string]$className, [string]$from, [string]$to, [string]$outFile) {
  $t = Get-Content (Join-Path $src $srcFile) -Raw -Encoding UTF8
  if ($t -notmatch [regex]::Escape($from)) { throw "mutant $outFile : pattern not found: $from" }
  $t = $t -replace [regex]::Escape($from), $to
  $t = $t -replace 'namespace WorldGen.Generation', "namespace WorldGen.Generation.$className"
  Set-Content (Join-Path $gen $outFile) $t -Encoding UTF8
}

# MutNoRing: Generate's wall-ring condition forced to false, so the buffer comes out solid Floor with no
# ring at all. SelfTestGenerator's named-corner/named-wall Wall checks (assertion 2) must fail.
New-BattleMutant 'BattleGridGenerator.cs' 'MutNoRing' `
  '(x == 0 || y == 0 || x == w - 1 || y == h - 1)' 'false' 'MutNoRing.cs'

# MutNoYFlip: AlongVertical's "distance down from the top" numerator flipped to "distance up from the
# bottom", so a door point at the top of the tile rect (screen-top) lands on the wrong grid row.
# SelfTestDoors assertion 3 pins the direction with a synthetic point, independent of which wall of the
# fixture the router actually routed the door to.
New-BattleMutant 'BattleGridGenerator.cs' 'MutNoYFlip' `
  '(top - ty)' '(ty - bottom)' 'MutNoYFlip.cs'

# MutFirstTouch: BattleGridStroke.Paint's "first touch wins" guard replaced with an unconditional record, so
# repainting a cell mid-stroke records it a SECOND time instead of keeping only the stroke's ORIGINAL
# previous value. SelfTestOps assertion 3 repaints the stamp's centre cell and checks the recorded previous
# is still the very first value (Empty), not the intermediate Wall — exactly what this mutant breaks.
New-BattleMutant 'BattleGridOps.cs' 'MutFirstTouch' `
  'if (touched.Add(idx))' 'if (true)' 'MutFirstTouch.cs'

# MutFillDiagonal: Fill's flood spread gains the four diagonal neighbours, so paint leaks across a
# diagonal-only pinch. SelfTestOps assertion 7 builds exactly such a pinch — (1,1) reachable from (0,0) only
# diagonally, because (1,0) and (0,1) are Wall — to catch this.
New-BattleMutant 'BattleGridOps.cs' 'MutFillDiagonal' `
  'Enqueue(px, py + 1); Enqueue(px, py - 1);' `
  "Enqueue(px, py + 1); Enqueue(px, py - 1);`r`n                Enqueue(px + 1, py + 1); Enqueue(px + 1, py - 1); Enqueue(px - 1, py + 1); Enqueue(px - 1, py - 1);   // MUTANT: diagonal spread" `
  'MutFillDiagonal.cs'

# ---- BATTLE GRID MUTANT-BOUND SELF-TESTS -------------------------------------------------------------------
# The real BattleGridSelfTests, rebound to each mutant. MutNoRing/MutNoYFlip only touch
# BattleGridGenerator.cs, which defines BOTH BattleGridGenerator and GridPoint — but GridPoint is never named
# explicitly in the test file (every call site captures it through `var`), so a blanket rebind of
# "BattleGridGenerator." alone is sound across the WHOLE class, exactly like the CompactLayout loop above.
$battleTests = Get-Content (Join-Path $src 'BattleGridSelfTests.cs') -Raw -Encoding UTF8
foreach ($bm in @('MutNoRing', 'MutNoYFlip')) {
  $t = $battleTests -replace 'namespace WorldGen\.Rendering', 'namespace WorldGen.MutantTests'
  $t = $t -replace 'class BattleGridSelfTests', "class ${bm}SelfTests"
  # New-BattleMutant only nests the NAMESPACE (WorldGen.Generation.<mutant>) — unlike CompactLayout's
  # mutants, it does NOT rename the class itself (BattleGridGenerator.cs defines two types, so renaming
  # only one would collide with the pristine copy's untouched second type). So the rebind must PREPEND the
  # namespace ahead of the class name, not replace the class name with the namespace.
  if ($t -notmatch 'BattleGridGenerator\.') { throw "no BattleGridGenerator. call found while deriving SelfTests_$bm.cs" }
  $t = $t -replace 'BattleGridGenerator\.', "WorldGen.Generation.$bm.BattleGridGenerator."
  Set-Content -Path (Join-Path $gen "SelfTests_$bm.cs") -Value $t -Encoding UTF8
}

# MutFirstTouch/MutFillDiagonal mutate BattleGridOps.cs, which defines BOTH BattleGridStroke AND
# BattleGridOps — re-namespacing it moves both. A blanket file-wide rebind would ALSO touch SelfTestUndo,
# which calls BattleGridOps.Stamp too but then hands that same stroke to the REAL (unmutated)
# BattleGridUndo.PushStroke (BattleGridUndo.cs is not one of the mutated files). One local variable cannot
# be both the mutant's BattleGridStroke (what Stamp would require) and the real one (what PushStroke
# requires) at once — that is exactly the type-identity trap the task brief warns about, and it would not
# surface as a failing test, it would surface as a COMPILE ERROR. So the rebind here is scoped to ONLY the
# SelfTestOps method body — the one method Mutants.cs actually runs for these two mutants. Every other
# method, including SelfTestUndo, is left calling the real BattleGridOps/BattleGridStroke and still compiles.
foreach ($bm in @('MutFirstTouch', 'MutFillDiagonal')) {
  $t = $battleTests -replace 'namespace WorldGen\.Rendering', 'namespace WorldGen.MutantTests'
  $t = $t -replace 'class BattleGridSelfTests', "class ${bm}SelfTests"

  $startIdx = $t.IndexOf('public void SelfTestOps()')
  if ($startIdx -lt 0) { throw "SelfTestOps method not found while deriving SelfTests_$bm.cs" }
  $endIdx = $t.IndexOf('[ContextMenu', $startIdx)
  if ($endIdx -lt 0) { throw "no ContextMenu marker after SelfTestOps while deriving SelfTests_$bm.cs" }

  $before = $t.Substring(0, $startIdx)
  $method = $t.Substring($startIdx, $endIdx - $startIdx)
  $after  = $t.Substring($endIdx)

  # Same "prepend, don't replace" fix as BattleGridGenerator above — New-BattleMutant nests only the
  # namespace, so the class name BattleGridOps (and the struct name BattleGridStroke, referenced bare)
  # must both survive the rebind.
  if ($method -notmatch 'BattleGridOps\.') { throw "SelfTestOps has no BattleGridOps. call to rebind for $bm" }
  $method = $method -replace 'BattleGridOps\.', "WorldGen.Generation.$bm.BattleGridOps."
  if ($method -notmatch '\bBattleGridStroke\b') { throw "SelfTestOps has no bare BattleGridStroke to rebind for $bm" }
  $method = $method -replace '\bBattleGridStroke\b', "WorldGen.Generation.$bm.BattleGridStroke"

  Set-Content -Path (Join-Path $gen "SelfTests_$bm.cs") -Value ($before + $method + $after) -Encoding UTF8
}

# ---- SETTLEMENT MUTANTS: four rules pinned by SettlementGenerator/SettlementStreets. -----------------------
# Same discipline as New-BattleMutant: read straight from the real source, throw if the pattern is gone,
# re-namespace (class name unchanged, only nested), write to gen/.
function New-SettlementMutant([string]$srcFile, [string]$className, [string]$from, [string]$to, [string]$outFile) {
  $t = Get-Content (Join-Path $src $srcFile) -Raw -Encoding UTF8
  if ($t -notmatch [regex]::Escape($from)) { throw "mutant $outFile : pattern not found: $from" }
  $t = $t -replace [regex]::Escape($from), $to
  $t = $t -replace 'namespace WorldGen.Generation', "namespace WorldGen.Generation.$className"
  Set-Content (Join-Path $gen $outFile) $t -Encoding UTF8
}

# MutNoInsideFilter: PlaceBuildings' keep condition drops the wall.Contains(cx, cy) term, so bbox-corner
# cells outside the rounded contour — but still >= half a cell from the nearest edge line, since a rounded
# nonagon's corners sit well clear of the circle — leak into the kept set. SelfTestBuildings case 1 (every
# building inside the wall) must fail.
New-SettlementMutant 'SettlementGenerator.cs' 'MutNoInsideFilter' `
  'if (wall.Contains(cx, cy) && wall.DistanceToEdge(cx, cy) >= half)' `
  'if (wall.DistanceToEdge(cx, cy) >= half)' `
  'MutNoInsideFilter.cs'

# MutNoWallClearance: PlaceBuildings' keep condition drops the wall.DistanceToEdge(...) >= half term, so
# cells hugging the wall line from the inside (closer than half a cell) are kept. SelfTestBuildings case 3
# (every building >= half a cell from the wall line) must fail.
New-SettlementMutant 'SettlementGenerator.cs' 'MutNoWallClearance' `
  'if (wall.Contains(cx, cy) && wall.DistanceToEdge(cx, cy) >= half)' `
  'if (wall.Contains(cx, cy))' `
  'MutNoWallClearance.cs'

# MutNoInsideFilter/MutNoWallClearance/MutGateAtCentre all nest the WHOLE of SettlementGenerator.cs — including
# its own BuildFloor/Generate methods, which internally call SettlementStreets.GenerateStreets(placement,
# buildings, gates, cfg.Seed). SettlementStreets.cs is NOT mutated for these three, so that call's parameter
# types (PlacedBuilding/GatePoint) still mean the REAL, top-level WorldGen.Generation ones — but BuildFloor's
# local `buildings`/`gates` are now the NESTED PlacedBuilding/GatePoint (PlaceBuildings/PlaceGates are declared
# in the SAME file, so they return the nested types once the file is renamespaced). IReadOnlyList<T> does not
# covary over a struct T, so this is a hard COMPILE ERROR baked into the mutant source itself, unrelated to
# whichever rule the mutant actually changes. None of the three catching tests (SelfTestBuildings x2,
# SelfTestGates) call BuildFloor/Generate at all, so the fix stubs out that one dead call rather than touching
# the rule under test.
function Repair-SettlementGeneratorCrossFileCall([string]$outFile) {
  $p = Join-Path $gen $outFile
  $t = Get-Content $p -Raw -Encoding UTF8
  $from = 'var edges = SettlementStreets.GenerateStreets(placement, buildings, gates, cfg.Seed);'
  if ($t -notmatch [regex]::Escape($from)) { throw "cross-file repair pattern not found in $outFile" }
  $stub = 'var edges = new List<StreetEdge>();   // MUTANT-NEST STUB: BuildFloor/Generate are dead code for this mutants catching test, and SettlementStreets.cs is unmutated here so its GenerateStreets cannot accept this nested PlacedBuilding/GatePoint'
  $t = $t -replace [regex]::Escape($from), $stub
  Set-Content -Path $p -Value $t -Encoding UTF8
}

# MutGateAtCentre: PointAtArcLength returns the wall centre (0.5,0.5) — every wall contour the self-tests
# build (WallContour.Rounded(cfg.Seed, 0.5f, 0.5f, ...) — BuildWall itself was removed in Task 9, this was its
# exact formula) centres there — instead of the arc-length-interpolated point, so every gate collapses onto
# the same spot in the middle of town. SelfTestGates case 4 (every gate lies ON the wall line) must fail.
New-SettlementMutant 'SettlementGenerator.cs' 'MutGateAtCentre' `
  'return new GatePoint { X = a.X + t * (b.X - a.X), Y = a.Y + t * (b.Y - a.Y) };' `
  'return new GatePoint { X = 0.5f, Y = 0.5f };   // MUTANT: gate at wall centre' `
  'MutGateAtCentre.cs'

# MutNoActiveMark: BuildFloor's active/dummy marking neutered — every building stays active (IsDummy always
# false) regardless of ActiveBuildings/placed count. SelfTestActiveBuildings' exact active/dummy split
# (assertion 1, plus the wall-less-camp check in part 4) must fail.
New-SettlementMutant 'SettlementGenerator.cs' 'MutNoActiveMark' `
  'IsDummy = i >= activeCount' `
  'IsDummy = false' `
  'MutNoActiveMark.cs'

foreach ($outFile in @('MutNoInsideFilter.cs', 'MutNoWallClearance.cs', 'MutGateAtCentre.cs', 'MutNoActiveMark.cs')) {
  Repair-SettlementGeneratorCrossFileCall $outFile
}

# MutStreetsNoGrowth: GenerateStreets' Prim-style spanning-growth loop is disabled (its while condition forced
# false), leaving only the gate-to-farthest-building trunks — most buildings never get an edge at all.
# SelfTestStreets case 2 (every building reachable from some gate) must fail.
New-SettlementMutant 'SettlementStreets.cs' 'MutStreetsNoGrowth' `
  'while (remaining > 0)' `
  'while (false)   // MUTANT: Prim growth disabled, trunks only' `
  'MutStreetsNoGrowth.cs'

# MutStreetsNoHub: in the gate-less (nGates == 0) branch, the hub building is never marked connected —
# `remaining` is still set to nBuild - 1, but the growth loop's outer scan finds no connected node to grow
# from, so it breaks on its first iteration and every building stays isolated. SelfTestVillage assertion 3
# (hub connectivity) must fail.
New-SettlementMutant 'SettlementStreets.cs' 'MutStreetsNoHub' `
  'connected[hub] = true;' `
  '// MUTANT: hub never marked connected — gate-less growth seeds from nothing' `
  'MutStreetsNoHub.cs'

# MutRoadsNoAvoid: SettlementRoads' obstacle mask never marks a cell — A* routes straight through
# houses. SelfTestRoads assertion 2 (no segment enters a foreign rect) must fail.
New-SettlementMutant 'SettlementRoads.cs' 'MutRoadsNoAvoid' `
  'blocked[(y - minY) * gw + (x - minX)] = true;' `
  'blocked[(y - minY) * gw + (x - minX)] = false;   // MUTANT: mask never set' `
  'MutRoadsNoAvoid.cs'

# MutRoadsNoReuse: the reuse discount removed — every road pays full price everywhere, so branches
# take their own parallel lanes instead of merging into arterials. SelfTestRoadJunctions' >=3-shared-
# cell lane stretch must fail.
New-SettlementMutant 'SettlementRoads.cs' 'MutRoadsNoReuse' `
  'float step = roads.Contains(nc) ? RoadReuseFactor : 1f;' `
  'float step = 1f;   // MUTANT: no reuse discount' `
  'MutRoadsNoReuse.cs'

# MutRoadsNoClearance: the obstacle-mask inflation drops its + RoadClearanceTiles term, so a road may hug
# a building right up to its bare rect (0 clearance) instead of keeping RoadClearanceTiles away.
# SelfTestRoads assertion 5 (every routed cell keeps >= RoadClearanceTiles from every building) must fail.
New-SettlementMutant 'SettlementRoads.cs' 'MutRoadsNoClearance' `
  'float hw = n.W * 0.5f + RoadClearanceTiles, hh = n.H * 0.5f + RoadClearanceTiles;' `
  'float hw = n.W * 0.5f, hh = n.H * 0.5f;   // MUTANT: no clearance inflation' `
  'MutRoadsNoClearance.cs'

# MutRoadsNoArterials: SettlementStreets' gate-gate arterial pass skipped — gates fall back to being
# mere seed points. SelfTestStreets' arterial-net span assertion must fail.
New-SettlementMutant 'SettlementStreets.cs' 'MutRoadsNoArterials' `
  'if (nGates > 1)' `
  'if (false)   // MUTANT: no gate-gate arterials' `
  'MutRoadsNoArterials.cs'

# ---- SETTLEMENT MUTANT-BOUND SELF-TESTS ---------------------------------------------------------------------
# SettlementGenerator.cs bundles FOUR types into one file: SettlementConfig, GatePoint, PlacedBuilding AND
# SettlementGenerator. Re-namespacing it for a mutant moves all four together. SelfTestGates/SelfTestBuildings
# both name SettlementConfig explicitly (GatePoint/PlacedBuilding never are — every call site captures them
# through `var`, exactly like BattleGridGenerator's GridPoint above), so their rebind must cover both
# SettlementGenerator. and SettlementConfig — but ONLY within the one method Mutants.cs actually runs. A
# blanket file-wide rebind would ALSO retype SelfTestStreets/SelfTestAssembly, which hand the (now-mutant)
# GatePoint/PlacedBuilding lists to SettlementStreets.GenerateStreets — a method whose parameter types stay
# the REAL WorldGen.Generation.GatePoint/PlacedBuilding when SettlementStreets.cs is not itself mutated.
# IReadOnlyList<T> does not covary over a struct T, so that mismatch would surface as a COMPILE ERROR, not a
# failing assertion — the same trap BattleGridOps's SelfTestOps scoping (above) exists to dodge.
$settlementTests = Get-Content (Join-Path $src 'SettlementSelfTests.cs') -Raw -Encoding UTF8
# Same rebind shape, second source: InteriorOps' mutant (below) is caught via SelfTestInteriorOps, which lives
# in InteriorOpsSelfTests.cs, not SettlementSelfTests.cs.
$interiorTests = Get-Content (Join-Path $src 'InteriorOpsSelfTests.cs') -Raw -Encoding UTF8
# Third source: the upper-floor-wall-gap mutant (below) is caught via SelfTestBuilding, which lives in
# BuildingGeneratorSelfTests.cs, not SettlementSelfTests.cs or InteriorOpsSelfTests.cs.
$buildingTests = Get-Content (Join-Path $src 'BuildingGeneratorSelfTests.cs') -Raw -Encoding UTF8
# Fourth source: the tile-grid wall-ring mutants (below) are caught via SelfTestWallRing, which lives in
# SettlementTileGridSelfTests.cs, not any of the three files above.
$tileGridTests = Get-Content (Join-Path $src 'SettlementTileGridSelfTests.cs') -Raw -Encoding UTF8
# Fifth source: the block-generation mutants (below) are caught via SelfTestBlocks, which lives in
# SettlementBlocksSelfTests.cs.
$blocksTests = Get-Content (Join-Path $src 'SettlementBlocksSelfTests.cs') -Raw -Encoding UTF8
# Sixth source: the POI-migration mutant (below) is caught via SelfTestPoiLegacyTypes, which lives in
# PoiMigrationSelfTests.cs, not any of the five files above.
$poiMigrationTests = Get-Content (Join-Path $src 'PoiMigrationSelfTests.cs') -Raw -Encoding UTF8

function New-SettlementRebind([string]$methodName, [string]$mutantClass, [string[]]$rebindPatterns, [string[]]$rebindTo) {
  $marker = "public void $methodName()"
  # Pick whichever source file actually defines this [ContextMenu] method — every existing caller's method
  # lives in $settlementTests, so this stays a no-op for them; SelfTestInteriorOps falls through to
  # $interiorTests, SelfTestBuilding falls through to $buildingTests, SelfTestWallRing falls through to
  # $tileGridTests, and SelfTestPoiLegacyTypes falls through to $poiMigrationTests.
  $srcText = $settlementTests
  $origClass = 'SettlementSelfTests'
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $interiorTests
    $origClass = 'InteriorOpsSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $buildingTests
    $origClass = 'BuildingGeneratorSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $tileGridTests
    $origClass = 'SettlementTileGridSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $blocksTests
    $origClass = 'SettlementBlocksSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $poiMigrationTests
    $origClass = 'PoiMigrationSelfTests'
  }
  $t = $srcText -replace 'namespace WorldGen\.Rendering', 'namespace WorldGen.MutantTests'
  $t = $t -replace "class $origClass", "class ${mutantClass}SelfTests"

  $startIdx = $t.IndexOf($marker)
  if ($startIdx -lt 0) { throw "$methodName not found while deriving SelfTests_$mutantClass.cs" }
  $endIdx = $t.IndexOf('[ContextMenu', $startIdx)
  if ($endIdx -lt 0) { throw "no ContextMenu marker after $methodName while deriving SelfTests_$mutantClass.cs" }

  $before = $t.Substring(0, $startIdx)
  $method = $t.Substring($startIdx, $endIdx - $startIdx)
  $after  = $t.Substring($endIdx)

  # Guard against a silent truncation: the [ContextMenu marker scan above is comment-blind, so if the
  # literal substring "[ContextMenu" ever appears inside THIS method's own body (including inside a //
  # comment), $endIdx lands early and $method is cut off mid-body. A truncated method can still happen
  # to compile, so nothing downstream would notice - the mutant would report CAUGHT while actually
  # testing UNMUTATED code, a false pass with no signal anywhere in the harness output. Brace-balance
  # plus a '}' terminator is a cheap, reliable proxy for "this is a complete method body" - but $method
  # legitimately runs past its own closing brace into the NEXT method's leading // or /// comment (the
  # real [ContextMenu match sits after that comment), so the terminator check below is applied to a
  # copy with trailing comment/blank lines stripped, not to $method itself.
  $openBraces = ([regex]::Matches($method, '\{')).Count
  $closeBraces = ([regex]::Matches($method, '\}')).Count
  $codeTail = ($method -replace '(?s)(\s*//[^\r\n]*\r?\n)+\s*$', '').TrimEnd()
  $lastChar = if ($codeTail.Length -gt 0) { $codeTail[-1] } else { '<empty>' }
  if ($openBraces -ne $closeBraces -or $codeTail.Length -eq 0 -or $lastChar -ne '}') {
    throw "$methodName extraction truncated while deriving SelfTests_$mutantClass.cs: braces $openBraces open / $closeBraces close, code (comments stripped) ends with '$lastChar' instead of '}'. This almost certainly means the literal '[ContextMenu' appears inside $methodName's own body or a comment within it, so the scan for the next method's [ContextMenu marker stopped early and cut $methodName off mid-body. A truncated extraction can still compile, so it would otherwise silently produce a mutant that reports CAUGHT while actually testing UNMUTATED code - fix the offending text in $methodName so '[ContextMenu' does not appear inside its body."
  }

  for ($i = 0; $i -lt $rebindPatterns.Count; $i++) {
    if ($method -notmatch $rebindPatterns[$i]) { throw "$methodName has no match for '$($rebindPatterns[$i])' to rebind for $mutantClass" }
    $method = $method -replace $rebindPatterns[$i], $rebindTo[$i]
  }

  Set-Content -Path (Join-Path $gen "SelfTests_$mutantClass.cs") -Value ($before + $method + $after) -Encoding UTF8
}

# MutNoInsideFilter / MutNoWallClearance both mutate PlaceBuildings and are caught by SelfTestBuildings, which
# never touches SettlementStreets — safe to rebind SettlementGenerator./SettlementConfig within just this method.
foreach ($mc in @('MutNoInsideFilter', 'MutNoWallClearance')) {
  New-SettlementRebind 'SelfTestBuildings' $mc `
    @('SettlementGenerator\.', '\bSettlementConfig\b') `
    @("WorldGen.Generation.$mc.SettlementGenerator.", "WorldGen.Generation.$mc.SettlementConfig")
}

# MutGateAtCentre mutates PointAtArcLength and is caught by SelfTestGates, which likewise never touches
# SettlementStreets.
New-SettlementRebind 'SelfTestGates' 'MutGateAtCentre' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutGateAtCentre.SettlementGenerator.', 'WorldGen.Generation.MutGateAtCentre.SettlementConfig')

# MutStreetsNoGrowth mutates GenerateStreets (SettlementStreets.cs, which does NOT bundle SettlementConfig/
# GatePoint/PlacedBuilding/SettlementGenerator — those stay real). Caught by SelfTestStreets: its wall/gates/
# buildings must keep coming from the REAL SettlementGenerator (so PlaceBuildings/PlaceGates still run), but
# its edges must come from the MUTATED SettlementStreets — so ONLY "SettlementStreets." is rebound here.
New-SettlementRebind 'SelfTestStreets' 'MutStreetsNoGrowth' `
  @('SettlementStreets\.') `
  @('WorldGen.Generation.MutStreetsNoGrowth.SettlementStreets.')

# MutStreetsNoHub mutates GenerateStreets' gate-less branch. SelfTestVillage's assertions 1/2/4 go through
# SettlementGenerator.Generate()/BuildFloor — which lives in the PRISTINE, never-rebound SettlementGenerator.cs
# copy and so always calls the REAL SettlementStreets — so a mutant of SettlementStreets.cs alone can only be
# observed through a call SelfTestVillage makes to SettlementStreets DIRECTLY (its assertion-3 reconstruction:
# exPlacement/exBuildings via the real SettlementGenerator, exEdges via SettlementStreets.GenerateStreets).
# That is exactly the same shape as SelfTestStreets/MutStreetsNoGrowth above — so ONLY "SettlementStreets." is
# rebound here too; SettlementConfig/SettlementGenerator stay real.
New-SettlementRebind 'SelfTestVillage' 'MutStreetsNoHub' `
  @('SettlementStreets\.') `
  @('WorldGen.Generation.MutStreetsNoHub.SettlementStreets.')

# MutNoActiveMark mutates BuildFloor's active/dummy marking directly and is caught by SelfTestActiveBuildings,
# which calls SettlementGenerator.Generate()/BuildFloor (so the same cross-file-call stub as MutNoInsideFilter/
# MutNoWallClearance/MutGateAtCentre above applies — BuildFloor's GenerateStreets call is stubbed to an empty
# edge list, which is harmless here: SelfTestActiveBuildings reads only floor.Rooms and floor.SettlementParams,
# never floor.Links) and never touches SettlementStreets directly — safe to rebind SettlementGenerator./
# SettlementConfig within just this method, exactly like MutNoInsideFilter/MutNoWallClearance above.
New-SettlementRebind 'SelfTestActiveBuildings' 'MutNoActiveMark' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutNoActiveMark.SettlementGenerator.', 'WorldGen.Generation.MutNoActiveMark.SettlementConfig')

# SettlementRoads.cs defines ONE class and no data types — its LinkNode/LinkEdge/LinkGeometry
# parameters resolve outward to the REAL WorldGen.Generation types after re-namespacing, so a rebind
# of just "SettlementRoads." is sound (no IReadOnlyList<T> struct-covariance trap here; the fixture
# still comes from the real SettlementGenerator).
New-SettlementRebind 'SelfTestRoads' 'MutRoadsNoAvoid' `
  @('SettlementRoads\.') `
  @('WorldGen.Generation.MutRoadsNoAvoid.SettlementRoads.')

# A SECOND rebind of SelfTestRoads (separate SelfTests_<class>.cs output) — MutRoadsNoClearance is caught
# by the SAME method's building-clearance assertion (5), not by a different one.
New-SettlementRebind 'SelfTestRoads' 'MutRoadsNoClearance' `
  @('SettlementRoads\.') `
  @('WorldGen.Generation.MutRoadsNoClearance.SettlementRoads.')

New-SettlementRebind 'SelfTestRoadJunctions' 'MutRoadsNoReuse' `
  @('SettlementRoads\.') `
  @('WorldGen.Generation.MutRoadsNoReuse.SettlementRoads.')

# Same shape as MutStreetsNoGrowth: only SettlementStreets. is rebound; the generator stays real.
New-SettlementRebind 'SelfTestStreets' 'MutRoadsNoArterials' `
  @('SettlementStreets\.') `
  @('WorldGen.Generation.MutRoadsNoArterials.SettlementStreets.')

# ---- SETTLEMENT FENCE MUTANTS: three rules pinned by SettlementFence.Derive (the three fence mutants). -----
# Same discipline as New-SettlementMutant. SettlementFence.cs defines ONE class and no data types
# (LinkNode/LinkSegment/LinkPoint live in RoomLinkGeometry.cs, WallContour/WallPoint in WallContour.cs,
# neither mutated here), so re-namespacing it and rebinding just "SettlementFence." is sound — the same
# single-class shape SettlementRoads/InteriorOps use above.

# MutFenceNoFill: InsideFromOutsideFill's final classification collapsed to the raw pre-fill town raster —
# the outside BFS still runs but its result is discarded, so any enclosed empty pocket (never itself
# rasterized as town) stays a literal hole instead of being kept inside (Rule 1). Every OTHER fixture
# (A/C/D/E/F) has NO enclosed pocket at all, so town == inside already for them and this mutant is silent
# there — only fixture B's donut centre diverges: it now traces as a SEPARATE inner loop, which
# TraceBoundary's single-loop guard refuses (corners.Count != next.Count), so Derive returns null and
# fixture B's null/not-sane check fires.
New-SettlementMutant 'SettlementFence.cs' 'MutFenceNoFill' `
  'inside[i] = !outside[i];' `
  'inside[i] = town[i];   // MUTANT: no-op flood fill — inside collapses to the raw town raster, so an enclosed pocket stays a hole' `
  'MutFenceNoFill.cs'

# MutFenceNoGates: the gate-cell rasterization write neutered — a gate's centre cell is never marked town, so
# the fence no longer hugs it. SelfTestFence fixture C's gate-distance assertion (a gate must sit within 1.5
# tiles of the fence) must fail.
New-SettlementMutant 'SettlementFence.cs' 'MutFenceNoGates' `
  'town[(gy - minY) * gw + (gx - minX)] = true;   // a POINT, no inflation (see class doc)' `
  ';   // MUTANT: gate cell never rasterized' `
  'MutFenceNoGates.cs'

# MutFenceNoRoads: the road-ribbon rasterization call skipped — a routed road never marks any cell, so it can
# no longer pull an otherwise-empty gap inside the fence. Caught by the hardened fixture F: its far spur point
# is inside ONLY via the road (one connected building cluster, so BridgeStrays never runs); with this mutant
# it reverts to OUTSIDE even with the road present.
New-SettlementMutant 'SettlementFence.cs' 'MutFenceNoRoads' `
  'RasterizeRoad(town, gw, gh, minX, minY, rd.A, rd.B, marginTiles);' `
  ';   // MUTANT: road never rasterized' `
  'MutFenceNoRoads.cs'

New-SettlementRebind 'SelfTestFence' 'MutFenceNoFill' `
  @('SettlementFence\.') `
  @('WorldGen.Generation.MutFenceNoFill.SettlementFence.')

New-SettlementRebind 'SelfTestFence' 'MutFenceNoGates' `
  @('SettlementFence\.') `
  @('WorldGen.Generation.MutFenceNoGates.SettlementFence.')

New-SettlementRebind 'SelfTestFence' 'MutFenceNoRoads' `
  @('SettlementFence\.') `
  @('WorldGen.Generation.MutFenceNoRoads.SettlementFence.')

# MutNoOwnedCleanup: InteriorOps' single-node RemoveOwnedInteriors(all, poiId, roomId) overload always returns
# 0 — a building node's deleted interior is never cleaned up. SelfTestInteriorOps' exact-1-removed assertion
# (and its town/sibling/foreign-interior survivor checks) must fail.
New-SettlementMutant 'InteriorOps.cs' 'MutNoOwnedCleanup' `
  'return all.RemoveAll(d => d != null && d.OwnerPoiId == poiId && d.OwnerRoomId == roomId);' `
  'return 0;   // MUTANT: node deletion never cleans the owned interior' `
  'MutNoOwnedCleanup.cs'

# InteriorOps.cs defines ONE class and no data types (InteriorData lives in DungeonData.cs, unmutated) — the
# same sound-rebind shape as SettlementRoads, so a rebind of just "InteriorOps." is sound.
New-SettlementRebind 'SelfTestInteriorOps' 'MutNoOwnedCleanup' `
  @('InteriorOps\.') `
  @('WorldGen.Generation.MutNoOwnedCleanup.InteriorOps.')

# ---- FLOOR FOOTPRINT MUTANT: the building wall must wrap corridors, not just rooms. -------------------------
# MutFootprintNoCorridors: ExpandedRects' corridor guard forced false, so the routed corridor legs are never
# folded into the footprint arrangement — the contour reverts to the room-union shape and cuts across a
# corridor that bows outside it. Caught by SelfTestBuildingFootprintCorridors' midpoint-inside assertion (the
# corridor midpoint, which sits in the open gap, now reads OUTSIDE the rooms+corridor footprint). FloorFootprint.cs
# defines ONE class and no data types (InteriorFloor/LinkSegment live elsewhere, unmutated and resolved outward),
# so the single-class New-SettlementMutant / rebind-only-"FloorFootprint." shape (SettlementRoads/InteriorOps) is sound.
New-SettlementMutant 'FloorFootprint.cs' 'MutFootprintNoCorridors' `
  'if (corridors != null)' `
  'if (false)   // MUTANT: routed corridor legs never folded into the footprint arrangement' `
  'MutFootprintNoCorridors.cs'

New-SettlementRebind 'SelfTestBuildingFootprintCorridors' 'MutFootprintNoCorridors' `
  @('FloorFootprint\.') `
  @('WorldGen.Generation.MutFootprintNoCorridors.FloorFootprint.')

# ---- UPPER-FLOOR WALL-GAP MUTANT: an upper floor's packer must stay UpperFloorWallGapTiles inside the drawn
# contour, not flush against it. -----------------------------------------------------------------------------
# MutUpperFloorNoGap: both BuildingGenerator call sites that size/bound the upper-floor packer
# (MaxRoomsByArea's usable-area call and GenerateFloorAroundColumn's PackAroundColumnWithinFootprint call)
# reverted from the reduced margin back to the FULL FloorFootprint.ContourMargin — the pre-fix flush bug. Both
# sites spell the reduced margin with the SAME literal expression, so one pattern/replacement reverts both at
# once (PowerShell -replace is global by default). Caught by BuildingGeneratorSelfTests' wall-gap assertion
# (SelfTestBuilding, section 14): a flush-packed upper-floor room, inflated by the gap, pokes past the
# full-margin contour. BuildingGenerator.cs defines ONE class and no data types, so the single-class
# New-SettlementMutant / rebind-only-"BuildingGenerator." shape (SettlementRoads/InteriorOps/FloorFootprint) is
# sound.
New-SettlementMutant 'BuildingGenerator.cs' 'MutUpperFloorNoGap' `
  'FloorFootprint.ContourMargin - UpperFloorWallGapTiles' `
  'FloorFootprint.ContourMargin' `
  'MutUpperFloorNoGap.cs'

New-SettlementRebind 'SelfTestBuilding' 'MutUpperFloorNoGap' `
  @('BuildingGenerator\.') `
  @('WorldGen.Generation.MutUpperFloorNoGap.BuildingGenerator.')

# ---- TILE GRID WALL-RING MUTANTS: three rules pinned by SettlementTileGrid.Build's wall-ring pass. ----------
# SettlementTileGrid.cs bundles TWO types in one namespace block: enum TileType AND class SettlementTileGrid
# (the same bundling shape as BattleGridGenerator.cs/GridPoint and SettlementGenerator.cs/SettlementConfig
# above) — renamespacing the file for a mutant moves TileType into the nested mutant namespace too, so its
# rebind needs a SECOND pattern (bare `\bTileType\b`) alongside `SettlementTileGrid\.`, exactly like
# BattleGridOps's `\bBattleGridStroke\b` and SettlementGenerator's `\bSettlementConfig\b` above — without it,
# `g.At(1,1) != TileType.Void` in the rebound test would compare the mutant's nested TileType against the
# real WorldGen.Generation.TileType (via `using WorldGen.Generation;`), a hard CS0019 nominal-type mismatch,
# not a failing assertion. InteriorFloor/Room/LinkSegment live in unmutated files and resolve outward, so no
# further rebind or cross-file stub is needed here (no IReadOnlyList<T> struct-covariance trap).

# MutTileGridNoFloodFill: the Outside->Inside CONSUMER line neutered — inside collapses to the raw pre-fill
# occupied raster, so an enclosed pocket stays a literal hole (None) instead of Void — the fence arc's
# MutFenceNoFill mutation (`inside[i] = town[i]`) mirrored exactly onto this file's naming (`inside[a, b] =
# occupied[a, b]`). This mutation was vacuous on the ORIGINAL 3x3-ring fixture alone: radius-2 dilation of
# that ring already covers its centre directly, so `occupied` and the correct post-fill `inside` are
# bit-identical there — there is no unoccupied-but-enclosed cell for it to corrupt. SelfTestWallRing's SECOND
# fixture (buildings on the perimeter of a 7x7 block) fixes that: its centre (3,3) is genuinely unoccupied
# and only Inside via the flood-fill actually walking around the ring, so this mutation now flips it to None
# and the fixture's dedicated assertion fires. Caught by SelfTestWallRing's 7x7-perimeter centre assertion.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridNoFloodFill' `
  'inside[a, b] = !outside[a, b];' `
  'inside[a, b] = occupied[a, b];   // MUTANT: outside flood-fill result never consulted — inside collapses to the raw pre-fill occupied raster' `
  'MutTileGridNoFloodFill.cs'

# MutTileGridNoWallRing: the Wall assignment neutered — 0 wall cells ever get written, so every cell that
# should be Wall falls through to the Void pass instead. Caught by SelfTestWallRing's Wall-ring assertion
# ((-2,1) reads Void, not Wall) and its Building->Void->Wall chain assertion.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridNoWallRing' `
  'g.Cells[a, b] = TileType.Wall;' `
  ';   // MUTANT: wall ring never assigned' `
  'MutTileGridNoWallRing.cs'

# MutTileGridNoVoid: the Void assignment neutered — the one-cell courtyard ring (and the enclosed centre) stay
# TileType.None instead of Void. Caught by SelfTestWallRing's enclosed-centre assertion (Void expected, gets
# None) and its Building->Void->Wall chain assertion (the courtyard cell reads None, not Void).
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridNoVoid' `
  'g.Cells[a, b] = TileType.Void;' `
  ';   // MUTANT: void ring never assigned' `
  'MutTileGridNoVoid.cs'

foreach ($mc in @('MutTileGridNoFloodFill', 'MutTileGridNoWallRing', 'MutTileGridNoVoid')) {
  New-SettlementRebind 'SelfTestWallRing' $mc `
    @('SettlementTileGrid\.', '\bTileType\b') `
    @("WorldGen.Generation.$mc.SettlementTileGrid.", "WorldGen.Generation.$mc.TileType")
}

# ---- TILE GRID ROADS/GATES MUTANTS (Task 3): two rules pinned by SettlementTileGrid.Build's road/gate pass. --
# Same bundling as the three wall-ring mutants above (TileType + SettlementTileGrid share the file), so the
# rebind needs the same two patterns.

# MutTileGridNoGates: the gate-reclassify write neutered — a gate room's nearest Wall ring cell never becomes
# TileType.Gate. Caught by SelfTestRoadsAndGates' west-wall-cell-is-Gate assertion (OVERRIDE 2).
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridNoGates' `
  'g.Cells[bestA, bestB] = TileType.Gate;' `
  ';   // MUTANT: gate reclassify never applied' `
  'MutTileGridNoGates.cs'

# MutTileGridRoadIgnoresBuilding: road marking's Building/Wall precedence guard dropped — a road cell
# overwrites whatever tile (even Building or Wall) already occupies that cell. Caught by SelfTestRoadsAndGates'
# building-precedence assertion (cell (0,1), the crossing road's own start sample, must stay Building). Named
# for the Building half specifically: the Wall half of the dropped guard is provably unreachable (every road
# cell is in BuildWallRing's dilation seed, so all its 4-neighbours are Inside and it can never itself be
# written Wall) — this mutant is caught purely via the Building-precedence assertion, never via a Wall one, so
# it is not evidence the Wall term is pinned by anything.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridRoadIgnoresBuilding' `
  'if (g.Cells[a, b] == TileType.Building || g.Cells[a, b] == TileType.Wall) continue;' `
  '' `
  'MutTileGridRoadIgnoresBuilding.cs'

# MutGridStreetsNotSeeded: the STREET mask is still marked Road (MarkRoads runs unchanged) but is no longer
# folded into the wall ring's occupied SEED, so the ring is dilated from the buildings alone. Caught by
# SelfTestRoadsAndGates' spur pair: the street spur runs 10 cells south of the building block, far outside the
# buildings-only blob, so its tip reads None instead of Road (MarkRoads' Inside test rejects it) and the cell
# CourtyardCells+1 beyond the tip reads None instead of Wall — the wall stops wrapping the streets.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGridStreetsNotSeeded' `
  'bool[,] inside = hasWall ? BuildWallRing(g, streetMask) : null;' `
  'bool[,] inside = hasWall ? BuildWallRing(g, null) : null;   // MUTANT: streets never folded into the ring seed' `
  'MutGridStreetsNotSeeded.cs'

foreach ($mc in @('MutTileGridNoGates', 'MutTileGridRoadIgnoresBuilding', 'MutGridStreetsNotSeeded')) {
  New-SettlementRebind 'SelfTestRoadsAndGates' $mc `
    @('SettlementTileGrid\.', '\bTileType\b') `
    @("WorldGen.Generation.$mc.SettlementTileGrid.", "WorldGen.Generation.$mc.TileType")
}

# ---- TILE GRID FOOTPRINT MUTANTS (arc A, task 2): a building is a FOOTPRINT of cells, not a point. ----------
# Same bundling as every other SettlementTileGrid mutant above (TileType + SettlementTileGrid share the file),
# so the rebind needs the same two patterns. SettlementFootprint.cs is NOT mutated here and is not in the same
# file, so `SettlementFootprint.` inside the renamespaced mutant resolves OUTWARD to the real one — which is
# exactly what these two mutants need (they call the real Representative to collapse a footprint).

# MutGridOneCellPerRoom: Build writes only the footprint's REPRESENTATIVE cell instead of every cell, i.e. the
# pre-footprint "one building = one tile" behaviour. Caught by SelfTestFootprintTiles' 2x3 fixture — the five
# non-representative cells read None instead of Building, and the Building count collapses from 6 to 1.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGridOneCellPerRoom' `
  'var fp = FootprintOf(r);' `
  'var fp = new System.Collections.Generic.List<(int i, int j)> { SettlementFootprint.Representative(FootprintOf(r)) };   // MUTANT: only the representative cell is drawn' `
  'MutGridOneCellPerRoom.cs'

# MutGridExtentIgnoresFootprint: Allocate folds only the footprint's REPRESENTATIVE cell into the grid extent,
# so a footprint reaching further than MarginCells past it falls outside the array — and because every write in
# SettlementTileGrid is InBounds-guarded, those cells are dropped SILENTLY with no error anywhere. Caught by
# SelfTestFootprintTiles' bar fixture, whose far cell sits MarginCells+3 east of the representative: InBounds
# reads false and At() returns None.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGridExtentIgnoresFootprint' `
  'foreach (var c in FootprintOf(r)) Fold(c.i, c.j);' `
  '{ var rep = SettlementFootprint.Representative(FootprintOf(r)); Fold(rep.i, rep.j); }   // MUTANT: extent folds one cell per room' `
  'MutGridExtentIgnoresFootprint.cs'

# MutFootprintNoNullFallback: FootprintOf's rule (a) — "no footprint -> one cell, derived from the room's
# point" — disabled. A generated town's rooms (Cells == null) still take the s_noCells short-circuit ABOVE
# this line unchanged, but with `cells.Count == 0` neutered the function falls all the way through to
# `return cells;`, handing back the shared EMPTY list instead of the one-cell fallback. Caught by
# SelfTestFootprintTiles fixture D (a room with no footprint at all): its point cell reads None instead of
# Building, and the Building count drops from 1 to 0.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutFootprintNoNullFallback' `
  'if (cells.Count == 0) return' `
  'if (false && cells.Count == 0) return' `
  'MutFootprintNoNullFallback.cs'

# MutFootprintStaleNotRederived: FootprintOf's rule (b) — "a single-cell footprint that disagrees with the
# room's point is STALE -> re-derived from the point" — disabled. A migrated/moved building's frozen
# one-cell footprint is trusted verbatim instead of being corrected, so the building would stop moving when
# dragged. Caught by SelfTestFootprintTiles fixture E: the footprint's stale cell (0,0) stays Building and
# the point cell (3,2) — where the building actually is — never gets one.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutFootprintStaleNotRederived' `
  'if (cells.Count == 1 && cells[0] != point)' `
  'if (false && cells.Count == 1 && cells[0] != point)' `
  'MutFootprintStaleNotRederived.cs'

foreach ($mc in @('MutGridOneCellPerRoom', 'MutGridExtentIgnoresFootprint', 'MutFootprintNoNullFallback', 'MutFootprintStaleNotRederived')) {
  New-SettlementRebind 'SelfTestFootprintTiles' $mc `
    @('SettlementTileGrid\.', '\bTileType\b') `
    @("WorldGen.Generation.$mc.SettlementTileGrid.", "WorldGen.Generation.$mc.TileType")
}

# ---- TILE GRID DEPTH MUTANT (Task 4): DepthKey's row-major sort broken. -------------------------------------
# Same bundling as the wall-ring/roads-gates mutants above (TileType + SettlementTileGrid share the file), so
# the rebind needs the same two patterns.

# MutDepthKeyNoRowSort: DepthKey rewritten COLUMN-major (i primary, j only a tie-break) instead of row-major —
# drops the "further south always draws later, regardless of column" invariant entirely. Caught by
# SelfTestDepth's NearOccludesFar sweep, and by its cross-column WallOccludesBuildingBehind pair — see that
# test's comments for why the same-column pair alone does not discriminate this mutant.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutDepthKeyNoRowSort' `
  'public static long DepthKey(int i, int j) => (long)j * 1_000_000 + i;' `
  'public static long DepthKey(int i, int j) => (long)i * 1_000_000 + j;   // MUTANT: column-major, drops row-major sort' `
  'MutDepthKeyNoRowSort.cs'

New-SettlementRebind 'SelfTestDepth' 'MutDepthKeyNoRowSort' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutDepthKeyNoRowSort.SettlementTileGrid.', 'WorldGen.Generation.MutDepthKeyNoRowSort.TileType')

# ---- TILE GRID HEIGHT MUTANT (Task 5): BuildingHeight's FNV term dropped. -------------------------------------
# SettlementTileGrid.cs still bundles TWO types (TileType + SettlementTileGrid) as documented above, but
# SelfTestHeight itself never references TileType (it only calls BuildingHeight/BuildingHeightMin/
# BuildingHeightMax/WallHeight), so — unlike SelfTestWallRing/SelfTestRoadsAndGates/SelfTestDepth above — its
# rebind needs only the single SettlementTileGrid\. pattern; including the unused \bTileType\b pattern would
# make New-SettlementRebind throw (no match in this method's body).

# MutHeightConstant: BuildingHeight's FNV term dropped — the function returns BuildingHeightMin unconditionally,
# so every room id maps to the SAME height. BuildingHeightMin is itself IN-RANGE, so SelfTestHeight's in-range
# loop cannot catch this; it's caught by the distinct-count ("varies") check collapsing to 1, the spread check
# collapsing to 0, and the id-7 pinned-value check (pinned != BuildingHeightMin) — see SelfTestHeight's own
# comments for why the plain determinism check (BuildingHeight(7) == BuildingHeight(7)) cannot.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutHeightConstant' `
  'return BuildingHeightMin + t * (BuildingHeightMax - BuildingHeightMin);' `
  'return BuildingHeightMin;   // MUTANT: FNV term dropped, height is constant' `
  'MutHeightConstant.cs'

New-SettlementRebind 'SelfTestHeight' 'MutHeightConstant' `
  @('SettlementTileGrid\.') `
  @('WorldGen.Generation.MutHeightConstant.SettlementTileGrid.')

# ---- FOOTPRINT MUTANTS (arc A, task 1): three rules pinned by SettlementFootprint. ---------------------------
# SettlementFootprint.cs defines ONE class and no data types — InteriorData/InteriorFloor/Room live in the
# unmutated DungeonData.cs and resolve OUTWARD once the file is re-namespaced — so the single-class
# New-SettlementMutant / rebind-only-"SettlementFootprint." shape (SettlementRoads/InteriorOps/FloorFootprint
# above) is sound here: no second bare type to rebind (unlike SettlementTileGrid's TileType) and no
# IReadOnlyList<T> struct-covariance trap (unlike SettlementGenerator's PlacedBuilding/GatePoint).

# MutFootprintNoConnectivity: IsConnected4's reached-vs-total comparison replaced by an unconditional true, so
# ANY cell set reads as one piece. Caught by SelfTestFootprint's diagonal-pair assertion — two cells touching
# only at a corner must NOT be 4-connected. The L / ring / translate assertions cannot catch it: all three
# expect true, which this mutant still returns.
New-SettlementMutant 'SettlementFootprint.cs' 'MutFootprintNoConnectivity' `
  'return seen.Count == all.Count;' `
  'return true;   // MUTANT: connectivity never actually checked' `
  'MutFootprintNoConnectivity.cs'

# MutFootprintRoundNotFloor: CellOf ROUNDS instead of flooring, so a cell stops being the half-open span
# [i*Pitch, (i+1)*Pitch) and each span is split between two indices. Caught twice by SelfTestFootprint: the
# CellOf(3.5*Pitch) assertion (Math.Round(3.5) is 4, not 3) and, independently, the CenterOf/CellOf round-trip
# at cell 7 (Math.Round(7.5) is 8). A BLOCK comment, not a line one: CellOf is an expression-bodied member, so
# a trailing // would swallow its own semicolon.
New-SettlementMutant 'SettlementFootprint.cs' 'MutFootprintRoundNotFloor' `
  '(int)System.Math.Floor(norm / Pitch)' `
  '(int)System.Math.Round(norm / Pitch) /* MUTANT: round, not floor */' `
  'MutFootprintRoundNotFloor.cs'

# MutMigrationSkipsFootprint: the v10 load normalization's single write neutered — a settlement building with
# no stored footprint stays footprint-less instead of getting its single-cell one at the cell its point falls
# in. Caught by SelfTestFootprintMigration's exactly-one-cell assertions on rooms 1 and 2. NOTE it does NOT
# exercise the no-overwrite branch (room 4) — that property is pinned by the assertion itself, which stores
# (9,9) on a room whose own point maps to (4,4), plus the second-pass idempotence sweep.
New-SettlementMutant 'SettlementFootprint.cs' 'MutMigrationSkipsFootprint' `
  'r.Cells = Encode(one);' `
  ';   // MUTANT: the load normalization writes no footprint' `
  'MutMigrationSkipsFootprint.cs'

New-SettlementRebind 'SelfTestFootprint' 'MutFootprintNoConnectivity' `
  @('SettlementFootprint\.') `
  @('WorldGen.Generation.MutFootprintNoConnectivity.SettlementFootprint.')

New-SettlementRebind 'SelfTestFootprint' 'MutFootprintRoundNotFloor' `
  @('SettlementFootprint\.') `
  @('WorldGen.Generation.MutFootprintRoundNotFloor.SettlementFootprint.')

New-SettlementRebind 'SelfTestFootprintMigration' 'MutMigrationSkipsFootprint' `
  @('SettlementFootprint\.') `
  @('WorldGen.Generation.MutMigrationSkipsFootprint.SettlementFootprint.')

# ---- BLOCK GENERATION MUTANTS (arc A, task 3): three rules pinned by SettlementBlocks.Generate. -----------
# SettlementBlocks.cs bundles TWO types in one namespace block — class BlockLayout AND class SettlementBlocks —
# the same bundling shape as SettlementTileGrid.cs/TileType and SettlementGenerator.cs/SettlementConfig above.
# Unlike those two, though, SelfTestBlocks NEVER NAMES BlockLayout: every layout it touches is captured through
# `var` (and its local Check(...) function takes only ints), exactly the way BattleGridGenerator's GridPoint is
# handled. So a rebind of just `SettlementBlocks\.` is sound and complete here — adding a `\bBlockLayout\b`
# pattern would make New-SettlementRebind throw for want of a match. WallContour / SettlementGenerator /
# SettlementFootprint all live in unmutated files and resolve OUTWARD once SettlementBlocks.cs is
# re-namespaced, and no IReadOnlyList<T> struct-covariance trap exists (the layout's lists are
# List<(int,int)>, a value tuple of ints, identical in both namespaces).

# MutBlocksNoRingStreet: the one-cell ring street just inside the wall is never laid — Generate takes an empty
# ring, so the whole interior becomes the subdividable core. The town then has only its subdivision strips for
# streets and, because PlaceGates opens a gate on a RING cell, no ring means NO GATES AT ALL. Caught by
# SelfTestBlocks assertion 4: the zero-gate check fires first and names the count, and the reachability sweep
# fires behind it (every street cell is unreachable when there is nothing to start from).
#
# NOTE, and it is a correction to this task's brief rather than an oversight: the brief predicted the
# street-ADJACENCY assertion would fire ("a building ends up flush against the wall with no street access").
# It cannot, and that is by design — FillBlock only ever SEEDS on a cell that already fronts a street, so
# removing the ring does not wall a building in, it simply leaves the outermost cells unbuilt. The invariant
# holds by construction; what the ring is load-bearing for is the town having a way IN.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksNoRingStreet' `
  'var ring = RingStreet(interior, interiorSet);' `
  'var ring = new List<(int i, int j)>();   // MUTANT: no ring street is laid at all' `
  'MutBlocksNoRingStreet.cs'

# MutBlocksNoSubdivision: every block is accepted as-is on its first visit, so the core is never cut and the
# whole interior comes out as ONE block. Caught by SelfTestBlocks assertion 5, which recovers the blocks as the
# 4-connected components of (interior minus streets) and requires each to be at or below BlockTargetCells — the
# single surviving block is several times that and the assertion names its exact cell count and bbox.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksNoSubdivision' `
  'if (block.Count <= BlockTargetCells) { blocks.Add(block); continue; }' `
  'if (true) { blocks.Add(block); continue; }   // MUTANT: never subdivide — one block for the whole interior' `
  'MutBlocksNoSubdivision.cs'

# MutBlocksOverlapAllowed: the fill's disjointness term is dropped from Available, so a cell already claimed by
# one building is handed to the next as well — every block cell seeds its own building and the grown rects
# overlap. Caught by SelfTestBlocks assertion 1, which names the shared cell and both buildings.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksOverlapAllowed' `
  '=> blockSet.Contains(c) && !claimed.Contains(c);' `
  '=> blockSet.Contains(c);   /* MUTANT: the fill skips its disjointness check */' `
  'MutBlocksOverlapAllowed.cs'

foreach ($mc in @('MutBlocksNoRingStreet', 'MutBlocksNoSubdivision', 'MutBlocksOverlapAllowed')) {
  New-SettlementRebind 'SelfTestBlocks' $mc `
    @('SettlementBlocks\.') `
    @("WorldGen.Generation.$mc.SettlementBlocks.")
}

# ---- POI MIGRATION MUTANT: the removed Village type must still normalize on load. ---------------------------
# PoiMigration.cs defines ONE class and no data types (PoiData/PoiType live in the unmutated PoiData.cs and
# resolve OUTWARD once this file is re-namespaced), so the single-class New-SettlementMutant / rebind-only-
# "PoiMigration." shape (SettlementFootprint/SettlementRoads/InteriorOps above) is sound here.

# MutPoiMigrationNoop: NormalizeLegacyTypes returns immediately, before ever inspecting a POI's type — a
# legacy-Village POI is never rewritten to City. Caught by SelfTestPoiLegacyTypes' legacy-Village assertion.
New-SettlementMutant 'PoiMigration.cs' 'MutPoiMigrationNoop' `
  'if (pois == null) return;' `
  'return;   // MUTANT: NormalizeLegacyTypes never runs' `
  'MutPoiMigrationNoop.cs'

New-SettlementRebind 'SelfTestPoiLegacyTypes' 'MutPoiMigrationNoop' `
  @('PoiMigration\.') `
  @('WorldGen.Generation.MutPoiMigrationNoop.PoiMigration.')

$variants = @('SpreadOnlyLayout', 'CompactOnlyLayout', 'CompactNoSlideLayout', 'CompactSlideNoCuts',
              'PreSlideLayout', 'PreSlideSpreadOnly', 'PreSlideCompactOnly', 'PreReviewLayout', 'NoPlainRunLayout')
Write-Host "synced $($files.Count) sources + $($variants.Count) variants + 10 mutants + 2 traces + 14 rebound test copies + 4 battle-grid mutants + 4 battle-grid rebound test copies + 30 settlement mutants + 30 settlement rebound test copies into gen/"

