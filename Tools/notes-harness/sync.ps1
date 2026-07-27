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
$src  = Join-Path $repo 'Assets\WorldGen\Notes\Data'
$gen  = Join-Path $here 'gen'

if (Test-Path $gen) { Remove-Item -Recurse -Force $gen }
New-Item -ItemType Directory -Path $gen | Out-Null

$files = @(
  'NotesData.cs',
  'NotesDocOps.cs',
  'NotesDocOpsSelfTests.cs'
)

$copied = 0
$skipped = @()
foreach ($f in $files) {
  $from = Join-Path $src $f
  if (Test-Path $from) { Copy-Item $from (Join-Path $gen $f); $copied++ }
  else { $skipped += $f }
}

Write-Host "synced $copied source(s) into gen/"
if ($skipped.Count -gt 0) { Write-Host "SKIPPED (not present yet): $($skipped -join ', ')" }
