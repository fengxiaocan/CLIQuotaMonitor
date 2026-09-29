$ErrorActionPreference = 'Stop'

$appSource = Join-Path $PSScriptRoot 'app'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\CLIQuotaMonitor'
$executableName = 'CLIQuotaMonitor.App.exe'
$sourceExecutable = Join-Path $appSource $executableName
$installedExecutable = Join-Path $installRoot $executableName

if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "The application payload is missing: $sourceExecutable"
}

# Stop running process if already running
Get-Process -Name 'CLIQuotaMonitor.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $appSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $installRoot $_.Name) -Recurse -Force
}

$shell = New-Object -ComObject WScript.Shell

# Start Menu Shortcut
$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
$startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory 'CLI Quota Monitor.lnk'))
$startMenuShortcut.TargetPath = $installedExecutable
$startMenuShortcut.WorkingDirectory = $installRoot
$startMenuShortcut.IconLocation = "$installedExecutable,0"
$startMenuShortcut.Description = 'CLI Quota Monitor'
$startMenuShortcut.Save()

# Desktop Shortcut
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
if (Test-Path -LiteralPath $desktopDirectory) {
    $desktopShortcut = $shell.CreateShortcut((Join-Path $desktopDirectory 'CLI Quota Monitor.lnk'))
    $desktopShortcut.TargetPath = $installedExecutable
    $desktopShortcut.WorkingDirectory = $installRoot
    $desktopShortcut.IconLocation = "$installedExecutable,0"
    $desktopShortcut.Description = 'CLI Quota Monitor'
    $desktopShortcut.Save()
}

Start-Process -FilePath $installedExecutable -WorkingDirectory $installRoot
