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
      'WorldObjectRef.cs'
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
