[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProjectRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$patchRoot = $PSScriptRoot
$sourceRoot = Join-Path $patchRoot 'Files'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path

foreach ($required in @('BicubicResizeLab', 'BicubicResizeDropTest')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Container)) {
        throw "This does not look like the FRAME source root: missing $required."
    }
}

$backupRoot = Join-Path $root ('.frame-combined-update-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backupRoot | Out-Null

Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $destination = Join-Path $root $relative
    $backup = Join-Path $backupRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        Copy-Item -LiteralPath $destination -Destination $backup
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
}

Write-Host "Applied FRAME combined update v1.0.2. Backup: $backupRoot"
if (-not $SkipBuild) {
    dotnet build (Join-Path $root 'BicubicResizeDropTest\BicubicResizeDropTest.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Restore the files from the backup folder above.' }
}
