$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

$out = Join-Path $root 'Terraria全物品修改器_原版TML双兼容.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/out:$out" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
    "/resource:$root\Vanilla1456Toolkit\Terraria全物品修改器.exe,payload.vanilla.injector" `
    "/resource:$root\Vanilla1456Toolkit\Terraria1456Toolkit.Bootstrap.dll,payload.vanilla.native" `
    "/resource:$root\Vanilla1456Toolkit\Terraria1456Toolkit.Managed.dll,payload.vanilla.managed" `
    "/resource:$root\TerrariaTML修改器.exe,payload.tml.injector" `
    "/resource:$root\TerrariaTmlToolkit.Native.dll,payload.tml.native" `
    "/resource:$root\TerrariaTmlToolkit.Bootstrap.dll,payload.tml.bootstrap" `
    "/resource:$root\TerrariaTmlToolkit.UI.dll,payload.tml.ui" `
    "/resource:$root\TerrariaTmlToolkit.runtimeconfig.json,payload.tml.config" `
    "/resource:$root\hostfxr.dll,payload.tml.hostfxr" `
    "$root\CombinedToolkit\CombinedLauncher.cs"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
Get-FileHash -LiteralPath $out -Algorithm SHA256 | Format-List Algorithm,Hash,Path
