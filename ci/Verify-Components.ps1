# Verifies that the combined launcher EXE embeds exactly the component files
# BUILD_COMBINED.ps1 packed, by hashing every embedded resource against the
# file on disk. Catches stale/forgotten rebuilds.
param([string]$RepoRoot = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

# Avoids Chinese literals so the script stays ASCII: the combined launcher is
# the largest root EXE, the tML injector the smallest; the vanilla toolkit has
# a single EXE.
$rootExes = Get-ChildItem -LiteralPath $RepoRoot -Filter *.exe | Sort-Object Length
if ($rootExes.Count -lt 2) { throw "expected at least two EXEs under $RepoRoot" }
$combined = $rootExes[-1].FullName
$tmlInjector = $rootExes[0].FullName
$vanillaDir = Join-Path $RepoRoot 'Vanilla1456Toolkit'
$vanillaInjector = Get-ChildItem -LiteralPath $vanillaDir -Filter *.exe |
    Select-Object -First 1

$map = @(
    @('payload.vanilla.injector', $vanillaInjector.FullName),
    @('payload.vanilla.native',   (Join-Path $vanillaDir 'Terraria1456Toolkit.Bootstrap.dll')),
    @('payload.vanilla.managed',  (Join-Path $vanillaDir 'Terraria1456Toolkit.Managed.dll')),
    @('payload.tml.injector',     $tmlInjector),
    @('payload.tml.native',       (Join-Path $RepoRoot 'TerrariaTmlToolkit.Native.dll')),
    @('payload.tml.bootstrap',    (Join-Path $RepoRoot 'TerrariaTmlToolkit.Bootstrap.dll')),
    @('payload.tml.ui',           (Join-Path $RepoRoot 'TerrariaTmlToolkit.UI.dll')),
    @('payload.tml.config',       (Join-Path $RepoRoot 'TerrariaTmlToolkit.runtimeconfig.json')),
    @('payload.tml.hostfxr',      (Join-Path $RepoRoot 'hostfxr.dll'))
)

$asm = [System.Reflection.Assembly]::LoadFile($combined)
$sha = [System.Security.Cryptography.SHA256]::Create()
$fail = 0
foreach ($pair in $map) {
    $stream = $asm.GetManifestResourceStream($pair[0])
    if ($null -eq $stream) {
        Write-Host ($pair[0].PadRight(26) + 'MISSING-IN-EXE')
        $fail = 1
        continue
    }
    $ms = New-Object System.IO.MemoryStream
    $stream.CopyTo($ms)
    $stream.Dispose()
    $resourceHash = [BitConverter]::ToString($sha.ComputeHash($ms.ToArray())).Replace('-', '').ToLower()
    if (-not (Test-Path -LiteralPath $pair[1])) {
        Write-Host ($pair[0].PadRight(26) + 'MISSING-FILE  ' + $pair[1])
        $fail = 1
        continue
    }
    $fileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pair[1]).Hash.ToLower()
    if ($resourceHash -ne $fileHash) { $fail = 1 }
    Write-Host ($pair[0].PadRight(26) + $(if ($resourceHash -eq $fileHash) { 'MATCH' } else { 'DIFF' }))
}
if ($fail -ne 0) { exit 1 }
