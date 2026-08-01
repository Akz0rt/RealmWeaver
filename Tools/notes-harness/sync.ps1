# Copies the REAL Unity notes sources into gen/. Nothing under gen/ is edited by hand — re-run this after
# every source change.
#
#   powershell -File sync.ps1
#
# Unlike f2-harness's sync, a missing source is SKIPPED rather than fatal: during TDD the implementation
# file legitimately does not exist yet, and the red state we want is a compile error naming the missing
# type, not a PowerShell stop. Every skip is printed, so a typo in $files can never pass unnoticed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$gen  = Join-Path $here 'gen'

if (Test-Path $gen) { Remove-Item -Recurse -Force $gen }
New-Item -ItemType Directory -Path $gen | Out-Null

# THE BUILD OUTPUT GOES WITH gen/, AND THIS IS A CORRECTNESS FIX, NOT A TIDY-UP. Copy-Item below stamps
# each copy with its SOURCE's LastWriteTime, not "now" — so a file synced from Assets/ can land here OLDER
# than an assembly MSBuild produced from a previous sync. MSBuild's incremental check is timestamp-based,
# concludes the output is up to date, and `dotnet run` then executes the PREVIOUS build's assembly while
# reporting on sources it never compiled.
#
# That is not theoretical: it was hit during Task 11's review round. A reviewer had mutated the gen/ copies
# to reproduce the reported mutants and rebuilt; the next sync restored the real sources but did not
# invalidate the output, so the following run reported one failure that no source in the tree could produce.
# It resolved itself on a later rebuild, which is the dangerous part — the same mechanism produces a false
# PASS exactly as easily as the false FAIL that happened to be visible, and a false PASS on a self-test gate
# is invisible by construction.
#
# The cost is that every run is a full compile of ~15 files, a few seconds. A gate that can lie is not worth
# the seconds. Deleting obj/ as well as bin/ because a stale project.assets.json can pin an old output path.
foreach ($stale in @('obj', 'bin')) {
  $path = Join-Path $here $stale
  if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}

# Each source directory paired with the files pulled from it. Two directories rather than one because the
# workspace-shell layer (WorldGen.Workspace.Data) lives beside, not inside, the notes layer.
$sources = @(
  @{ Dir = (Join-Path $repo 'Assets\WorldGen\Notes\Data'); Files = @(
      'NotesData.cs',
      'NotesDocOps.cs',
      'NotesDocOpsSelfTests.cs',
      'DocKeyboardOps.cs',
      'DocKeyboardOpsSelfTests.cs'
    ) },
  @{ Dir = (Join-Path $repo 'Assets\WorldGen\Workspace\Data'); Files = @(
      'WorkspaceLayout.cs',
      'WorkspaceOps.cs',
      'WorkspaceOpsSelfTests.cs',
      'NavigatorTree.cs',
      'NavigatorTreeSelfTests.cs',
      'QuickOpen.cs',
      'QuickOpenSelfTests.cs',
      'WorldObjectRef.cs',
      'SurfaceIds.cs',
      'SurfaceIdsSelfTests.cs'
    ) }
)

$copied = 0
$skipped = @()
foreach ($source in $sources) {
  foreach ($f in $source.Files) {
    $from = Join-Path $source.Dir $f
    if (Test-Path $from) { Copy-Item $from (Join-Path $gen $f); $copied++ }
    else { $skipped += $f }
  }
}

Write-Host "synced $copied source(s) into gen/"
if ($skipped.Count -gt 0) { Write-Host "SKIPPED (not present yet): $($skipped -join ', ')" }
