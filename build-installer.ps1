<#
.SYNOPSIS
    Compiles and packages the CLI Quota Monitor application into an executable installer (.exe).

.DESCRIPTION
    This script publishes the application in Release mode (win-x64, self-contained by default)
    and packages it into an installer .exe using Inno Setup (preferred) or 7-Zip SFX (fallback).

.PARAMETER Configuration
    Build configuration. Default is 'Release'.

.PARAMETER FrameworkDependent
    If specified, creates a framework-dependent build (requires .NET 8 Desktop Runtime installed).
    By default, creates a self-contained build that requires no external runtime.

.PARAMETER OutputDirectory
    The directory where the final installer .exe will be saved. Default is 'dist'.

.PARAMETER InstallerType
    Packaging engine to use: 'Auto' (prefer Inno Setup, fallback to 7-Zip), 'InnoSetup', or '7z'.

.EXAMPLE
    .\build-installer.ps1
    Builds a standalone, self-contained installer .exe in dist\

.EXAMPLE
    .\build-installer.ps1 -FrameworkDependent
    Builds a lightweight installer (.NET runtime not bundled).
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$FrameworkDependent,
    [string]$OutputDirectory = 'dist',
    [ValidateSet('Auto', 'InnoSetup', '7z')]
    [string]$InstallerType = 'Auto'
)

$ErrorActionPreference = 'Stop'
$rootDir = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       CLI Quota Monitor - Installer Package Builder      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Paths
$appProj = Join-Path $rootDir "src\CLIQuotaMonitor.App\CLIQuotaMonitor.App.csproj"
$publishDir = Join-Path $rootDir "bin\publish\app"
$distDir = Join-Path $rootDir $OutputDirectory
$packagingDir = Join-Path $rootDir "packaging"
$issPath = Join-Path $packagingDir "setup.iss"
$appIco = Join-Path $packagingDir "app.ico"

# 2. Ensure packaging app.ico exists
if (-not (Test-Path -LiteralPath $appIco)) {
    $srcIco = Join-Path $rootDir "src\CLIQuotaMonitor.App\app.ico"
    if (Test-Path -LiteralPath $srcIco) {
        Copy-Item -LiteralPath $srcIco -Destination $appIco -Force
    }
}

# 3. Publish application
$selfContained = -not $FrameworkDependent.IsPresent
$modeDesc = if ($selfContained) { "Self-Contained (All runtimes included)" } else { "Framework-Dependent" }
Write-Host "`n[1/3] Publishing application [$modeDesc]..." -ForegroundColor Yellow

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArgs = @(
    "publish",
    $appProj,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", ($selfContained.ToString().ToLowerInvariant()),
    "-p:PublishSingleFile=false",
    "-o", $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Remove .pdb debug files to reduce installer size
Get-ChildItem -LiteralPath $publishDir -Filter "*.pdb" -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Publish completed successfully." -ForegroundColor Green

# 4. Detect Tools
function Find-InnoSetupCompiler {
    $iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($iscc) { return $iscc.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path "${env:ProgramFiles(x86)}" "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path "${env:ProgramFiles(x86)}" "Inno Setup 5\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 5\ISCC.exe")
    )

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }
    return $null
}

function Find-7Zip {
    $cmd = Get-Command "7z.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles "7-Zip\7z.exe"),
        (Join-Path "${env:ProgramFiles(x86)}" "7-Zip\7z.exe")
    )

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }
    return $null
}

$isccPath = Find-InnoSetupCompiler
$sevenZipPath = Find-7Zip

$selectedType = $InstallerType
if ($selectedType -eq 'Auto') {
    if ($isccPath) {
        $selectedType = 'InnoSetup'
    } elseif ($sevenZipPath) {
        $selectedType = '7z'
    } else {
        throw "Neither Inno Setup (ISCC.exe) nor 7-Zip (7z.exe) was found on this system. Please install Inno Setup 6."
    }
}

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$finalInstallerPath = $null

# 5. Build Installer
Write-Host "`n[2/3] Building installer package with $selectedType..." -ForegroundColor Yellow

if ($selectedType -eq 'InnoSetup') {
    if (-not $isccPath) {
        throw "Inno Setup compiler (ISCC.exe) was not found."
    }

    Write-Host "Using Inno Setup: $isccPath" -ForegroundColor DarkGray
    & $isccPath "/O$distDir" $issPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }

    $finalInstallerPath = Get-ChildItem -LiteralPath $distDir -Filter "CLIQuotaMonitor-Setup-*.exe" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
elseif ($selectedType -eq '7z') {
    if (-not $sevenZipPath) {
        throw "7-Zip (7z.exe) was not found."
    }

    $sevenZipDir = Split-Path -Parent $sevenZipPath
    $sfxModule = Join-Path $sevenZipDir "7z.sfx"
    if (-not (Test-Path -LiteralPath $sfxModule -PathType Leaf)) {
        throw "7z.sfx module was not found in $sevenZipDir"
    }

    $configTxt = Join-Path $packagingDir "7z-installer-config.txt"
    $installPs1 = Join-Path $packagingDir "Install.ps1"
    $tempArchive = Join-Path $distDir "payload.7z"
    $outputExe = Join-Path $distDir "CLIQuotaMonitor-Setup-7z.exe"

    if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive -Force }
    if (Test-Path -LiteralPath $outputExe) { Remove-Item -LiteralPath $outputExe -Force }

    # Pack payload
    Write-Host "Creating 7z payload archive..." -ForegroundColor DarkGray
    & $sevenZipPath a -t7z -mx=9 "$tempArchive" "$publishDir\*" "$installPs1" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7z compression failed with exit code $LASTEXITCODE"
    }

    # Concatenate SFX + Config + 7z
    Write-Host "Combining SFX module..." -ForegroundColor DarkGray
    $sfxBytes = [System.IO.File]::ReadAllBytes($sfxModule)
    $configBytes = [System.IO.File]::ReadAllBytes($configTxt)
    $archiveBytes = [System.IO.File]::ReadAllBytes($tempArchive)

    $outStream = [System.IO.File]::Create($outputExe)
    $outStream.Write($sfxBytes, 0, $sfxBytes.Length)
    $outStream.Write($configBytes, 0, $configBytes.Length)
    $outStream.Write($archiveBytes, 0, $archiveBytes.Length)
    $outStream.Dispose()

    Remove-Item -LiteralPath $tempArchive -Force -ErrorAction SilentlyContinue
    $finalInstallerPath = $outputExe
}

# 6. Verification & Summary
Write-Host "`n[3/3] Verifying generated installer..." -ForegroundColor Yellow
if (-not $finalInstallerPath -or -not (Test-Path -LiteralPath $finalInstallerPath -PathType Leaf)) {
    throw "Installer binary was not found after packaging."
}

$fileInfo = Get-Item -LiteralPath $finalInstallerPath
$sizeMb = [Math]::Round($fileInfo.Length / 1MB, 2)
$hash = (Get-FileHash -LiteralPath $finalInstallerPath -Algorithm SHA256).Hash

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "                  BUILD SUCCESSFUL!                       " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Installer File : $($fileInfo.FullName)" -ForegroundColor White
Write-Host "Installer Size : $sizeMb MB ($($fileInfo.Length) bytes)" -ForegroundColor White
Write-Host "SHA256 Hash    : $hash" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
