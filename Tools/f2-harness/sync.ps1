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
  'WallContour.cs', 'SettlementGenerator.cs', 'SettlementFence.cs', 'SettlementSelfTests.cs',
  'InteriorOps.cs', 'InteriorOpsSelfTests.cs',
  'SettlementTileGrid.cs', 'SettlementTileGridSelfTests.cs', 'SettlementFootprint.cs',
  'SettlementBlocks.cs', 'SettlementBlocksSelfTests.cs',
  'SettlementStreetOps.cs', 'SettlementStreetOpsSelfTests.cs',
  'SettlementSizing.cs', 'SettlementMigration.cs',
  'PoiData.cs', 'PoiMigrationSelfTests.cs', 'PoiMigration.cs',
  'SettlementBrushOps.cs', 'SettlementBrushOpsSelfTests.cs',
  'SettlementUndo.cs', 'SettlementUndoSelfTests.cs'
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
  # Same reason, and the same both-sides normalisation, as New-SettlementMutant's — see the comment there.
  $t = $t -replace "`r`n", "`n"
  $from = $from -replace "`r`n", "`n"
  $to = $to -replace "`r`n", "`n"
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
  "Enqueue(px, py + 1); Enqueue(px, py - 1);`n                Enqueue(px + 1, py + 1); Enqueue(px + 1, py - 1); Enqueue(px - 1, py + 1); Enqueue(px - 1, py - 1);   // MUTANT: diagonal spread" `
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

# ---- SETTLEMENT MUTANTS: the rules pinned by SettlementGenerator's own self-tests. -------------------------
# Same discipline as New-BattleMutant: read straight from the real source, throw if the pattern is gone,
# re-namespace (class name unchanged, only nested), write to gen/.
function New-SettlementMutant([string]$srcFile, [string]$className, [string]$from, [string]$to, [string]$outFile) {
  $t = Get-Content (Join-Path $src $srcFile) -Raw -Encoding UTF8
  # NEWLINES NORMALIZED TO LF BEFORE ANY MATCHING, and this is load-bearing rather than tidy. A MULTI-LINE
  # locator has to agree with the file's line endings byte for byte, and those are NOT a property of the
  # repository: core.autocrlf is true here with no .gitattributes, so `git ls-files --eol` shows the index
  # holding LF while the working tree holds CRLF for most of these sources — and any `git checkout` of a file
  # re-materializes it as CRLF. That already broke MutEraseStaleCheck once, silently, and it would have broken
  # every multi-line settlement locator on a FRESH CLONE, since they are written with a forced `n. Normalizing
  # here makes a locator depend only on the source text, never on how the file happened to be checked out.
  #
  # NORMALISED ON BOTH SIDES, and that is what makes it safe for the locators that already exist. A locator
  # can be written two ways in this script: as a double-quoted string with an explicit `n (independent of
  # anything), or as a single-quoted string that literally spans lines in THIS file — whose newline is then
  # whatever sync.ps1 itself was saved with, i.e. CRLF. Normalising only the source would break every locator
  # of the second kind. Normalising both means either form matches either checkout, and no existing locator
  # had to be rewritten.
  $t = $t -replace "`r`n", "`n"
  $from = $from -replace "`r`n", "`n"
  $to = $to -replace "`r`n", "`n"
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

# NO CROSS-FILE REPAIR IS NEEDED ANY MORE (Task 5). The mutants that nest the WHOLE of SettlementGenerator.cs
# used to need one: BuildFloor called SettlementStreets.GenerateStreets(placement, buildings, gates, seed) with
# its own file-local — and therefore NESTED — PlacedBuilding/GatePoint, against a GenerateStreets whose
# parameter types stayed the REAL top-level ones (SettlementStreets.cs was never mutated alongside), and
# IReadOnlyList<T> does not covary over a struct T — a hard compile error baked into every such mutant source.
# Repair-SettlementGeneratorCrossFileCall stubbed that one dead call out. BuildFloor makes no cross-file call
# with those types now, so the function and its loop are gone; if a future edit reintroduces one, the mutant
# builds will fail loudly at compile time rather than silently.

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
  'IsDummy = !isActiveBuilding[i]' `
  'IsDummy = false' `
  'MutNoActiveMark.cs'

# MutActiveBuildingsPrefix: the DM-reported clustering bug, restored on purpose. The farthest-point pick is
# left computed (isActiveBuilding, activeGoal, activeRng, the whole FPS loop all still run — dead but
# harmless) and only the room's IsDummy line reverts to the old rule, "active" == "the first activeCount
# buildings in emission order" — which SettlementBlocks.Generate's block-by-block fill always packs into one
# corner of the town. Caught by SelfTestActiveBuildings section 6's farthest-point re-derivation: the prefix
# set it produces is (with overwhelming probability, verified for every swept seed) NOT the set the honest
# greedy farthest-point reference computes, so the exact-cell-set comparison fails.
New-SettlementMutant 'SettlementGenerator.cs' 'MutActiveBuildingsPrefix' `
  'IsDummy = !isActiveBuilding[i]' `
  'IsDummy = i >= activeCount   // MUTANT: active reverts to a prefix of emission order' `
  'MutActiveBuildingsPrefix.cs'

# MutActiveBuildingsFixedFirst: the seeded starting pick hardcoded to building 0, ignoring activeRng
# entirely — the exact "later pass simplifies the seemingly-unused RNG away" regression flagged on review.
# Every subsequent farthest-point pick still runs for real off that wrong start, so this is NOT the same
# failure as MutActiveBuildingsPrefix (the resulting set can still look "spread," just anchored at the wrong
# point) — it needs its own mutant. Caught by section 6's exact-cell-set comparison: the independent
# reference in the test genuinely calls activeRng.Next(...), so on any swept seed where the real random
# first pick is not building 0 (verified true for all six), production's fixed-start set diverges from the
# reference's honestly-random-start set.
New-SettlementMutant 'SettlementGenerator.cs' 'MutActiveBuildingsFixedFirst' `
  'int first = activeRng.Next(buildings.Count);' `
  'int first = 0;   // MUTANT: seeded starting pick hardcoded, activeRng never consulted' `
  'MutActiveBuildingsFixedFirst.cs'

# ---- SETTLEMENT MUTANT-BOUND SELF-TESTS ---------------------------------------------------------------------
# SettlementGenerator.cs bundles FOUR types into one file: SettlementConfig, GatePoint, PlacedBuilding AND
# SettlementGenerator. Re-namespacing it for a mutant moves all four together. SelfTestGates/SelfTestBuildings
# both name SettlementConfig explicitly (GatePoint/PlacedBuilding never are — every call site captures them
# through `var`, exactly like BattleGridGenerator's GridPoint above), so their rebind must cover both
# SettlementGenerator. and SettlementConfig — but ONLY within the one method Mutants.cs actually runs. The
# method-scoped rewrite is not a nicety: a blanket file-wide rebind would retype every OTHER test in the file
# too, and any of them that hands a (now-mutant) GatePoint/PlacedBuilding to a method in an UNMUTATED file
# would fail to compile rather than fail an assertion — IReadOnlyList<T> does not covary over a struct T.
# That is the same trap BattleGridOps's SelfTestOps scoping (above) exists to dodge.
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
# Seventh source: the street-access mutants (below) are caught via SelfTestStreetAccess, which lives in
# SettlementStreetOpsSelfTests.cs, not any of the six files above.
$streetOpsTests = Get-Content (Join-Path $src 'SettlementStreetOpsSelfTests.cs') -Raw -Encoding UTF8
# Eighth source: the brush mutants (below) are caught via SelfTestBrushStrokes, which lives in
# SettlementBrushOpsSelfTests.cs, not any of the seven files above.
$brushOpsTests = Get-Content (Join-Path $src 'SettlementBrushOpsSelfTests.cs') -Raw -Encoding UTF8
# Ninth source: the undo mutant (below) is caught via SelfTestSettlementUndo, which lives in
# SettlementUndoSelfTests.cs, not any of the eight files above.
$undoTests = Get-Content (Join-Path $src 'SettlementUndoSelfTests.cs') -Raw -Encoding UTF8

