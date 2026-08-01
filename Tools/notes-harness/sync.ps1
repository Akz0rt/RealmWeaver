# Copies the REAL Unity notes sources into gen/. Nothing under gen/ is edited by hand — re-run this after
# every source change.
#
#   powershell -File sync.ps1
#
# EVERY .cs IN EACH DIRECTORY, BY GLOB — never a hand-written file list, and that is a correctness
# property rather than a convenience. Program.cs reflects over every *SelfTests type it can see, under the
# comment "adding a suite must never require remembering to register it here". That was true of Program.cs
# and FALSE OF THE PIPELINE while this script copied by an explicit list: a new suite dropped into either
# directory was never copied, never compiled, and therefore invisible to the reflection built to make
# invisibility impossible. The old "every skip is printed" reassurance did not cover it either, because a
# file that was never listed produces no skip line at all. Green by absence, inside the gate that measures
# everything else, in a script already caught serving stale assemblies once.
#
# A file that does not exist yet is simply not copied, which preserves the TDD red state this script has
# always wanted: a compile error naming the missing type, not a PowerShell stop.
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

# Two directories rather than one because the workspace-shell layer (WorldGen.Workspace.Data) lives beside,
# not inside, the notes layer. Both are PURE BY RULE — no UnityEngine outside the stubbed test attributes —
# which is what makes "copy everything here" safe: a file that does not belong in the harness does not
# belong in these directories either, so the glob and that boundary are the same rule, enforced once.
$sourceDirs = @(
  (Join-Path $repo 'Assets\WorldGen\Notes\Data'),
  (Join-Path $repo 'Assets\WorldGen\Workspace\Data')
)

$copied = 0
$names = @{}
foreach ($dir in $sourceDirs) {
  # Fatal, unlike a missing FILE: a missing source DIRECTORY means the layout moved, and silently syncing
  # nothing from it would leave a green run over an empty suite — the exact failure this glob removes.
  if (-not (Test-Path $dir)) { throw "sync.ps1: source directory not found: $dir" }
  foreach ($file in Get-ChildItem -Path $dir -Filter *.cs -File) {
    # gen/ is FLAT, so two same-named files in different source directories would silently overwrite each
    # other while the count still looked right. Fatal rather than last-one-wins.
    if ($names.ContainsKey($file.Name)) { throw "sync.ps1: duplicate file name across source directories: $($file.Name)" }
    $names[$file.Name] = $true
    Copy-Item $file.FullName (Join-Path $gen $file.Name)
    $copied++
  }
}

Write-Host "synced $copied source(s) into gen/ (every *.cs under $($sourceDirs.Count) source directories)"
