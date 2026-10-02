# Generates a csc response file for the tModLoader UI build.
# The committed ui.rsp hardcodes one machine's tModLoader install; this script
# discovers the same references under any tML directory and dotnet shared root,
# so CI (or a fresh machine) can compile without editing paths.
param(
    [Parameter(Mandatory = $true)][string]$TmlDir,
    [Parameter(Mandatory = $true)][string]$DotnetRoot,
    [string]$RepoRoot = (Get-Location).Path,
    [string]$OutFile = 'ui.ci.rsp',
    [string]$OutputPath = '',
    # When set, both shared frameworks must be exactly this version (e.g.
    # '8.0.0'). Compiling against the oldest supported runtime keeps the build
    # loadable on every later 8.x install; leaving it empty picks the newest.
    [string]$RuntimeVersion = ''
)
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot 'TerrariaTmlToolkit.UI.dll' }

function LatestVersionDir([string]$parent) {
    $dirs = Get-ChildItem -LiteralPath $parent -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+' }
    if (-not $dirs) { throw "no version directories under $parent" }
    $dirs |
        Sort-Object { [version]($_.Name -replace '^(\d+(\.\d+){0,3}).*$', '$1') } -Descending |
        Select-Object -First 1
}

function ResolveVersionDir([string]$parent, [string]$exact) {
    if (-not $exact) { return LatestVersionDir $parent }
    $path = Join-Path $parent $exact
    if (-not (Test-Path -LiteralPath $path)) {
        throw "runtime $exact not found under $parent"
    }
    Get-Item -LiteralPath $path
}

function RequireFile([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { throw "missing reference: $p" }
    $p
}

function FirstGlob([string]$pattern) {
    $hit = Get-ChildItem -Path $pattern -File |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $hit) { throw "missing reference: $pattern" }
    $hit.FullName
}

# Shared runtime folders also hold native binaries (coreclr.dll, hostpolicy.dll,
# wpfgfx_cor3.dll, ...), which csc rejects with CS0009 when referenced.
function IsManagedAssembly([string]$path) {
    try {
        [void][System.Reflection.AssemblyName]::GetAssemblyName($path)
        return $true
    }
    catch {
        return $false
    }
}

$netcore = ResolveVersionDir (Join-Path $DotnetRoot 'shared\Microsoft.NETCore.App') $RuntimeVersion
$desktop = ResolveVersionDir (Join-Path $DotnetRoot 'shared\Microsoft.WindowsDesktop.App') $RuntimeVersion
$tml = (Resolve-Path -LiteralPath $TmlDir).Path

$refs = @()
$netcoreRefs = Get-ChildItem -LiteralPath $netcore.FullName -Filter *.dll |
    Sort-Object Name |
    Where-Object { IsManagedAssembly $_.FullName }
$refs += $netcoreRefs | ForEach-Object { $_.FullName }
$netcoreNames = @{}
$netcoreRefs | ForEach-Object { $netcoreNames[$_.Name] = $true }
# Both shared frameworks ship some of the same assemblies (WindowsBase.dll and
# friends); referencing both trips CS1703, so NETCore wins by name.
$refs += Get-ChildItem -LiteralPath $desktop.FullName -Filter *.dll |
    Sort-Object Name |
    Where-Object { -not $netcoreNames.ContainsKey($_.Name) } |
    Where-Object { IsManagedAssembly $_.FullName } |
    ForEach-Object { $_.FullName }
$refs += RequireFile (Join-Path $tml 'tModLoader.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\TerrariaHooks\*\TerrariaHooks.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\FNA\*\FNA.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\ReLogic\*\ReLogic.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\monomod.runtimedetour\*\lib\net8.0\MonoMod.RuntimeDetour.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\monomod.utils\*\lib\net8.0\MonoMod.Utils.dll')
$refs += FirstGlob (Join-Path $tml 'Libraries\mono.cecil\*\lib\netstandard2.0\Mono.Cecil.dll')

$lines = @('/nologo', '/nostdlib+', '/langversion:latest', '/nullable:disable', '/target:library', '/optimize+')
$lines += $refs | ForEach-Object { '/reference:"' + $_ + '"' }
$lines += '/out:"' + $OutputPath + '"'
$lines += @('UiEntry.cs', 'TmlTrainerForm.cs', 'TmlInventorySlotGrid.cs', 'TmlViewZoom.cs') |
    ForEach-Object { '"' + (Join-Path $RepoRoot $_) + '"' }

[System.IO.File]::WriteAllLines(
    (Join-Path (Get-Location).Path $OutFile),
    $lines,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("wrote {0} ({1} references, NETCore {2}, WindowsDesktop {3})" -f `
    $OutFile, $refs.Count, $netcore.Name, $desktop.Name)
