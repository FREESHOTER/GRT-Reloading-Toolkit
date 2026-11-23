<#
  Publishes the plugin and assembles a drop-in GRT plugin folder.

  Usage:
    ./build-plugin.ps1                      # -> ./dist/AthlonImporter/
    ./build-plugin.ps1 -GrtDir "C:\...\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
                                            # also copies into <GrtDir>\plugins\AthlonImporter
#>
param(
    [string]$Configuration = "Release",
    [string]$GrtDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$publishDir = Join-Path $root "artifacts\publish"
$pluginOut  = Join-Path $root "dist\AthlonImporter"

Write-Host "==> dotnet publish (self-contained, single file, win-x64)"
& $dotnet publish (Join-Path $root "GrtAthlonImporter.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $publishDir | Out-Host

if (Test-Path $pluginOut) { Remove-Item -Recurse -Force $pluginOut }
New-Item -ItemType Directory -Force $pluginOut | Out-Null

Copy-Item (Join-Path $publishDir "GRT_Athlon_Importer.exe") $pluginOut
Copy-Item (Join-Path $root "plugin\com.grt.plugin.xml") $pluginOut
Copy-Item (Join-Path $root "plugin\media") $pluginOut -Recurse

Write-Host "==> plugin folder ready: $pluginOut"
Get-ChildItem -Recurse $pluginOut | ForEach-Object { "   " + $_.FullName.Substring($pluginOut.Length + 1) }

if ($GrtDir -ne "") {
    $target = Join-Path $GrtDir "plugins\AthlonImporter"
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item $pluginOut $target -Recurse
    Write-Host "==> installed into $target"
}
