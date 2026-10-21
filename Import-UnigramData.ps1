<#
.SYNOPSIS
    Moves the accounts and settings of an installed Unigram into the fork, so that the fork can
    replace it without logging in again.

.DESCRIPTION
    Copies the source app's data folder - the TDLib databases with the sessions in them, the
    themes and wallpapers, and settings.dat with the app settings - into the fork's. Media caches
    are left behind unless -IncludeMedia is given; TDLib downloads whatever it misses again.

    Both apps have to be closed: settings.dat is a registry hive held open while an app runs, and
    a database copied mid-write is a broken one. The fork's own data, if there is any, is moved
    aside to a backup folder first, never deleted.

    A copied session is the same session, not a new one. Use only one of the two apps with it
    from then on: Telegram sees the same key from two places at once and may end the session for
    both. Do not log out in the old app either - that ends the session the fork now uses too.
    Uninstalling the old app (-RemoveSource does it once the copy succeeded) removes its copy
    without telling Telegram anything.

.EXAMPLE
    .\Import-UnigramData.ps1

.EXAMPLE
    .\Import-UnigramData.ps1 -IncludeMedia -RemoveSource
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # The app to take the data from; by default the first installed of Unigram and Unigram Preview.
    [string] $Source,

    # The fork's identity, as Telegram.Win32.csproj names it.
    [string] $Target = '38833FF26BA1D.UnigramPrimary',

    # Also copy downloaded photos, videos, documents and voice messages.
    [switch] $IncludeMedia,

    # Replace data the fork already has; it is backed up first.
    [switch] $Force,

    # Uninstall the source app once the copy succeeded.
    [switch] $RemoveSource
)

$ErrorActionPreference = 'Stop'

function Get-Package([string] $Name) {
    Get-AppxPackage -Name $Name | Select-Object -First 1
}

$candidates = if ($Source) { @($Source) } else { @('38833FF26BA1D.Unigram', '38833FF26BA1D.UnigramPreview') }
$sourcePackage = $candidates | ForEach-Object { Get-Package $_ } | Select-Object -First 1
if (-not $sourcePackage) {
    throw "No source app is installed (looked for $($candidates -join ', '))."
}

$targetPackage = Get-Package $Target
if (-not $targetPackage) {
    throw "$Target is not installed. Install the fork first (Build.Win32.ps1 -Identity Primary), then run this."
}

$sourceData = Join-Path $env:LOCALAPPDATA "Packages\$($sourcePackage.PackageFamilyName)"
$targetData = Join-Path $env:LOCALAPPDATA "Packages\$($targetPackage.PackageFamilyName)"

if (-not (Test-Path (Join-Path $sourceData 'LocalState'))) {
    throw "$($sourcePackage.Name) has no data at $sourceData."
}

# Neither may be running: matched by install folder, so that other apps named Telegram.exe - the
# test build, Telegram Desktop - are not mistaken for either.
foreach ($package in $sourcePackage, $targetPackage) {
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($package.InstallLocation, 'OrdinalIgnoreCase') }
    if ($running) {
        throw "$($package.Name) is running. Close it (including from the tray) and run this again."
    }
}

$targetLocal = Join-Path $targetData 'LocalState'
$hasAccounts = Get-ChildItem $targetLocal -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+$' -and (Test-Path (Join-Path $_.FullName 'td.binlog')) }

if ($hasAccounts -and -not $Force) {
    throw "The fork already has accounts in $targetLocal. Run with -Force to replace them; they are backed up first."
}

Write-Host "From: $($sourcePackage.Name) $($sourcePackage.Version)"
Write-Host "To:   $($targetPackage.Name) $($targetPackage.Version)"

if (-not $PSCmdlet.ShouldProcess($targetData, 'Replace the fork''s data with the source app''s')) {
    return
}

# Backup of whatever the fork had, beside the package data rather than inside it, so that
# reinstalling the fork does not take the backup with it.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $env:LOCALAPPDATA "UnigramPrimary-backup-$stamp"

foreach ($folder in 'LocalState', 'Settings') {
    $path = Join-Path $targetData $folder
    if (Test-Path $path) {
        New-Item -ItemType Directory -Force (Join-Path $backup $folder) | Out-Null
        robocopy $path (Join-Path $backup $folder) /E /MOVE /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Backing up $path failed (robocopy $LASTEXITCODE)." }
        New-Item -ItemType Directory -Force $path | Out-Null
    }
}

Write-Host "Previous data of the fork: $backup"

# Logs and crash reports belong to the old app; the media folders are TDLib's own names inside
# each account folder, and only skipped unless asked for.
$excludeDirs = @('ErrorReports', 'Reports', 'EBWebView', 'Microsoft.AppCenter.Storage', 'e2e', 'temp')
if (-not $IncludeMedia) {
    $excludeDirs += @('photos', 'videos', 'documents', 'music', 'voice', 'video_notes', 'animations')
}

$excludeFiles = @('tdlib_log.txt', 'tdlib_log.txt.old', 'app_log.txt', 'crash.id')

robocopy (Join-Path $sourceData 'LocalState') $targetLocal /E /R:1 /W:1 /NFL /NDL /NJH /NP /XD @excludeDirs /XF @excludeFiles
if ($LASTEXITCODE -ge 8) { throw "Copying the data failed (robocopy $LASTEXITCODE)." }

# settings.dat is the hive behind ApplicationData.LocalSettings; its .LOG files have to travel
# with it or the copy opens as the hive minus its last writes.
robocopy (Join-Path $sourceData 'Settings') (Join-Path $targetData 'Settings') settings.dat* /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the settings failed (robocopy $LASTEXITCODE)." }

Write-Host ''
Write-Host 'Done. Start the fork: the accounts are signed in.' -ForegroundColor Green

if ($RemoveSource) {
    Write-Host "Uninstalling $($sourcePackage.Name)..."
    Remove-AppxPackage -Package $sourcePackage.PackageFullName
} else {
    Write-Warning "Do not use $($sourcePackage.Name) with these accounts any more, and do not log out in it: the session is shared now. Uninstalling it is safe."
}

Write-Host 'Telegram links: Settings > Apps > Default apps > "tg" - pick Unigram (the fork).'
