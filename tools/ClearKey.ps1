# Jega, for testing: takes the Datafordeler key out of this PC's settings (%APPDATA%\Jega\settings.json), so Jega
# behaves as on a new PC (Load from Address offers Open Settings, Settings opens on Data access). A backup with the
# time in its name is made first; -Restore puts the newest one back. Only the key changes; the rest is kept.
#
#   powershell -ExecutionPolicy Bypass -File ClearKey.ps1            take the key out
#   powershell -ExecutionPolicy Bypass -File ClearKey.ps1 -Restore   put the newest backup back
#
# Or double-click ClearKey.cmd / RestoreKey.cmd beside it. Revit may stay open: Jega reads the file at each command.
param([switch]$Restore)

$dir = Join-Path $env:APPDATA "Jega"
$file = Join-Path $dir "settings.json"

if ($Restore) {
    # Newest by when the backup was made: Copy-Item keeps the settings' own LastWriteTime.
    $newest = Get-ChildItem $dir -Filter "settings.backup-*.json" -ErrorAction SilentlyContinue | Sort-Object CreationTime -Descending | Select-Object -First 1
    if (-not $newest) {
        Write-Host "No backup in $dir."
        exit 1
    }
    Copy-Item $newest.FullName $file -Force
    Write-Host "Put back: $($newest.Name) -> settings.json"
    exit 0
}

if (-not (Test-Path $file)) {
    Write-Host "No settings yet ($file): there is no key to take out."
    exit 0
}
$settings = Get-Content $file -Raw | ConvertFrom-Json
if ([string]::IsNullOrEmpty($settings.DatafordelerKeyProtected)) {
    Write-Host "No key in $file already."
    exit 0
}
$backup = Join-Path $dir ("settings.backup-{0}.json" -f (Get-Date -Format "yyyy-MM-dd_HHmmss"))
Copy-Item $file $backup
$settings.DatafordelerKeyProtected = ""
# UTF-8 without a BOM, as Jega writes it.
[IO.File]::WriteAllText($file, ($settings | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
Write-Host "Key taken out. Backup: $backup"
Write-Host "To put it back: RestoreKey.cmd (or ClearKey.ps1 -Restore), or enter it in Settings > Data access."
