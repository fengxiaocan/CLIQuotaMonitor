$ErrorActionPreference = 'Stop'

$appSource = Join-Path $PSScriptRoot 'app'
$installRoot = Join-Path $env:LOCALAPPDATA 'CLIQuotaMonitor'
$executableName = 'CLIQuotaMonitor.App.exe'
$sourceExecutable = Join-Path $appSource $executableName
$installedExecutable = Join-Path $installRoot $executableName

if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "The application payload is missing: $sourceExecutable"
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $appSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $installRoot $_.Name) -Recurse -Force
}

$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
$shortcutPath = Join-Path $startMenuDirectory 'CLI Quota Monitor.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $installRoot
$shortcut.IconLocation = "$installedExecutable,0"
$shortcut.Description = 'CLI Quota Monitor'
$shortcut.Save()

Start-Process -FilePath $installedExecutable -WorkingDirectory $installRoot
