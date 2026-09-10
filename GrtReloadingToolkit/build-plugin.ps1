<#
  Publishes the plugin and assembles drop-in GRT plugin folders.

    ./dist/ReloadingToolkit/       self-contained (~64 MB exe) — no runtime needed, host it
    ./dist/ReloadingToolkit-lite/  framework-dependent (~1.5 MB zip) — needs .NET 8 Desktop Runtime

  Usage:
    ./build-plugin.ps1
    ./build-plugin.ps1 -GrtDir "C:\...\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
    ./build-plugin.ps1 -Zip     # also make dist\*.zip
#>
param(
    [string]$Configuration = "Release",
    [string]$GrtDir = "",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
# dotnet off PATH: the hardcoded "C:\Program Files\dotnet\dotnet.exe" broke every install that
# isn't the default x64 machine-wide one — winget, per-user, ARM64, side-by-side.
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
           Select-Object -First 1).Source
if (-not $dotnet) {
    $dotnet = @("$env:ProgramFiles\dotnet\dotnet.exe",
                "${env:ProgramFiles(x86)}\dotnet\dotnet.exe",
                "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") |
              Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $dotnet) { throw "dotnet not found on PATH. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" }
$csproj = Join-Path $root "GrtReloadingToolkit.csproj"

# The version lives in Directory.Build.props and nowhere else: the assemblies get it from the
# compiler, the window titles read it back off the assembly, and the shipped manifest is stamped
# with it below. GRT reads com.grt.plugin.xml, so an unstamped copy is how a 0.1.5 build ends up
# announcing itself as 0.1.0.
$propsPath = Join-Path $root "..\Directory.Build.props"
$version = ([xml](Get-Content $propsPath)).SelectSingleNode("/Project/PropertyGroup/Version").InnerText.Trim()
if (-not $version) { throw "no <Version> in $propsPath" }
Write-Host "==> version $version"

# Rewrites the manifest's version attribute in place. Line-anchored so it can't hit the
# `<?xml version="1.0"?>` declaration, and the result is re-parsed and checked, because a
# silently unstamped manifest is exactly the drift this is here to stop.
function Stamp($manifest) {
    $txt = [regex]::Replace((Get-Content $manifest -Raw), '(?m)^(\s*version\s*=\s*")[^"]*(")', "`${1}$version`${2}")
    Set-Content $manifest -Value $txt -NoNewline -Encoding UTF8
    $got = ([xml](Get-Content $manifest)).SelectSingleNode("/GordonsReloadingTool/plugin").GetAttribute("version")
    if ($got -ne $version) { throw "manifest version is '$got', expected '$version' — check the version attribute in plugin\com.grt.plugin.xml" }
}

function Assemble($outDir, $payloadDir) {
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item (Join-Path $payloadDir "*") $outDir -Recurse
    Remove-Item -Recurse -Force (Join-Path $outDir "plugin") -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root "plugin\com.grt.plugin.xml") $outDir -Force
    Stamp (Join-Path $outDir "com.grt.plugin.xml")
    Copy-Item (Join-Path $root "plugin\media") $outDir -Recurse
    Copy-Item (Join-Path $root "README.md") $outDir
    Copy-Item (Join-Path $root "MANUAL.md") $outDir
    $docs = Join-Path $root "docs"
    if (Test-Path $docs) { Copy-Item $docs $outDir -Recurse }
}

# ---- 1. self-contained single file ----
Write-Host "==> publish: self-contained single file"
$scDir = Join-Path $root "artifacts\publish-sc"
& $dotnet publish $csproj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $scDir | Out-Host
$pluginOut = Join-Path $root "dist\ReloadingToolkit"
# self-contained publish dir also holds the plugin\ copy from csproj <None> — Assemble strips it
Assemble $pluginOut $scDir
Get-ChildItem $pluginOut -File | Where-Object { $_.Name -notin @('GRT_Reloading_Toolkit.exe','com.grt.plugin.xml','README.md','MANUAL.md') } | Remove-Item -Force

# ---- 2. framework-dependent (win-x64, needs .NET 8 Desktop Runtime) ----
Write-Host "==> publish: framework-dependent (lite)"
$fdDir = Join-Path $root "artifacts\publish-fd"
& $dotnet publish $csproj -c $Configuration -p:RuntimeIdentifier=win-x64 -p:SelfContained=false `
    -p:DebugType=none -p:SatelliteResourceLanguages=en -o $fdDir | Out-Host
$liteOut = Join-Path $root "dist\ReloadingToolkit-lite"
Assemble $liteOut $fdDir

Write-Host "==> dist\ReloadingToolkit      $('{0:N0}' -f ((Get-ChildItem $pluginOut -Recurse -File | Measure-Object Length -Sum).Sum/1KB)) KB"
Write-Host "==> dist\ReloadingToolkit-lite $('{0:N0}' -f ((Get-ChildItem $liteOut  -Recurse -File | Measure-Object Length -Sum).Sum/1KB)) KB"

if ($Zip) {
    Compress-Archive -Path $pluginOut -DestinationPath (Join-Path $root "dist\ReloadingToolkit.zip") -Force
    Compress-Archive -Path $liteOut   -DestinationPath (Join-Path $root "dist\ReloadingToolkit-lite.zip") -Force
    Write-Host "==> zips written to dist\"
}

if ($GrtDir -ne "") {
    $target = Join-Path $GrtDir "plugins\ReloadingToolkit"
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item $pluginOut $target -Recurse
    Write-Host "==> installed into $target"
}