function New-SettlementRebind([string]$methodName, [string]$mutantClass, [string[]]$rebindPatterns, [string[]]$rebindTo) {
  $marker = "public void $methodName()"
  # Pick whichever source file actually defines this [ContextMenu] method — every existing caller's method
  # lives in $settlementTests, so this stays a no-op for them; SelfTestInteriorOps falls through to
  # $interiorTests, SelfTestBuilding falls through to $buildingTests, SelfTestWallRing falls through to
  # $tileGridTests, SelfTestPoiLegacyTypes falls through to $poiMigrationTests, SelfTestStreetAccess falls
  # through to $streetOpsTests, SelfTestBrushStrokes falls through to $brushOpsTests, and
  # SelfTestSettlementUndo falls through to $undoTests.
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
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $streetOpsTests
    $origClass = 'SettlementStreetOpsSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $brushOpsTests
    $origClass = 'SettlementBrushOpsSelfTests'
  }
  if ($srcText.IndexOf($marker) -lt 0) {
    $srcText = $undoTests
    $origClass = 'SettlementUndoSelfTests'
  }
  # Same both-sides newline normalisation the two mutant generators do (see New-SettlementMutant's comment for
  # why a locator must not depend on how a file was checked out). LATENT here rather than active — review
  # finding: all 111 rebind patterns in this script are currently single-line, so none of them can notice a
  # CRLF. Hardened anyway, because the ONE thing this sub-project learned three separate times is that the
  # trap is invisible until someone writes the first multi-line pattern, and then it fails as a confusing
  # "method not found" rather than as anything that points at line endings.
  $srcText = $srcText -replace "`r`n", "`n"
  for ($k = 0; $k -lt $rebindPatterns.Count; $k++) {
    $rebindPatterns[$k] = $rebindPatterns[$k] -replace "`r`n", "`n"
    $rebindTo[$k] = $rebindTo[$k] -replace "`r`n", "`n"
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

# MutNoInsideFilter / MutNoWallClearance both mutate PlaceBuildings and are caught by SelfTestBuildings —
# safe to rebind SettlementGenerator./SettlementConfig within just this method.
foreach ($mc in @('MutNoInsideFilter', 'MutNoWallClearance')) {
  New-SettlementRebind 'SelfTestBuildings' $mc `
    @('SettlementGenerator\.', '\bSettlementConfig\b') `
    @("WorldGen.Generation.$mc.SettlementGenerator.", "WorldGen.Generation.$mc.SettlementConfig")
}

# MutGateAtCentre mutates PointAtArcLength and is caught by SelfTestGates — same shape.
New-SettlementRebind 'SelfTestGates' 'MutGateAtCentre' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutGateAtCentre.SettlementGenerator.', 'WorldGen.Generation.MutGateAtCentre.SettlementConfig')

# MutNoActiveMark mutates BuildFloor's active/dummy marking directly and is caught by SelfTestActiveBuildings,
# which calls SettlementGenerator.Generate()/BuildFloor — safe to rebind SettlementGenerator./SettlementConfig
# within just this method, exactly like MutNoInsideFilter/MutNoWallClearance above.
New-SettlementRebind 'SelfTestActiveBuildings' 'MutNoActiveMark' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutNoActiveMark.SettlementGenerator.', 'WorldGen.Generation.MutNoActiveMark.SettlementConfig')

# MutActiveBuildingsPrefix is caught by the SAME method's farthest-point re-derivation (a second, separate
# SelfTests_<class>.cs output) — same shape as MutNoActiveMark just above.
New-SettlementRebind 'SelfTestActiveBuildings' 'MutActiveBuildingsPrefix' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutActiveBuildingsPrefix.SettlementGenerator.', 'WorldGen.Generation.MutActiveBuildingsPrefix.SettlementConfig')

# MutActiveBuildingsFixedFirst is caught by the SAME method's farthest-point re-derivation too (a third,
# separate SelfTests_<class>.cs output) — same shape again.
New-SettlementRebind 'SelfTestActiveBuildings' 'MutActiveBuildingsFixedFirst' `
  @('SettlementGenerator\.', '\bSettlementConfig\b') `
  @('WorldGen.Generation.MutActiveBuildingsFixedFirst.SettlementGenerator.', 'WorldGen.Generation.MutActiveBuildingsFixedFirst.SettlementConfig')

# ---- SETTLEMENT FENCE MUTANTS: three rules pinned by SettlementFence.Derive (the three fence mutants). -----
# Same discipline as New-SettlementMutant. SettlementFence.cs defines ONE class and no data types
# (LinkNode/LinkSegment/LinkPoint live in RoomLinkGeometry.cs, WallContour/WallPoint in WallContour.cs,
# neither mutated here), so re-namespacing it and rebinding just "SettlementFence." is sound — the same
# single-class shape InteriorOps uses below.

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
# same sound-rebind shape as SettlementFence above, so a rebind of just "InteriorOps." is sound.
New-SettlementRebind 'SelfTestInteriorOps' 'MutNoOwnedCleanup' `
  @('InteriorOps\.') `
  @('WorldGen.Generation.MutNoOwnedCleanup.InteriorOps.')

# ---- FLOOR FOOTPRINT MUTANT: the building wall must wrap corridors, not just rooms. -------------------------
# MutFootprintNoCorridors: ExpandedRects' corridor guard forced false, so the routed corridor legs are never
# folded into the footprint arrangement — the contour reverts to the room-union shape and cuts across a
# corridor that bows outside it. Caught by SelfTestBuildingFootprintCorridors' midpoint-inside assertion (the
# corridor midpoint, which sits in the open gap, now reads OUTSIDE the rooms+corridor footprint). FloorFootprint.cs
# defines ONE class and no data types (InteriorFloor/LinkSegment live elsewhere, unmutated and resolved outward),
# so the single-class New-SettlementMutant / rebind-only-"FloorFootprint." shape (SettlementFence/InteriorOps) is sound.
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
# New-SettlementMutant / rebind-only-"BuildingGenerator." shape (SettlementFence/InteriorOps/FloorFootprint) is
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

# ---- TILE GRID NARROW-SPUR MUTANT (Task 2 of the street-access arc): the narrow radius is neutered. ---------
# Same bundling as every other SettlementTileGrid mutant above (TileType + SettlementTileGrid share the file),
# so the rebind needs the same two patterns.

# MutSpurWideDilation: every street cell takes the wide radius again, so a connector corridor is back to five
# cells wide. SelfTestSpurWidth's three-cell column must fail.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutSpurWideDilation' `
  '                    if (isBuilding || HasBuildingWithin(g, a, b, OpenStreetNeighbourhood)) wide[a, b] = true;' `
  '                    if (true) wide[a, b] = true;   // MUTANT: every seed cell takes the wide radius' `
  'MutSpurWideDilation.cs'

New-SettlementRebind 'SelfTestSpurWidth' 'MutSpurWideDilation' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutSpurWideDilation.SettlementTileGrid.', 'WorldGen.Generation.MutSpurWideDilation.TileType')

# MutSpurWideDilationRoadsAndGates: the IDENTICAL mutation (same find/replace pair as MutSpurWideDilation just
# above) under a SECOND, distinct class — required because New-SettlementRebind writes exactly ONE output file
# per mutant class (gen/SelfTests_<mutantClass>.cs), keyed only by class name; a second
# New-SettlementRebind under the SAME 'MutSpurWideDilation' class name (tried and inspected before choosing
# this shape — task review, street-access arc) silently OVERWRITES the first call's file. The overwritten file
# still contains SelfTestSpurWidth as a method (carried through verbatim in $before/$after, since
# New-SettlementRebind's rebindPatterns are applied only to the ONE method it extracts), but with its body
# never rewritten to the nested mutant type — it keeps calling the REAL SettlementTileGrid and passes
# regardless of the mutation, exactly the silently-vacuous-claim defect this task review exists to catch.
# Same precedent as MutGateOpeningNoGates/MutTileGridNoGates below (same mutation, second catching test, in a
# different source file — "a distinct class per (mutation, catching test) pair is the only shape that works").
# Never printed as its own case in Mutants.cs: the ONE printed "MutSpurWideDilation" case calls both this
# class's SelfTestRoadsAndGates and the class above's SelfTestSpurWidth, so a failure in either kills it.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutSpurWideDilationRoadsAndGates' `
  '                    if (isBuilding || HasBuildingWithin(g, a, b, OpenStreetNeighbourhood)) wide[a, b] = true;' `
  '                    if (true) wide[a, b] = true;   // MUTANT: every seed cell takes the wide radius' `
  'MutSpurWideDilationRoadsAndGates.cs'

New-SettlementRebind 'SelfTestRoadsAndGates' 'MutSpurWideDilationRoadsAndGates' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutSpurWideDilationRoadsAndGates.SettlementTileGrid.', 'WorldGen.Generation.MutSpurWideDilationRoadsAndGates.TileType')

# ---- TILE GRID ROADS/GATES MUTANTS (Task 3): two rules pinned by SettlementTileGrid.Build's road/gate pass. --
# Same bundling as the three wall-ring mutants above (TileType + SettlementTileGrid share the file), so the
# rebind needs the same two patterns.

# MutTileGridNoGates: the gate-reclassify write neutered — a gate room's nearest Wall ring cell never becomes
# TileType.Gate. Caught by SelfTestRoadsAndGates' west-wall-cell-is-Gate assertion (OVERRIDE 2).
New-SettlementMutant 'SettlementTileGrid.cs' 'MutTileGridNoGates' `
  'g.Cells[wallI - g.OriginI, wallJ - g.OriginJ] = TileType.Gate;' `
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

# MutGateSpurNone: the gate-spur pass is skipped, so the one-cell courtyard stays between every gate and the
# road network — exactly the state DM finding .3 reported. SelfTestGateSpur's "path is empty on a built grid"
# assertion must fail.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGateSpurNone' `
  '                MarkGateSpurs(g);   // AFTER MarkGates: there is nothing to spur from until the gates exist' `
  '                // MUTANT: gate spurs never painted' `
  'MutGateSpurNone.cs'

New-SettlementRebind 'SelfTestGateSpur' 'MutGateSpurNone' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutGateSpurNone.SettlementTileGrid.', 'WorldGen.Generation.MutGateSpurNone.TileType')

# MutGateHitNoGateRoom: MarkGates draws the Gate tile but never records which room owns it, so the renderer's
# HitRoomId fallback has nothing to look up and a click on the visible gate selects nothing again.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGateHitNoGateRoom' `
  '                g.GateRoomAt[DepthKey(wallI, wallJ)] = r.Id;' `
  '                // MUTANT: the gate cell is never attributed to its room' `
  'MutGateHitNoGateRoom.cs'

New-SettlementRebind 'SelfTestGateHandles' 'MutGateHitNoGateRoom' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutGateHitNoGateRoom.SettlementTileGrid.', 'WorldGen.Generation.MutGateHitNoGateRoom.TileType')

# MutNearestWallCellTie: NearestWallCell's strict `<` flipped to `<=`, so a LATER equidistant candidate wins a
# tie instead of the first one encountered — the exact divergence Finding 4 of the final-arc review named as
# unguarded (MarkGates and SettlementVolumeRenderer.TryNearestWallCell now share this ONE implementation, but
# nothing pinned the tie rule ITSELF). Only reachable when a real corpus town has a genuine equidistant tie for
# SOME gate's nearest wall/gate cell; whether SelfTestGateHandles' 60-gate sweep contains one is reported by
# the harness run, not assumed here.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutNearestWallCellTie' `
  'if (d2 < bestD2) { bestD2 = d2; wallI = a + g.OriginI; wallJ = b + g.OriginJ; any = true; }' `
  'if (d2 <= bestD2) { bestD2 = d2; wallI = a + g.OriginI; wallJ = b + g.OriginJ; any = true; }   // MUTANT: a later tie wins' `
  'MutNearestWallCellTie.cs'

New-SettlementRebind 'SelfTestGateHandles' 'MutNearestWallCellTie' `
  @('SettlementTileGrid\.', '\bTileType\b') `
  @('WorldGen.Generation.MutNearestWallCellTie.SettlementTileGrid.', 'WorldGen.Generation.MutNearestWallCellTie.TileType')

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
# New-SettlementMutant / rebind-only-"SettlementFootprint." shape (SettlementFence/InteriorOps/FloorFootprint
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

# RETIRED (arc C.2, task C): MutBlocksNoSubdivision. It forced SettlementBlocks.Subdivide to accept every block
# uncut, and Subdivide no longer exists — streets are laid where a house would otherwise have no frontage, not
# by recursive halving — so there is no rule left for it to remove. The assertion it used to catch (every
# recovered block at or below BlockTargetCells) went with it: a block's SIZE is not bounded any more, only its
# DEPTH, which SelfTestFrontage pins as geometry.

# MutBlocksOverlapAllowed: the fill's disjointness term is dropped from Available, so a cell already claimed by
# one building is handed to the next as well — every block cell seeds its own building and the grown rects
# overlap. Caught by SelfTestBlocks assertion 1, which names the shared cell and both buildings.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksOverlapAllowed' `
  '=> blockSet.Contains(c) && !claimed.Contains(c);' `
  '=> blockSet.Contains(c);   /* MUTANT: the fill skips its disjointness check */' `
  'MutBlocksOverlapAllowed.cs'

foreach ($mc in @('MutBlocksNoRingStreet', 'MutBlocksOverlapAllowed')) {
  New-SettlementRebind 'SelfTestBlocks' $mc `
    @('SettlementBlocks\.') `
    @("WorldGen.Generation.$mc.SettlementBlocks.")
}

# ---- SHAPE-PALETTE MUTANTS (arc A, task 3): two rules pinned by SelfTestBlockForms. -------------------------
# Same file, same rebind shape as the two above — SelfTestBlockForms never names BlockLayout either (every
# layout it touches is captured through `var`), so a rebind of just `SettlementBlocks\.` is sound and complete.

# MutFillNoTruncate: placement takes the WHOLE template regardless of availability, so footprints overlap each
# other and spill outside their block. SelfTestBlockForms' disjointness and in-block claims must fail.
New-SettlementMutant 'SettlementBlocks.cs' 'MutFillNoTruncate' `
  '                    if (!Available(c, blockSet, claimed)) break;   // TRUNCATE: every prefix is a legal house' `
  '                    // MUTANT: availability never tested, the whole template is taken' `
  'MutFillNoTruncate.cs'

# MutFillSingleOnly: the palette always rolls the single-cell template, i.e. exactly the pre-arc behaviour the
# DM reported as .11. SelfTestBlockForms' 25% multi-cell and 5% non-rect shares must fail.
New-SettlementMutant 'SettlementBlocks.cs' 'MutFillSingleOnly' `
  '            int roll = rng.Next(PaletteWeightTotal);' `
  '            int roll = -1; return Palette[0];   // MUTANT: always the single-cell template' `
  'MutFillSingleOnly.cs'

foreach ($mc in @('MutFillNoTruncate', 'MutFillSingleOnly')) {
  New-SettlementRebind 'SelfTestBlockForms' $mc `
    @('SettlementBlocks\.') `
    @("WorldGen.Generation.$mc.SettlementBlocks.")
}

# MutFillNoMinimumRepair: the minimum-count repair returns without splitting anything, so a town whose palette
# roll came out heavy stays under the guarantee the inspector shows. SelfTestSizeCalibration's per-town
# guarantee assertion must fail.
New-SettlementMutant 'SettlementBlocks.cs' 'MutFillNoMinimumRepair' `
  '            if (buildings == null || guarantee <= 0) return 0;' `
  '            if (true) return 0;   // MUTANT: nothing is ever split' `
  'MutFillNoMinimumRepair.cs'

New-SettlementRebind 'SelfTestSizeCalibration' 'MutFillNoMinimumRepair' `
  @('SettlementBlocks\.') `
  @('WorldGen.Generation.MutFillNoMinimumRepair.SettlementBlocks.')

# ---- FRONTAGE-STREET MUTANTS (arc C.2, task C): four rules pinned by SelfTestFrontage. --------------------
# Same file, same rebind shape as the three above — SelfTestFrontage likewise never names BlockLayout (every
# layout it touches is captured through `var`), and SettlementSizing / SettlementGenerator / WallContour /
# SettlementFootprint all live in unmutated files and resolve OUTWARD once SettlementBlocks.cs is renamespaced.

# MutBlocksNoArterials: Arterials returns before it lays a single cell, so the only streets a town gets are its
# ring and whatever the frontage fill hangs off it. Caught by SelfTestFrontage assertion 4 — the two properties
# only the arterial pass delivers: the core cell just inside a gate is a street, and the town's centre cell is
# a street.
#
# NOTE, and it is a correction to this task's brief rather than an oversight: the brief predicted the
# ONE-NETWORK assertion would fire ("a gate is left with no street reaching it"). It cannot, and that is by
# design — every gate is a RING cell, the ring is one 4-connected lap, and every frontage strip must touch the
# network at an end, so the streets are one piece whether or not an arterial was ever laid. The arterials are a
# LEGIBILITY rule (the road a traveller arrives on) and their invariants had to be stated as such.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksNoArterials' `
  'var laid = new List<(int i, int j)>();' `
  'var laid = new List<(int i, int j)>(); return laid;   // MUTANT: no arterial is ever laid' `
  'MutBlocksNoArterials.cs'

# MutBlocksNoFrontageFill: FrontageFill returns before it paves a single strip, so nothing bridges the gap
# between the ring and the middle of a block. Caught by SelfTestFrontage assertion 1, which names the exact
# stranded cell, and behind it assertion 5 (a block that surrounds one of its own cells on all four sides).
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksNoFrontageFill' `
  'var paved = new List<(int i, int j)>();' `
  'var paved = new List<(int i, int j)>(); return paved;   // MUTANT: no frontage strip is ever paved' `
  'MutBlocksNoFrontageFill.cs'

# MutBlocksFillIgnoresNetwork: the frontage fill's candidate strips no longer have to touch the street network
# at either end, so the greedy is free to pave a strip floating in the middle of a block — which it does,
# because a deep strip serves more unfronted cells per cell consumed than one hanging off the ring. Caught by
# SelfTestFrontage assertion 2: the flood-fill from the first gate cannot reach the island.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksFillIgnoresNetwork' `
  '=> nearTouch || (isFullRun && streets.Contains(after));' `
  '=> true;   // MUTANT: a candidate strip no longer has to touch the existing network' `
  'MutBlocksFillIgnoresNetwork.cs'

# MutBlocksGatesAdjacent: the MinGateSeparationCells term is dropped from PlaceGateCells' TooCloseToAGate, so
# two gates may land in what reads as one wide doorway. Caught by SelfTestFrontage assertion 7 — the 8-cell
# ring of a 3x3 town, where every pair of ring cells is within Chebyshev 2 and the real rule can therefore
# place exactly ONE gate out of the four asked for. A real town's ring is ~30 cells and its gates come out 9+
# apart, so the sweep alone could never reach the regime this rule governs.
New-SettlementMutant 'SettlementBlocks.cs' 'MutBlocksGatesAdjacent' `
  'if (Chebyshev(c, g) < MinGateSeparationCells) return true;' `
  ';   // MUTANT: the MinGateSeparationCells term is dropped' `
  'MutBlocksGatesAdjacent.cs'

foreach ($mc in @('MutBlocksNoArterials', 'MutBlocksNoFrontageFill', 'MutBlocksFillIgnoresNetwork',
                  'MutBlocksGatesAdjacent')) {
  New-SettlementRebind 'SelfTestFrontage' $mc `
    @('SettlementBlocks\.') `
    @("WorldGen.Generation.$mc.SettlementBlocks.")
}

# ---- STREET ACCESS MUTANTS (settlement-street-access, task 1): two rules pinned by SettlementStreetOps. ----
# SettlementStreetOps.cs defines ONE class and no data types (InteriorFloor/Room live in the unmutated
# DungeonData.cs, SettlementFootprint/SettlementTileGrid in their own unmutated files — all resolve OUTWARD),
# so the single-class New-SettlementMutant / rebind-only-"SettlementStreetOps." shape (SettlementFence/
# InteriorOps/FloorFootprint above) is sound here.

# MutAccessNoCarve: MissingAccess returns nothing, so no edit is ever repaired — the exact state DM finding
# .4 reported. SelfTestStreetAccess' moved-building and street-less cases must fail.
New-SettlementMutant 'SettlementStreetOps.cs' 'MutAccessNoCarve' `
  '            var added = new List<(int i, int j)>();' `
  '            var added = new List<(int i, int j)>(); if (floor != null) return added;   // MUTANT: never carves' `
  'MutAccessNoCarve.cs'

# MutAccessIgnoresConnectivity: only half 1 of the invariant is enforced, so a house can be served by an
# island lane. SelfTestStreetAccess' one-component assertion must fail.
New-SettlementMutant 'SettlementStreetOps.cs' 'MutAccessIgnoresConnectivity' `
  '                var orphan = SmallestOrphanComponent(streets);' `
  '                List<(int i, int j)> orphan = null;   // MUTANT: orphan components never joined' `
  'MutAccessIgnoresConnectivity.cs'

foreach ($mc in @('MutAccessNoCarve', 'MutAccessIgnoresConnectivity')) {
  New-SettlementRebind 'SelfTestStreetAccess' $mc `
    @('SettlementStreetOps\.') `
    @("WorldGen.Generation.$mc.SettlementStreetOps.")
}

# ---- SIZE-CLASS + v11 LATTICE MIGRATION MUTANTS (arc C.2, task B): three rules pinned by SettlementSizing
# and SettlementMigration. ------------------------------------------------------------------------------------
# SettlementSizing.cs bundles TWO types in one namespace block — enum SettlementSize AND class
# SettlementSizing — the same bundling shape as SettlementTileGrid.cs/TileType and SettlementGenerator.cs/
# SettlementConfig above, so its rebind needs a SECOND bare pattern (`\bSettlementSize\b`) alongside
# `SettlementSizing\.`. The two patterns cannot collide in either order: "SettlementSize" inside
# "SettlementSizing" is followed by 'i', a word character, so \b never matches there. SettlementFootprint
# lives in an unmutated file and resolves OUTWARD once SettlementSizing.cs is re-namespaced (WallRadiusNorm
# reads SettlementFootprint.Pitch), and SelfTestSizing touches no other type — no cross-file stub needed.

# MutSizingLargeOverflowsField: the Large size's wall radius blown up past what the 0..1 field can hold, so a
# Large town's wall would need to span 0.5 +/- 0.6 = -0.1..1.1 — off both ends of the field and far outside
# DungeonViewController's 0.04..0.96 drag clamp. Caught by SelfTestSizing's FIELD BOUND assertion, and ONLY
# that one: the table stays monotone (20 > 6.4 > 4.7), the targets/gates/guarantees are untouched, and the
# legacy bucketing does not read a radius at all — so this is a single-rule mutant on the one constraint that
# forced the pitch change in the first place.
New-SettlementMutant 'SettlementSizing.cs' 'MutSizingLargeOverflowsField' `
  'case SettlementSize.Large: return 10.0f;' `
  'case SettlementSize.Large: return 20f;   // MUTANT: a Large town no longer fits the 0..1 field' `
  'MutSizingLargeOverflowsField.cs'

New-SettlementRebind 'SelfTestSizing' 'MutSizingLargeOverflowsField' `
  @('SettlementSizing\.', '\bSettlementSize\b') `
  @('WorldGen.Generation.MutSizingLargeOverflowsField.SettlementSizing.', 'WorldGen.Generation.MutSizingLargeOverflowsField.SettlementSize')

New-SettlementMutant 'SettlementSizing.cs' 'MutSizingGuaranteeTooHigh' `
  'public static int GuaranteedMinBuildings(SettlementSize size)' `
  'public static int GuaranteedMinBuildings(SettlementSize size) { return TargetBuildings(size); }   // MUTANT: guarantee == target
        private static int _GuaranteedMinBuildingsUnreachable(SettlementSize size)' `
  'MutSizingGuaranteeTooHigh.cs'

# MutSizingGuaranteeTooHigh (Task D): GuaranteedMinBuildings' switch is bypassed entirely — the method returns
# TargetBuildings(size) instead, i.e. a guarantee equal to the target, the exact lie SettlementSizing's own doc
# says a guarantee must never tell. The original switch survives as an unreachable, never-called private method
# (`_GuaranteedMinBuildingsUnreachable`) purely so the mutant copy still compiles with nothing else disturbed.
# Caught by SelfTestSizeCalibration's per-seed floor: at every one of the three shipped sizes the 200-seed sweep
# has seeds that land BELOW target (that is exactly why a guarantee below target was needed at all), so forcing
# the guarantee up to the target must fail the sweep on those seeds.
#
# THE CROSS-FILE TYPE TRAP, why this rebind is ONE surgical line and not the usual two-pattern
# "SettlementSizing\./\bSettlementSize\b" dance: SelfTestSizeCalibration calls SettlementBlocks.Generate(wall,
# seed, size) — SettlementBlocks.cs is NOT mutated here, so its Generate keeps expecting the REAL
# WorldGen.Generation.SettlementSize. Rebinding the bare enum too would retype the loop's `size` to the MUTANT
# namespace's OWN SettlementSize and break that call (a straight compile error, not a covariance one — enums
# have no implicit conversion between two same-named types in different namespaces). So only the ONE guarantee
# call is rebound, with an explicit int round-trip cast bridging the two enums at that single call site; `size`
# itself, and every other SettlementSizing call in the method (WallRadiusNorm, TargetBuildings), stay REAL.
New-SettlementRebind 'SelfTestSizeCalibration' 'MutSizingGuaranteeTooHigh' `
  @('int guarantee = SettlementSizing\.GuaranteedMinBuildings\(size\);') `
  @('int guarantee = WorldGen.Generation.MutSizingGuaranteeTooHigh.SettlementSizing.GuaranteedMinBuildings((WorldGen.Generation.MutSizingGuaranteeTooHigh.SettlementSize)(int)size);')

# MutSizingCapIgnoresActual (Task 6, settlement brushes): ActiveBuildingsCap reverts to the OLD ceiling
# behaviour it replaced — it returns GuaranteedMinBuildings(size) regardless of buildingCount, ignoring how
# many buildings the town actually has. Caught by SelfTestActiveBuildingsCap's `floor + 25` half: with more
# buildings than the table promises, the real rule must return the actual count (a floor, never a ceiling)
# while the mutant still clamps down to the table's number. The `buildingCount == 0` half does NOT catch this
# mutant — Max(floor, 0) == floor == GuaranteedMinBuildings(size) either way, so a town with fewer buildings
# than the table promises looks identical under both rules; only the above-the-table case tells them apart.
# Same two-pattern shape as MutSizingLargeOverflowsField's rebind of SelfTestSizing just above:
# SelfTestActiveBuildingsCap's only cross-references are its own `new[] { SettlementSize.Small, ... }` array
# and SettlementSizing.* calls, so rebinding both the bare enum and the static class within just this method
# is safe — nothing else in the method needs the REAL SettlementSize.
New-SettlementMutant 'SettlementSizing.cs' 'MutSizingCapIgnoresActual' `
  'public static int ActiveBuildingsCap(SettlementSize size, int buildingCount)
            => System.Math.Max(GuaranteedMinBuildings(size), buildingCount);' `
  'public static int ActiveBuildingsCap(SettlementSize size, int buildingCount)
            => GuaranteedMinBuildings(size);   // MUTANT: back to a CEILING, ignoring how many buildings the town actually has' `
  'MutSizingCapIgnoresActual.cs'

New-SettlementRebind 'SelfTestActiveBuildingsCap' 'MutSizingCapIgnoresActual' `
  @('SettlementSizing\.', '\bSettlementSize\b') `
  @('WorldGen.Generation.MutSizingCapIgnoresActual.SettlementSizing.', 'WorldGen.Generation.MutSizingCapIgnoresActual.SettlementSize')

# SettlementMigration.cs defines ONE class and no data types (InteriorData/InteriorFloor/Room live in the
# unmutated DungeonData.cs, SettlementFootprint in its own unmutated file — both resolve OUTWARD), so the
# single-class New-SettlementMutant / rebind-only-"SettlementMigration." shape is sound here, exactly like
# SettlementFootprint/SettlementFence/InteriorOps above. No IReadOnlyList<T> struct-covariance trap: every
# cell list it exchanges is List<(int,int)>, a value tuple of ints, identical in both namespaces.

# MutMigrationNoRecentre: RecentreFloor returns before it folds a single cell, so a legacy town is left
# wherever the finer lattice put it — 3/7 of the way toward the origin, up in the corner of the field.
# The pattern is unique in the file (RederivePositions guards with `continue`, and its own null test names
# `dungeon`, not `floor`), which matters because PowerShell -replace is global. Caught by
# SelfTestSizeMigration's bbox-centre assertion (the town's cell bbox must centre on CellOf(0.5) = 16) and
# by the hand-derived (16..17,15..17) bbox behind it.
New-SettlementMutant 'SettlementMigration.cs' 'MutMigrationNoRecentre' `
  'if (floor == null) return;' `
  'return;   // MUTANT: RecentreFloor never runs — a legacy town stays in the corner it landed in' `
  'MutMigrationNoRecentre.cs'

New-SettlementRebind 'SelfTestSizeMigration' 'MutMigrationNoRecentre' `
  @('SettlementMigration\.') `
  @('WorldGen.Generation.MutMigrationNoRecentre.SettlementMigration.')

# MutMigrationTruncatingHalf: RecentreFloor's FloorHalf reverted to C#'s truncate-toward-zero `/` — the exact
# implementation the D5 deviation exists to replace. The two agree for every non-negative sum, so this is
# silent until a town's cell bbox sums ODD AND NEGATIVE on an axis, where truncation lands the town one cell
# off centre AND leaves a non-zero delta behind, so it moves AGAIN on the next load. Caught by
# SelfTestSizeMigration's section-7 fixture (cells at i = -2..-1, sum -3), which asserts both halves: the
# hand-derived 16..17 landing and the second-RecentreFloor-changes-nothing idempotence.
New-SettlementMutant 'SettlementMigration.cs' 'MutMigrationTruncatingHalf' `
  'static int FloorHalf(int v) => v >= 0 ? v / 2 : -(((-v) + 1) / 2);' `
  'static int FloorHalf(int v) => v / 2;   // MUTANT: C# truncating division — wrong for an odd NEGATIVE sum' `
  'MutMigrationTruncatingHalf.cs'

New-SettlementRebind 'SelfTestSizeMigration' 'MutMigrationTruncatingHalf' `
  @('SettlementMigration\.') `
  @('WorldGen.Generation.MutMigrationTruncatingHalf.SettlementMigration.')

# MutMigrationCurrentPitch: EnsureFootprints ignores its legacyLattice argument and always derives a
# cell-less room's cell with CellOf (the CURRENT 0.03 lattice), even for a pre-v11 file — the exact mistake
# that would scatter a legacy town's houses 2.33x apart and open a gap between every pair that stood flush.
# Caught by SelfTestSizeMigration's gate-cell assertion on fixture 1 (loaded with legacyLattice: true): the
# gate's point 0.60/0.66 is legacy cell (8,9) and current cell (20,22). Mutates SettlementFootprint.cs, so
# this is a SECOND rebind of SelfTestSizeMigration against a different file (the same shape as
# SelfTestActiveBuildings' three rebinds above).
New-SettlementMutant 'SettlementFootprint.cs' 'MutMigrationCurrentPitch' `
  'var cell = legacyLattice ? (LegacyCellOf(r.X), LegacyCellOf(r.Y)) : (CellOf(r.X), CellOf(r.Y));' `
  'var cell = (CellOf(r.X), CellOf(r.Y));   // MUTANT: a LEGACY point always read on the CURRENT lattice' `
  'MutMigrationCurrentPitch.cs'

New-SettlementRebind 'SelfTestSizeMigration' 'MutMigrationCurrentPitch' `
  @('SettlementFootprint\.') `
  @('WorldGen.Generation.MutMigrationCurrentPitch.SettlementFootprint.')

# MutMigrationAlwaysLegacyPitch: the MIRROR of the mutant above, and the one that pins the defect review
# found — EnsureFootprints ignores legacyLattice the OTHER way and always derives on the pre-v11 0.07 pitch.
# A v11 file's cell-less room then gets a legacy index written back to disk that NOTHING repairs
# (RecentreFloor/RederivePositions are version-gated off at v11) and that the render masks
# (SettlementTileGrid.FootprintOf rule (b) re-derives a disagreeing single-cell footprint from the point), so
# it is silently wrong data at rest. Caught by SelfTestSizeMigration's section-8 fixture, which calls
# EnsureFootprints(legacyLattice: false) and pins the stamped cell at CellOf's (20,22) rather than
# LegacyCellOf's (8,9).
New-SettlementMutant 'SettlementFootprint.cs' 'MutMigrationAlwaysLegacyPitch' `
  'var cell = legacyLattice ? (LegacyCellOf(r.X), LegacyCellOf(r.Y)) : (CellOf(r.X), CellOf(r.Y));' `
  'var cell = (LegacyCellOf(r.X), LegacyCellOf(r.Y));   // MUTANT: a v11 point always read on the LEGACY lattice' `
  'MutMigrationAlwaysLegacyPitch.cs'

New-SettlementRebind 'SelfTestSizeMigration' 'MutMigrationAlwaysLegacyPitch' `
  @('SettlementFootprint\.') `
  @('WorldGen.Generation.MutMigrationAlwaysLegacyPitch.SettlementFootprint.')

# ---- POI MIGRATION MUTANT: the removed Village type must still normalize on load. ---------------------------
# PoiMigration.cs defines ONE class and no data types (PoiData/PoiType live in the unmutated PoiData.cs and
# resolve OUTWARD once this file is re-namespaced), so the single-class New-SettlementMutant / rebind-only-
# "PoiMigration." shape (SettlementFootprint/SettlementFence/InteriorOps above) is sound here.

# MutPoiMigrationNoop: NormalizeLegacyTypes returns immediately, before ever inspecting a POI's type — a
# legacy-Village POI is never rewritten to City. Caught by SelfTestPoiLegacyTypes' legacy-Village assertion.
New-SettlementMutant 'PoiMigration.cs' 'MutPoiMigrationNoop' `
  'if (pois == null) return;' `
  'return;   // MUTANT: NormalizeLegacyTypes never runs' `
  'MutPoiMigrationNoop.cs'

New-SettlementRebind 'SelfTestPoiLegacyTypes' 'MutPoiMigrationNoop' `
  @('PoiMigration\.') `
  @('WorldGen.Generation.MutPoiMigrationNoop.PoiMigration.')

# ---- THE FENCE'S FOOTPRINT PROJECTION (task 4). ------------------------------------------------------------
# DungeonLayout.cs bundles FOUR types in one namespace block — class DungeonLayout, struct LayoutPoint, class
# RenderSegment, class RenderGraph — but SelfTestFence names ONLY `DungeonLayout.` (LinkNodeFor, TilesPerAxis);
# the other three never appear in it, and adding a pattern for them would make New-SettlementRebind throw for
# want of a match. Nothing else is at risk from the bundling: the mutant copy's own BuildRenderGraph returns its
# NESTED RenderGraph, and no unmutated file (nor the rebound test) ever receives one. Everything DungeonLayout.cs
# calls — RoomLinkGeometry, DungeonProjection, SettlementFence, SettlementTileGrid,
# SettlementFootprint, Room/InteriorFloor/Link — lives in an unmutated file and resolves OUTWARD once this file
# is re-namespaced, so no cross-file stub is needed. LinkNode is RoomLinkGeometry's and stays REAL, so the
# LinkNodes the rebound fixture hands the REAL SettlementFence carry no covariance trap.

# MutFenceIgnoresFootprint: LinkNodeFor's settlement-building branch projects only the footprint's
# REPRESENTATIVE cell instead of its whole cell bounding box — i.e. the fence goes back to wrapping a building
# as a point rather than as its footprint. Caught by SelfTestFence fixture G, whose 4-cell bar puts its far cell
# 11.52 tiles from the representative, far past the 1.92 + FenceMarginTiles = 3.92 tiles a one-cell rect reaches:
# that cell falls OUTSIDE the derived fence and the enclosure assertion names it. Fixture G is four cells long
# for exactly this reason — at TWO cells a representative-only rect still covers the neighbour's centre (3.92 >
# 3.84) and this mutant would survive.
New-SettlementMutant 'DungeonLayout.cs' 'MutFenceIgnoresFootprint' `
  'var (minI, minJ, maxI, maxJ) = SettlementFootprint.Bounds(fp);' `
  'var repCell = SettlementFootprint.Representative(fp); var (minI, minJ, maxI, maxJ) = (repCell.i, repCell.j, repCell.i, repCell.j);   // MUTANT: only the representative cell is projected' `
  'MutFenceIgnoresFootprint.cs'

New-SettlementRebind 'SelfTestFence' 'MutFenceIgnoresFootprint' `
  @('DungeonLayout\.') `
  @('WorldGen.Generation.MutFenceIgnoresFootprint.DungeonLayout.')

# MutFenceNoStreets (task 5): DeriveTownFence stops folding the floor's STORED street cells into the rasterized
# union — Decode is handed null, which its own contract turns into an empty list, so the fence is traced around
# buildings + gates alone. That is EXACTLY the fence the task's measurement rejected: on the SelfTestWallBounds
# fixture (seed 1 / Medium / walled) 21 of 78 stored street cells fall outside it, so section 3a's "every stored
# street cell is inside the derived fence" assertion fires and names the first offender. Section 3b — the
# control, which derives that buildings+gates-only fence itself and demands it leave at least one cell outside —
# keeps passing under the mutant (it never calls DeriveTownFence), which is what makes 3a's failure the signal.
# The locator matches exactly once in the file.
New-SettlementMutant 'DungeonLayout.cs' 'MutFenceNoStreets' `
  'SettlementFootprint.Decode(lvl.SettlementParams.StreetCells)' `
  'SettlementFootprint.Decode(null)   /* MUTANT: the stored streets are not folded into the fence */' `
  'MutFenceNoStreets.cs'

# SelfTestWallBounds names DungeonLayout. (DeriveTownFence, TilesPerAxis, LinkNodeFor) and nothing else from
# that file's other three types — same rebind shape as MutFenceIgnoresFootprint above. Everything else it
# touches (SettlementGenerator, SettlementFence, SettlementFootprint, DungeonProjection, WallContour) stays
# REAL, so the LinkNodes its own control fence is built from carry no covariance trap.
New-SettlementRebind 'SelfTestWallBounds' 'MutFenceNoStreets' `
  @('DungeonLayout\.') `
  @('WorldGen.Generation.MutFenceNoStreets.DungeonLayout.')

# ---- THE SETTLEMENT VALIDATION RULES (task 4): four rules pinned by DungeonValidator.SettlementIssues. ------
# DungeonValidator.cs bundles THREE types in one namespace block — enum IssueSeverity, class DungeonIssue AND
# class DungeonValidator — the same bundling shape as SettlementGenerator.cs/SettlementConfig above, so the
# rebind needs all three patterns. SelfTestSettlementValidation names every one of them (its two local helpers
# take List<DungeonIssue> and IssueSeverity — deliberately LOCAL functions, so the rebind's method-scoped
# rewrite covers their signatures too; a file-level helper would keep the REAL DungeonIssue and turn every
# mutant into a compile error instead of a failing assertion). The three patterns cannot collide in either
# order: "DungeonIssue" and "DungeonValidator" share no prefix, and \b anchors both.
#
# Everything the mutant copy calls — SettlementFootprint, SettlementTileGrid, InteriorData/InteriorFloor/Room,
# BuildingGenerator.StairTypeId, DungeonGraphGenerator.DefaultMinBossDistance — lives in an unmutated file and
# resolves OUTWARD, and Validate takes an InteriorData (unmutated) so the fixtures the rebound test builds pass
# through unchanged. No cross-file stub needed.

# MutValidatorNoOverlapRule: the disjointness decision is forced false, so two buildings claiming one cell are
# never reported (the claim map is still filled, so nothing else changes). This is the rule Task 7's report
# folded into this task: SettlementVolumeRenderer.AreCellsFree is the only thing preventing a permanent overlap
# at the EDIT and it lives in Rendering, which the harness cannot compile — this is the same property asserted
# from the data side, where it can be. Caught by SelfTestSettlementValidation part 2(c): rooms 10 and 11 share
# exactly cell (7,5) and the Error naming both ids and that cell must be raised.
New-SettlementMutant 'DungeonValidator.cs' 'MutValidatorNoOverlapRule' `
  'bool contended = claimed.ContainsKey(c);' `
  'bool contended = false;   // MUTANT: the disjointness rule is dropped' `
  'MutValidatorNoOverlapRule.cs'

# MutValidatorNoStreetRule: the street-coincidence decision is forced false, so a building standing in its own
# street is never reported. Caught by SelfTestSettlementValidation part 2(d), which pins the Warning naming
# cell (6,6). This is the one of the four rules a DM reaches by ordinary dragging — a stored street cell is
# owned by no room, so the drag verdict does not refuse it — which is why it gets its own mutant.
New-SettlementMutant 'DungeonValidator.cs' 'MutValidatorNoStreetRule' `
  'if (streets.Contains(c))' `
  'if (false)   // MUTANT: the street-coincidence rule is dropped' `
  'MutValidatorNoStreetRule.cs'

# MutValidatorFootprintScopeAllRooms: the TypeId == 1 scope is widened to every room, so the rules run on GATES
# too. That is the exact mistake the buildings-only scope exists to prevent: a gate's cell IS a street cell by
# construction (SettlementBlocks.PlaceGateCells picks it off the ring), so the street rule then fires on every
# gate of every town. Caught in TWO places at once — part 1 (a freshly generated town must report nothing) and
# part 3's gate fixtures (a gate on its own ring-street cell, and gates with empty/disconnected footprints).
# The pattern stops before that line's trailing comment on purpose: sync.ps1 is read by PowerShell 5.1 as ANSI,
# so a non-ASCII character in a literal here (the source comment's em dash) never matches the UTF-8 source.
# Keep every mutant pattern and replacement in this file pure ASCII. `r.TypeId != 1` is unique in the file.
New-SettlementMutant 'DungeonValidator.cs' 'MutValidatorFootprintScopeAllRooms' `
  'if (r == null || r.TypeId != 1) continue;' `
  'if (r == null) continue;   /* MUTANT: buildings-only scope dropped, gates are judged too */' `
  'MutValidatorFootprintScopeAllRooms.cs'

# MutValidatorEmptyViaFootprintOf: rules 1-2 read SettlementTileGrid.FootprintOf instead of the STORED array.
# This is the ANTI-VACUITY mutant of the pair: FootprintOf's rule (a) substitutes the room's point cell for a
# missing footprint, so read through it "the footprint is non-empty" can never fail however broken the data is
# — the rule would still be there, still be evaluated, and be structurally incapable of firing. Caught by
# SelfTestSettlementValidation part 2(a), whose room 1 carries no Cells at all.
New-SettlementMutant 'DungeonValidator.cs' 'MutValidatorEmptyViaFootprintOf' `
  'var stored = SettlementFootprint.Decode(r.Cells);' `
  'var stored = SettlementTileGrid.FootprintOf(r);   // MUTANT: the stored shape read through the render fallback' `
  'MutValidatorEmptyViaFootprintOf.cs'

foreach ($mc in @('MutValidatorNoOverlapRule', 'MutValidatorNoStreetRule',
                  'MutValidatorFootprintScopeAllRooms', 'MutValidatorEmptyViaFootprintOf')) {
  New-SettlementRebind 'SelfTestSettlementValidation' $mc `
    @('DungeonValidator\.', '\bDungeonIssue\b', '\bIssueSeverity\b') `
    @("WorldGen.Generation.$mc.DungeonValidator.", "WorldGen.Generation.$mc.DungeonIssue",
      "WorldGen.Generation.$mc.IssueSeverity")
}

# ---- THE GATE-OPENING PROPERTY ON GENERATED TOWNS (task 4). ------------------------------------------------
# SelfTestGateOpening re-verifies, on GENERATED towns, the property the street rework MOVED: the wall the DM
# sees is SettlementTileGrid's ring and the opening is a Gate TILE, not the vector fence's bulge around a gate
# point. Two mutants because it makes two independent claims (one opening per gate, and every stored street
# enclosed), and each must be shown to fire.
#
# WHY THESE ARE SEPARATE CLASSES rather than a second rebind of MutTileGridNoGates / MutGridStreetsNotSeeded
# above, which is what the SelfTestActiveBuildings-x3 precedent might suggest: those three rebind DIFFERENT
# mutant classes against ONE test method. This is the opposite shape — the SAME mutation caught by a SECOND test method, in a
# DIFFERENT source file. New-SettlementRebind names its output gen/SelfTests_<mutantClass>.cs and the class
# inside it ${mutantClass}SelfTests, so reusing the class name would overwrite the SettlementTileGridSelfTests-
# derived copy with a SettlementSelfTests-derived one, and the existing catch
# (MutTileGridNoGatesSelfTests().SelfTestRoadsAndGates()) would stop compiling. A distinct class per (mutation,
# catching test) pair is the only shape that works — the same reason MutMigrationCurrentPitch and
# MutMigrationAlwaysLegacyPitch are two classes over one file.
#
# Same two-pattern rebind as every other SettlementTileGrid mutant (the file bundles TileType), and both
# patterns are required: SelfTestGateOpening reads grid.Cells[a, b] against TileType.Gate/Road/Building, so
# moving one without the other is a compile error rather than a red assertion. SettlementGenerator and
# SettlementFootprint stay REAL (unmutated files, resolving outward), so the town under test is the real one
# and only the grid derived from it is mutated.

# MutGateOpeningNoGates: the same gate-reclassify neutering as MutTileGridNoGates, caught here instead by the
# GENERATED-town claims — gateTiles falls to 0 in every town, so the one-opening-per-gate count fires, and with
# no Gate tile anywhere the nearest-Gate-tile search finds none and the Chebyshev bound fires too.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGateOpeningNoGates' `
  'g.Cells[wallI - g.OriginI, wallJ - g.OriginJ] = TileType.Gate;' `
  ';   // MUTANT: gate reclassify never applied' `
  'MutGateOpeningNoGates.cs'

# MutGateOpeningStreetsNotSeeded: the same ring-seed neutering as MutGridStreetsNotSeeded. The ring is dilated
# from the buildings alone, so the outermost stored street cells fall outside it and MarkRoads' Inside test
# rejects them — they read None instead of Road, and the street-enclosure claim fires naming the exact cell.
New-SettlementMutant 'SettlementTileGrid.cs' 'MutGateOpeningStreetsNotSeeded' `
  'bool[,] inside = hasWall ? BuildWallRing(g, streetMask) : null;' `
  'bool[,] inside = hasWall ? BuildWallRing(g, null) : null;   // MUTANT: streets never folded into the ring seed' `
  'MutGateOpeningStreetsNotSeeded.cs'

foreach ($mc in @('MutGateOpeningNoGates', 'MutGateOpeningStreetsNotSeeded')) {
  New-SettlementRebind 'SelfTestGateOpening' $mc `
    @('SettlementTileGrid\.', '\bTileType\b') `
    @("WorldGen.Generation.$mc.SettlementTileGrid.", "WorldGen.Generation.$mc.TileType")
}

# ---- BRUSH MUTANTS (settlement-brushes, task 1): two rules pinned by SettlementBrushOps. -------------------
# SettlementBrushOps.cs bundles TWO types in one namespace block — class SettlementBrushOps AND class
# SettlementVolumeRendererPlacement — but SelfTestBrushStrokes never names SettlementVolumeRendererPlacement
# (every call to IsPlaceable happens inside SettlementBrushOps.PaintBuilding itself, never from the test), so
# a rebind of just `SettlementBrushOps\.` is sound and complete — the same single-pattern shape SelfTestHeight
# uses above, and for the same reason: adding a second, unused pattern would make New-SettlementRebind throw.
# TileType/SettlementTileGrid/SettlementFootprint/InteriorFloor/Room/RoomSizing all live in unmutated files
# and resolve OUTWARD once SettlementBrushOps.cs is re-namespaced, and every list here is List<(int,int)> — a
# value tuple of ints, identical in both namespaces — so there is no IReadOnlyList<T> struct-covariance trap.

# MutBrushNoInterpolation: AppendSegment appends only the endpoint, so a fast drag paints a dotted line and
# the footprint is not 4-connected. SelfTestBrushStrokes' cell-count and contiguity claims must fail.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutBrushNoInterpolation' `
  '            int ci = from.i, cj = from.j;' `
  '            Push(into, to); if (into != null) return;   // MUTANT: no interpolation
            int ci = from.i, cj = from.j;' `
  'MutBrushNoInterpolation.cs'

# MutBrushIgnoresPlaceable: PaintBuilding keeps every cell of the stroke, including one another building
# already owns. SelfTestBrushStrokes' case 5 must fail.
#
# THE LOCATOR IS THE `continue` LINE, NOT THE WHOLE LOOP BODY — a deliberate departure from this task's brief,
# which paired the placement test and the dedup test in one short-circuited `if (IsPlaceable(...) &&
# keptSet.Add(c)) kept.Add(c);` expression and then wrote a locator for that exact line. The two never
# actually matched (the brief's Step 3 code carries `&& keptSet.Add(c)`, its Step 5 locator does not), which
# is caught here rather than passed through: New-SettlementMutant's pattern-match guard would have hard-failed
# on the mismatch, and folding the two tests into one expression would have made THIS mutant remove BOTH
# rules at once — a stroke over an occupied cell would also stop deduplicating, so a failure could not be
# blamed on the placement rule alone. Splitting the guard into two statements (SettlementBrushOps.cs,
# PaintBuilding) keeps the two mutants surgical: this one removes only the `continue`, so every cell reaches
# the dedup test regardless of what tile it lands on.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutBrushIgnoresPlaceable' `
  '                if (!SettlementVolumeRendererPlacement.IsPlaceable(grid.At(c.i, c.j))) continue;' `
  '                // MUTANT: the placement rule is ignored' `
  'MutBrushIgnoresPlaceable.cs'

# MutBrushNoComponentRepair: the connectivity repair skipped entirely — the deduped-but-unrepaired `kept`
# list is used as the painted footprint verbatim, so a stroke genuinely severed by a dropped obstacle keeps
# BOTH sides instead of only the component the DM started from. Case 5's L cannot catch this (its remainder
# is already one connected piece with or without the repair); SelfTestBrushStrokes' case 6 (a bent stroke
# severed by an obstacle at its bend, every other cell kept at Chebyshev distance 1 from it so none is
# eaten by the wall ring instead) must fail.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutBrushNoComponentRepair' `
  '            var keep = ComponentContainingFirst(kept);' `
  '            var keep = kept;   // MUTANT: connectivity repair skipped, the unrepaired kept list is used as-is' `
  'MutBrushNoComponentRepair.cs'

foreach ($mc in @('MutBrushNoInterpolation', 'MutBrushIgnoresPlaceable', 'MutBrushNoComponentRepair')) {
  New-SettlementRebind 'SelfTestBrushStrokes' $mc `
    @('SettlementBrushOps\.') `
    @("WorldGen.Generation.$mc.SettlementBrushOps.")
}

# ---- UNDO MUTANT (settlement-brushes, task 2): the one rule pinned by SettlementUndo.TryUndo. --------------
# SettlementUndo.cs defines ONE class and no data types (InteriorFloor/Room/Portal live in the unmutated
# DungeonData.cs, SettlementFootprint in its own unmutated file — both resolve OUTWARD), so the single-class
# New-SettlementMutant / rebind-only-"SettlementUndo." shape (SettlementFence/InteriorOps/FloorFootprint above)
# is sound here.

# MutUndoNoRestore: TryUndo pops and reports success without restoring anything.
New-SettlementMutant 'SettlementUndo.cs' 'MutUndoNoRestore' `
  '            floor.Rooms.Clear();' `
  '            if (e != null) return true;   // MUTANT: nothing is restored
            floor.Rooms.Clear();' `
  'MutUndoNoRestore.cs'

# ONE PATTERN, not the brief's two ('SettlementUndo\.' plus '\bSettlementUndo\b'), and this is a correction to
# the brief rather than an oversight: SelfTestSettlementUndo names SettlementUndo BOTH ways — `new
# SettlementUndo()` (bare) and `SettlementUndo.MaxDepth` (dotted) — but unlike every other two-pattern rebind
# above (SettlementConfig/SettlementGenerator, TileType/SettlementTileGrid, ...), both spellings here name the
# SAME identifier. Running the brief's two patterns in sequence double-substitutes: pattern 1 turns
# "SettlementUndo.MaxDepth" into "WorldGen.Generation.MutUndoNoRestore.SettlementUndo.MaxDepth", and pattern 2's
# bare `\bSettlementUndo\b` then matches AGAIN inside that very output (still bounded by dots on both sides),
# nesting the namespace a second time and producing an unresolvable
# "WorldGen.Generation.MutUndoNoRestore.WorldGen.Generation.MutUndoNoRestore.SettlementUndo" — confirmed by
# actually running the brief's two-pattern version first. A single bare-word pattern handles both spellings
# with no overlap: `\b` is satisfied by "(" (bare constructor) and by "." (dotted access) alike, and it does
# NOT match inside "SettlementUndoSelfTests" or "SelfTestSettlementUndo" (no boundary either side there).
New-SettlementRebind 'SelfTestSettlementUndo' 'MutUndoNoRestore' `
  @('\bSettlementUndo\b') `
  @('WorldGen.Generation.MutUndoNoRestore.SettlementUndo')

# MutUndoDiscardIsNoop (task 3b): Discard leaves the snapshot on the stack, so a dab that painted nothing
# still costs the DM one Ctrl+Z. SelfTestSettlementUndo's Discard case (case 6) must fail on both the Count
# claim (the stack still holds the pushed entry) and the TryUndo claim (that entry is still there to pop and
# restore) — see the case's own comment in SettlementUndoSelfTests.cs.
New-SettlementMutant 'SettlementUndo.cs' 'MutUndoDiscardIsNoop' `
  '            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);' `
  '            // MUTANT: Discard keeps the snapshot' `
  'MutUndoDiscardIsNoop.cs'

# Same single bare-word pattern as MutUndoNoRestore's rebind just above, for the identical reason: this is a
# DISTINCT mutant class, so it needs its own rebind call (New-SettlementRebind writes one file keyed on the
# mutant class name — a second call under the SAME class name would silently overwrite the first), but the
# pattern itself does not change: SelfTestSettlementUndo still names SettlementUndo both ways
# ("new SettlementUndo()" and "SettlementUndo.MaxDepth"), the same identifier either way.
New-SettlementRebind 'SelfTestSettlementUndo' 'MutUndoDiscardIsNoop' `
  @('\bSettlementUndo\b') `
  @('WorldGen.Generation.MutUndoDiscardIsNoop.SettlementUndo')

# ---- CHECKPOINT-1 MUTANTS (settlement-brushes, task 3a). --------------------------------------------------

# MutPlaceRefusesWall: the retired three-term rule is back, so a stroke stops at the town's own derived wall.
# SelfTestBrushStrokes' case 9 must fail (1 cell instead of 3).
New-SettlementMutant 'SettlementBrushOps.cs' 'MutPlaceRefusesWall' `
  'public static bool IsPlaceable(TileType type) => type != TileType.Building;' `
  'public static bool IsPlaceable(TileType type) => type != TileType.Building && type != TileType.Wall && type != TileType.Gate;   // MUTANT: the derived wall refuses a stroke again' `
  'MutPlaceRefusesWall.cs'

# MutPaintIgnoresOnField: PaintBuilding keeps cells whose centre lies past the 0..1 field, which the cascade
# then Clamp01s to the edge on every later drag. SelfTestBrushStrokes' case 10 must fail — but NOT on its
# cell COUNT, which is the trap in reading this: with every cell surviving the filter, kept[0] becomes the
# stroke's isolated first cell (-1,0), and ComponentContainingFirst collapses the footprint onto that one
# cell, so the count is 1 either way. What fails is case 10's off-field loop (a claimed cell with a negative
# centre) and its has32 claim (the only genuinely on-field cell is gone). Stated exactly because the count
# reading was in this comment before case 10 grew its (-1,0) cell, and was wrong the moment it did.
#
# TWO-LINE LOCATOR (task 5a widened this from one line): PaintRoad grew the IDENTICAL guard statement
# `if (!SettlementVolumeRendererPlacement.OnFieldCell(c.i, c.j)) continue;` for MutRoadIgnoresOnField below,
# and New-SettlementMutant's -replace is GLOBAL — a single-line locator here would now match BOTH occurrences
# and silently strip PaintRoad's guard as well, making this mutant PaintBuilding's AND PaintRoad's on-field
# check at once and no longer distinguishable from MutRoadIgnoresOnField by which method it mutates (confirmed
# empirically: before this widening, `gen/MutPaintIgnoresOnField.cs` carried the MUTANT marker inside PaintRoad
# too). Extending the locator to include the very next line — IsPlaceable(grid.At(c.i, c.j)), unique to
# PaintBuilding, confirmed by grep — makes the two-line span match only PaintBuilding's copy again, `n forcing
# a bare LF between the lines regardless of how this .ps1 file itself is saved (same reasoning as
# MutEraseStaleCheck and MutRoadIgnoresOnField).
$mutPaintIgnoresOnFieldFrom = "                if (!SettlementVolumeRendererPlacement.OnFieldCell(c.i, c.j)) continue;`n                if (!SettlementVolumeRendererPlacement.IsPlaceable(grid.At(c.i, c.j))) continue;"
$mutPaintIgnoresOnFieldTo   = "                // MUTANT: the on-field bound is ignored`n                if (!SettlementVolumeRendererPlacement.IsPlaceable(grid.At(c.i, c.j))) continue;"
New-SettlementMutant 'SettlementBrushOps.cs' 'MutPaintIgnoresOnField' `
  $mutPaintIgnoresOnFieldFrom $mutPaintIgnoresOnFieldTo 'MutPaintIgnoresOnField.cs'

# MutOnFieldNoLowerBound: OnFieldCell drops only its LOWER x-bound, so a NEGATIVE normalized X (the direction
# the deleted renderer doc flagged as the one actually reachable in production — a large city's margin band)
# is no longer refused, while the upper bound (and both y-bound terms) stay intact. MutPaintIgnoresOnField
# above deletes the WHOLE on-field call and is caught by case 10's `> 1f` claims alone; this mutant instead
# targets the specific half review finding M1 named as unpinned — a partial mutation that a whole-line-deletion
# mutant cannot distinguish from a correct rule. SelfTestBrushStrokes' extended case 10 (the (-1,0) cell) must
# fail: (-1,0) survives placement, becomes kept[0] (it is not 4-adjacent to anything else in the stroke), and
# the connectivity repair then collapses the whole footprint onto that one off-field cell instead of (32,0).
New-SettlementMutant 'SettlementBrushOps.cs' 'MutOnFieldNoLowerBound' `
  '            return nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f;' `
  '            return nx <= 1f && ny >= 0f && ny <= 1f;   // MUTANT: a negative normalized X is no longer refused' `
  'MutOnFieldNoLowerBound.cs'

# MutPaintIgnoresRoomOwnership: PaintBuilding's new room-ownership rule (checkpoint-1 reversal, finding I3)
# deleted — a stroke may found a Building on top of any NON-BUILDING room's own cell (in practice a gate's).
# On a town whose gate was previously dragged (so the gate's own cell is normalized onto the wall/gate cell
# and reads Wall/Gate rather than Road), IsPlaceable alone no longer refuses that cell either (checkpoint-1's
# own amendment), so nothing would stop the founded Building from claiming it and — via Precedes — permanently
# hiding the gate from drawing and clicking. SelfTestBrushStrokes' case 11 must fail: the painted footprint
# claims cell (2,0), the gate room's own cell.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutPaintIgnoresRoomOwnership' `
  '                if (owned.Contains(c)) continue;' `
  '                // MUTANT: the room-ownership rule is ignored' `
  'MutPaintIgnoresRoomOwnership.cs'

# ---- ROAD ON-FIELD BOUND (settlement-brushes, task 5a): PaintRoad gets the bound PaintBuilding already has. -

# MutRoadIgnoresOnField: PaintRoad keeps cells whose centre lies past the 0..1 field. They cannot be Clamp01ed
# (a street cell has no room) but they DO widen FitBoundsFor's extent, and the view never zooms back in
# (DungeonViewController.cs:373) — so one stroke toward the panel edge shrinks the whole town until the cells
# are erased. SelfTestBrushStrokes' case 12 must fail on its added-count claim AND on its stored-streets loop.
# NOT MutPaintIgnoresOnField's shadow: that mutant deletes PaintBuilding's guard and is caught by cases 10/11
# (a painted BUILDING's footprint and cell count); this one deletes PaintRoad's own separate guard and is
# caught only by case 12 (PaintRoad's returned added-count and the stored StreetCells) — the two mutate
# different methods and neither case's fixture reaches the other method at all.
#
# TWO-LINE LOCATOR, built with `n rather than a raw multi-line literal embedded in this .ps1 file: sync.ps1
# itself is CRLF but SettlementBrushOps.cs is LF-only (a previous task's `git checkout --` on this exact file
# let core.autocrlf re-CRLF it and broke MutEraseStaleCheck's own multi-line locator the same way, just above)
# — `n forces a bare LF byte regardless of how this .ps1 file is saved, so the pattern matches the source file
# byte-for-byte instead of depending on this file's own line endings.
$mutRoadFrom = "                if (!SettlementVolumeRendererPlacement.OnFieldCell(c.i, c.j)) continue;`n                if (seen.Add(c)) { all.Add(c); added++; }"
$mutRoadTo   = "                if (seen.Add(c)) { all.Add(c); added++; }   // MUTANT: the road brush ignores the field bound"
New-SettlementMutant 'SettlementBrushOps.cs' 'MutRoadIgnoresOnField' `
  $mutRoadFrom $mutRoadTo 'MutRoadIgnoresOnField.cs'

foreach ($mc in @('MutPlaceRefusesWall', 'MutPaintIgnoresOnField', 'MutOnFieldNoLowerBound',
                  'MutPaintIgnoresRoomOwnership', 'MutRoadIgnoresOnField')) {
  New-SettlementRebind 'SelfTestBrushStrokes' $mc `
    @('SettlementBrushOps\.') `
    @("WorldGen.Generation.$mc.SettlementBrushOps.")
}

# MutPreviewBuildingsIgnored: Build drops the preview cells entirely. SelfTestPreviewBuildings must fail on
# BOTH the "drew as Building" claim and the ring-re-derives claim ((7,0) stays outside the ring).
New-SettlementMutant 'SettlementTileGrid.cs' 'MutPreviewBuildingsIgnored' `
  '            if (extraBuildings != null)' `
  '            if (false && extraBuildings != null)   // MUTANT: the brush preview is never drawn' `
  'MutPreviewBuildingsIgnored.cs'

# MutPreviewBuildingsNotInExtent: the preview reaches the WRITE but not Allocate's extent, so a cell past the
# margin is silently dropped — the exact split-union regression SelfTestPreviewStreets' own comment records.
# SelfTestPreviewBuildings' InBounds claim must fail on (4,0) and (5,0).
New-SettlementMutant 'SettlementTileGrid.cs' 'MutPreviewBuildingsNotInExtent' `
  '                foreach (var c in extraCells) Fold(c.i, c.j);' `
  '                { }   // MUTANT: preview cells never widen the extent' `
  'MutPreviewBuildingsNotInExtent.cs'

foreach ($mc in @('MutPreviewBuildingsIgnored', 'MutPreviewBuildingsNotInExtent')) {
  New-SettlementRebind 'SelfTestPreviewBuildings' $mc `
    @('SettlementTileGrid\.', '\bTileType\b') `
    @("WorldGen.Generation.$mc.SettlementTileGrid.", "WorldGen.Generation.$mc.TileType")
}

# ---- ERASER MUTANTS (settlement-brushes, task 4; the building-split half retired and replaced in task 7):
# the street rule pinned by CanErase, the re-evaluation rule pinned by Erase's re-ask-after-every-removal, and
# (task 7) the split-and-identity rule pinned by RemoveBuildingCell plus the gesture-order rule pinned by
# Erase's own iteration. All are caught via SelfTestEraseRefusal, which lives in SettlementBrushOpsSelfTests.cs
# (the eighth source above) — same single-pattern rebind shape as the
# other SettlementBrushOps mutants: SettlementStreetOps/SettlementFootprint/SettlementTileGrid all live in
# unmutated files and resolve OUTWARD once SettlementBrushOps.cs is re-namespaced, and the test's own
# `SettlementStreetOps.MissingAccess(floor)` call (case 5's post-stroke check) must keep judging the mutant's
# OUTPUT with the REAL repair logic, not a mutated copy of it — so `SettlementStreetOps.` is deliberately left
# out of the rebind pattern list, exactly as SelfTestBrushStrokes never names SettlementVolumeRendererPlacement.

# MutEraseAllowsStranding: CanErase's street half always allows, so a lane that is a house's only access can
# be erased anyway. SelfTestEraseRefusal case 1 ("was allowed") must fail — and case 5 also fires (removing
# both cells of the two-cell-wide lane now succeeds), reported as a second symptom of the same cause, not a
# separate rule.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseAllowsStranding' `
  '            bool safe = SettlementStreetOps.MissingAccess(floor).Count == 0;' `
  '            bool safe = true;   // MUTANT: a lane can always be erased' `
  'MutEraseAllowsStranding.cs'

# MutEraseStaleCheck: Erase decides every cell's safety UP FRONT, against the floor as it stood when the
# stroke began, instead of re-asking CanErase after every removal. A single-line locator cannot express this
# (there is no single statement that IS "decide in advance" — the precompute needs its own statement AND the
# loop's guard needs to read from it instead of calling CanErase live), so the locator spans the loop's
# opening three lines and rewrites all three: `stale` is populated by asking CanErase once per cell against
# the UNMODIFIED floor, before the removal loop runs at all, and the loop's own guard reads `stale` instead of
# ever calling CanErase again. Rewritten for Task 7 (the loop now reads straight from the caller's `cells`,
# gesture order, never sorted into `ordered`) but the rule under test is unchanged. SelfTestEraseRefusal case
# 5 must fail on the "removed N, expected exactly 1" claim: (5,4) and (6,4) are BOTH individually safe on the
# intact floor (each still fronts the shared two-cell building via the other column), so `stale` holds both
# and this mutant erases 2, not 1 — the exact gap between "each cell is safe alone" and "both together are
# not" that the re-ask rule exists to close. Cases 1/2/4 do not distinguish stale from fresh (the floor never
# changes between two safe/refused verdicts in any of them), so this must be the only case that fails.
$mutEraseStaleFrom = "            foreach (var cell in cells)`n            {`n                if (!CanErase(floor, cell)) continue;"
$mutEraseStaleTo   = "            var stale = new HashSet<(int i, int j)>();`n            foreach (var c in cells) if (CanErase(floor, c)) stale.Add(c);   // MUTANT: decided before any removal, against the floor as it stood when the stroke began`n            foreach (var cell in cells)`n            {`n                if (!stale.Contains(cell)) continue;"
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseStaleCheck' `
  $mutEraseStaleFrom $mutEraseStaleTo 'MutEraseStaleCheck.cs'

# MutEraseNoRestore (review finding M2): CanErase's trial removal is never restored, so a CALL THAT MERELY
# ASKS "can I erase this?" permanently corrupts StreetCells — the worst-shaped bug in this file, since it
# fires from a predicate the eraser's hover (Task 5) calls every frame, on cells the DM never actually erased.
# Traced two ways, and BOTH are already covered by an existing assertion with no mutant of its own until now:
#   - Deleting the restore outright leaves case 2's CanErase((6,4)) returning true (street rule reads
#     MissingAccess == 0 before ever restoring) but StreetCells stays {(5,4)} — the FOLLOWING
#     Erase(floor, Cells((6,4))) then finds streets.Remove((6,4)) false and returns 0: case 2a2 fires
#     ("Erase did not remove the spare cell (6,4)").
#   - This single-line deletion is the shape review finding M2 asked to pin: it removes the restore on BOTH
#     the safe and unsafe branches at once (there is only one restore statement, after the branch that
#     computes `safe`), so the mutant exercises the "restore deleted outright" trace above. Expected killer:
#     case 2a2.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseNoRestore' `
  '            floor.SettlementParams.StreetCells = saved;               // ALWAYS restored, on both branches' `
  '            // MUTANT: the restore is skipped — a trial removal corrupts StreetCells permanently' `
  'MutEraseNoRestore.cs'

# MutEraseNoSplit: the disconnected remainder is written back as ONE room, so a building erased through the
# middle becomes a single room whose footprint is two separate pieces — which DungeonValidator reports as an
# Error and which draws as one building with a hole in it. SelfTestEraseRefusal's case 3 (room count) and
# case 6 (the surviving original's cell count) must both fail.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseNoSplit' `
  '                var comps = Components4(rest);' `
  '                var comps = new List<List<(int i, int j)>> { rest };   // MUTANT: never split' `
  'MutEraseNoSplit.cs'

# MutEraseSplitKeepsSmallest: the identity follows the SMALLEST piece instead of the largest, so the DM's
# building name, notes and interior end up on the offcut. Case 6 must fail on the surviving original's cell
# count — NOT on its title: `Title` lives on `r`, and `r` survives the split either way (only which cells
# land on it changes), so the title assertion cannot distinguish this mutant from correct code. It guards a
# DIFFERENT regression instead — a split that copies identity onto the offcut too — via case 6's duplication
# loop. Case 7 (the tie) cannot catch this mutant either — both pieces are size 1 there — which is why case
# 6's fixture is deliberately lopsided.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseSplitKeepsSmallest' `
  '                    if (comps[c].Count > comps[best].Count) { best = c; continue; }' `
  '                    if (comps[c].Count < comps[best].Count) { best = c; continue; }   // MUTANT: identity to the smallest' `
  'MutEraseSplitKeepsSmallest.cs'

# MutEraseRowMajorOrder: the retired row-major sort is back, so a stroke no longer erases in the order it was
# drawn. Case 8 must fail on the ordering that starts at (6,4) — under the sort, BOTH orderings remove (5,4).
# This is also what would silently desynchronise Task 8's live preview from the commit.
New-SettlementMutant 'SettlementBrushOps.cs' 'MutEraseRowMajorOrder' `
  '            foreach (var cell in cells)' `
  "            var sorted = new List<(int i, int j)>(cells); sorted.Sort(RowMajor);   // MUTANT: row-major again`n            foreach (var cell in sorted)" `
  'MutEraseRowMajorOrder.cs'

foreach ($mc in @('MutEraseAllowsStranding', 'MutEraseStaleCheck', 'MutEraseNoRestore',
                  'MutEraseNoSplit', 'MutEraseSplitKeepsSmallest', 'MutEraseRowMajorOrder')) {
  New-SettlementRebind 'SelfTestEraseRefusal' $mc `
    @('SettlementBrushOps\.') `
    @("WorldGen.Generation.$mc.SettlementBrushOps.")
}

$variants = @('SpreadOnlyLayout', 'CompactOnlyLayout', 'CompactNoSlideLayout', 'CompactSlideNoCuts',
              'PreSlideLayout', 'PreSlideSpreadOnly', 'PreSlideCompactOnly', 'PreReviewLayout', 'NoPlainRunLayout')
# The two settlement counts are COUNTED, not quoted: the literal 46 that stood here was already 8 short of the
# real figure before Task 5 removed seven mutants and added one, and a hand-maintained number that nobody can
# check is worse than no number. Everything else is still a literal — those groups have not changed in a long
# time. A settlement mutant writes gen/<Name>.cs and gen/SelfTests_<Name>.cs, so the second count is over the
# rebound copies that are NOT the packer's 14 or the battle grid's 4.
$settlementMutants = (Get-ChildItem $gen -Filter 'SelfTests_*.cs').Count - 14 - 4
Write-Host "synced $($files.Count) sources + $($variants.Count) variants + 10 mutants + 2 traces + 14 rebound test copies + 4 battle-grid mutants + 4 battle-grid rebound test copies + $settlementMutants settlement mutants + $settlementMutants settlement rebound test copies into gen/"

