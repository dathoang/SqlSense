<#
.SYNOPSIS
    Builds SqlSense and produces the deploy package: dist\SqlSense-<version>\ and dist\SqlSense-<version>.zip.

.DESCRIPTION
    1. (optional) sets Version in source.extension.vsixmanifest
    2. runs the engine tests (dotnet test)
    3. Rebuilds the VSIX in Release with Visual Studio's MSBuild (found with vswhere; needs the VS extension workload).
       Rebuild, not Build: an incremental build can keep the previous manifest version in the .vsix.
    4. copies SqlSense.vsix, the install scripts and LICENSE into dist, renders the user guide to .docx, and zips them.
       dist also gets SqlSense-<version>.vsix and HUONG-DAN-SU-DUNG-<version>.docx for separate download.

.PARAMETER Version
    New extension version (e.g. 1.2.0). Required to reinstall over an existing install: VSIXInstaller refuses
    the same version twice.

.PARAMETER SkipTests
    Don't run dotnet test.

.PARAMETER Install
    Run the package's Install.ps1 afterwards (SSMS must be closed).

.EXAMPLE
    .\deploy\Build-Package.ps1 -Version 1.2.0 -Install
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [switch]$SkipTests,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SqlSense.Vsix\SqlSense.Vsix.csproj'
$manifestPath = Join-Path $root 'src\SqlSense.Vsix\source.extension.vsixmanifest'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# 1. Version
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.PackageManifest.Metadata.Identity
if ($Version -and $identity.Version -ne $Version) {
    Step "Version $($identity.Version) -> $Version"
    # Text replace keeps the file's formatting; XmlDocument.Save would rewrite it.
    $text = Get-Content -LiteralPath $manifestPath -Raw
    $text = $text -replace '(<Identity [^>]*Version=")[^"]+(")', "`${1}$Version`${2}"
    [System.IO.File]::WriteAllText($manifestPath, $text, (New-Object System.Text.UTF8Encoding($true)))
}
else { $Version = $identity.Version }

# 2. Tests
if (-not $SkipTests) {
    Step 'dotnet test tests\SqlSense.Core.Tests'
    & dotnet test (Join-Path $root 'tests\SqlSense.Core.Tests') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
}

# 3. Build
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe not found: install Visual Studio 2022/2026 with the "Visual Studio extension development" workload.' }
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VSSDK `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio with the VSSDK component (workload "Visual Studio extension development") was found.' }

Step "MSBuild Rebuild Release ($msbuild)"
& $msbuild $project -restore -t:Rebuild -p:Configuration=Release -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'MSBuild failed.' }

# Same rule as Directory.Build.props: long checkout paths build under %TEMP%\ssb.
$propsDir = $root.TrimEnd('\') + '\'
$binRoot = if ($propsDir.Length -gt 100) { Join-Path ([System.IO.Path]::GetTempPath()) 'ssb\bin\SqlSense.Vsix' } else { Join-Path $root 'src\SqlSense.Vsix\bin' }
$vsix = Join-Path $binRoot 'Release\SqlSense.vsix'
if (-not (Test-Path -LiteralPath $vsix)) { throw "Build succeeded but $vsix is missing." }

# 4. Package
$name = "SqlSense-$Version"
$dist = Join-Path $root 'dist'
$out = Join-Path $dist $name
Step "Package $out"
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Copy-Item -LiteralPath $vsix -Destination $out
Copy-Item -Path (Join-Path $PSScriptRoot 'package\*') -Destination $out
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $out

# User guide: Markdown in the repo, Word in the package (deploy\tools\md-to-docx.js, needs Node.js 22+).
$tools = Join-Path $PSScriptRoot 'tools'
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js 22+ is needed to build the user guide (.docx).' }
if (-not (Test-Path -LiteralPath (Join-Path $tools 'node_modules'))) {
    Step 'npm ci (deploy\tools)'
    & npm ci --prefix $tools --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed in deploy\tools.' }
}
$guide = Join-Path $out 'HUONG-DAN-SU-DUNG.docx'
& node --no-warnings (Join-Path $tools 'md-to-docx.js') (Join-Path $PSScriptRoot 'HUONG-DAN-SU-DUNG.md') $guide $Version
if ($LASTEXITCODE -ne 0) { throw 'Building the user guide failed.' }

# Release assets next to the zip: the VSIX and the guide on their own, versioned.
$releaseVsix = Join-Path $dist "SqlSense-$Version.vsix"
$releaseGuide = Join-Path $dist "HUONG-DAN-SU-DUNG-$Version.docx"
Copy-Item -LiteralPath $vsix -Destination $releaseVsix -Force
Copy-Item -LiteralPath $guide -Destination $releaseGuide -Force
# Keep dist\SqlSense.vsix pointing at the latest build.
Copy-Item -LiteralPath $vsix -Destination (Join-Path $dist 'SqlSense.vsix') -Force

$zip = Join-Path $dist "$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip

$built = (Get-Item -LiteralPath $vsix)
Write-Host ''
Write-Host "SqlSense $Version" -ForegroundColor Green
Write-Host "  VSIX    $($built.Length.ToString('N0')) bytes"
Write-Host "  Folder  $out"
Write-Host "  Zip     $zip"
Write-Host "  Assets  $releaseVsix"
Write-Host "          $releaseGuide"

if ($Install) {
    Step 'Install'
    & (Join-Path $out 'Install.ps1')
}
