# Builds both injector launchers with the platforms the injection chains expect:
# the tML injector must be x64 (targets the x64 tModLoader process) and the
# vanilla injector x86 (targets the x86 Terraria process); both are WinExe
# because they may show MessageBox prompts before injection.
param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$OutTml = '',
    [string]$OutVanilla = ''
)
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $OutTml) { $OutTml = Join-Path $RepoRoot 'TerrariaTML修改器.exe' }
if (-not $OutVanilla) { $OutVanilla = Join-Path $RepoRoot 'Vanilla1456Toolkit\Terraria全物品修改器.exe' }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found: $csc" }

& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$OutTml" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
    (Join-Path $RepoRoot 'TmlInjector.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $csc /nologo /target:winexe /platform:x86 /optimize+ "/out:$OutVanilla" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
    (Join-Path $RepoRoot 'Vanilla1456Toolkit\Launcher.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'injectors built'
