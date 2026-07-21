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
  'BattleGridData.cs', 'BattleGridSelfTests.cs'
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

$variants = @('SpreadOnlyLayout', 'CompactOnlyLayout', 'CompactNoSlideLayout', 'CompactSlideNoCuts',
              'PreSlideLayout', 'PreSlideSpreadOnly', 'PreSlideCompactOnly', 'PreReviewLayout', 'NoPlainRunLayout')
Write-Host "synced $($files.Count) sources + $($variants.Count) variants + 10 mutants + 2 traces + 14 rebound test copies into gen/"

