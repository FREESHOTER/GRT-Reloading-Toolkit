<#
  Publishes the plugin and assembles drop-in GRT plugin folders.

    ./dist/ReloadingToolkit/       self-contained (~64 MB exe) - no runtime needed, host it
    ./dist/ReloadingToolkit-lite/  framework-dependent (~1.5 MB zip) - needs .NET 8 Desktop Runtime

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

# A `dotnet.exe` with no SDK registered (a bare host - e.g. a 32-bit stub left behind by some
# other product's install, sitting on PATH ahead of the real one) loads fine as a command but
# can't run `publish`. Take the first PATH match that actually reports an SDK, not just the
# first one PATH happens to list first.
function HasSdk($path) {
    try { return [bool](& $path --list-sdks 2>$null) } catch { return $false }
}
# dotnet off PATH: the hardcoded "C:\Program Files\dotnet\dotnet.exe" broke every install that
# isn't the default x64 machine-wide one - winget, per-user, ARM64, side-by-side.
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
          Select-Object -ExpandProperty Source -Unique |
          Where-Object { HasSdk $_ } | Select-Object -First 1
if (-not $dotnet) {
    $dotnet = @("$env:ProgramFiles\dotnet\dotnet.exe",
                "${env:ProgramFiles(x86)}\dotnet\dotnet.exe",
                "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") |
              Where-Object { $_ -and (Test-Path $_) -and (HasSdk $_) } | Select-Object -First 1
}
if (-not $dotnet) { throw "no dotnet install with an SDK found on PATH or in the usual install locations. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" }
$csproj = Join-Path $root "GrtReloadingToolkit.csproj"

# The version lives in Directory.Build.props and nowhere else: the assemblies get it from the
# compiler, the window titles read it back off the assembly, and the shipped manifest is stamped
# with it below. GRT reads com.grt.plugin.xml, so an unstamped copy is how a 0.1.5 build ends up
# announcing itself as 0.1.0.
$propsPath = Join-Path $root "..\Directory.Build.props"
$version = ([xml](Get-Content $propsPath -Raw -Encoding UTF8)).SelectSingleNode("/Project/PropertyGroup/Version").InnerText.Trim()
if (-not $version) { throw "no <Version> in $propsPath" }
Write-Host "==> version $version"

# Rewrites the manifest's version attribute in place. Line-anchored so it can't hit the
# `<?xml version="1.0"?>` declaration, and the result is re-parsed and checked, because a
# silently unstamped manifest is exactly the drift this is here to stop.
#
# Every read and write below names its encoding. The manifest is BOM-less UTF-8 holding one
# non-ASCII character - the ellipsis in the menu label - and Windows PowerShell 5.1 reads a
# BOM-less file as the system ANSI code page, so a bare Get-Content mangles that label and
# writes the mojibake straight into the shipped plugin. Set-Content -Encoding UTF8 is no good
# either: on 5.1 it prepends a BOM the source does not have, so the same script would ship a
# different manifest on Windows than on pwsh. WriteAllText with an explicit BOM-less
# UTF8Encoding is the one spelling that means the same thing on both.
function Stamp($manifest) {
    # .NET resolves relative paths against the process working directory, not PowerShell's.
    $manifest = (Resolve-Path $manifest).Path
    $txt = [regex]::Replace((Get-Content $manifest -Raw -Encoding UTF8), '(?m)^(\s*version\s*=\s*")[^"]*(")', "`${1}$version`${2}")
    [System.IO.File]::WriteAllText($manifest, $txt, (New-Object System.Text.UTF8Encoding $false))
    $got = ([xml](Get-Content $manifest -Raw -Encoding UTF8)).SelectSingleNode("/GordonsReloadingTool/plugin").GetAttribute("version")
    if ($got -ne $version) { throw "manifest version is '$got', expected '$version' - check the version attribute in plugin\com.grt.plugin.xml" }
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

# Zips $srcDir as a single top-level folder named after it, which is the layout GRT expects when
# the zip is unpacked into its plugins folder.
#
# Compress-Archive is the obvious call and is what this used to be, but on Windows PowerShell 5.1
# it writes the entry paths with backslashes. The ZIP spec requires "/" (APPNOTE 4.4.17.1), and
# while Windows Explorer and Expand-Archive forgive it, Python's zipfile, macOS Archive Utility
# and Info-ZIP do not - they read "ReloadingToolkit\com.grt.plugin.xml" as one flat filename and
# unpack a heap of oddly named files instead of a folder. pwsh writes "/", so the same script
# produced two different artifacts depending on who ran it. Naming the separator fixes that.
function WriteZip($srcDir, $zipPath) {
    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }
    $srcDir = (Resolve-Path $srcDir).Path.TrimEnd('\', '/')
    $top = Split-Path $srcDir -Leaf
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($f in (Get-ChildItem $srcDir -Recurse -File)) {
            $entry = "$top/" + $f.FullName.Substring($srcDir.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $f.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
}

# ---- 1. self-contained single file ----
Write-Host "==> publish: self-contained single file"
$scDir = Join-Path $root "artifacts\publish-sc"
& $dotnet publish $csproj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $scDir | Out-Host
# `& dotnet.exe` is a native command: $ErrorActionPreference = "Stop" does not see its exit code,
# only PowerShell's own terminating errors - a failed publish would otherwise fall through to
# Assemble/WriteZip below, which happily re-package whatever is already sitting in $scDir/dist\
# from a previous run and report success sizes for a build that never happened.
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (self-contained) failed with exit code $LASTEXITCODE" }
$pluginOut = Join-Path $root "dist\ReloadingToolkit"
# self-contained publish dir also holds the plugin\ copy from csproj <None> - Assemble strips it
Assemble $pluginOut $scDir
Get-ChildItem $pluginOut -File | Where-Object { $_.Name -notin @('GRT_Reloading_Toolkit.exe','com.grt.plugin.xml','README.md','MANUAL.md') } | Remove-Item -Force

# ---- 2. framework-dependent (win-x64, needs .NET 8 Desktop Runtime) ----
Write-Host "==> publish: framework-dependent (lite)"
$fdDir = Join-Path $root "artifacts\publish-fd"
& $dotnet publish $csproj -c $Configuration -p:RuntimeIdentifier=win-x64 -p:SelfContained=false `
    -p:DebugType=none -p:SatelliteResourceLanguages=en -o $fdDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (framework-dependent) failed with exit code $LASTEXITCODE" }
$liteOut = Join-Path $root "dist\ReloadingToolkit-lite"
Assemble $liteOut $fdDir

Write-Host "==> dist\ReloadingToolkit      $('{0:N0}' -f ((Get-ChildItem $pluginOut -Recurse -File | Measure-Object Length -Sum).Sum/1KB)) KB"
Write-Host "==> dist\ReloadingToolkit-lite $('{0:N0}' -f ((Get-ChildItem $liteOut  -Recurse -File | Measure-Object Length -Sum).Sum/1KB)) KB"

if ($Zip) {
    WriteZip $pluginOut (Join-Path $root "dist\ReloadingToolkit.zip")
    WriteZip $liteOut   (Join-Path $root "dist\ReloadingToolkit-lite.zip")
    Write-Host "==> zips written to dist\"
}

if ($GrtDir -ne "") {
    $target = Join-Path $GrtDir "plugins\ReloadingToolkit"
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item $pluginOut $target -Recurse
    Write-Host "==> installed into $target"
}
