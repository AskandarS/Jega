param([switch]$NoInstaller, [string]$Dist = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'))
# Jega's release packaging: a Release build staged as the Autodesk bundle, checked, and packed two ways: the installer
# (tools\Jega.iss, Inno Setup 6) and a zip of the bundle for whoever installs by hand (unzip into
# %APPDATA%\Autodesk\ApplicationPlugins\). Output in $Dist (the repository's dist\, git-ignored):
#
#   powershell -ExecutionPolicy Bypass -File tools\Pack.ps1                 build, stage, zip, installer
#   powershell -ExecutionPolicy Bypass -File tools\Pack.ps1 -NoInstaller    build, stage, zip only
#
# Needs the .NET 8 SDK and Revit 2025 (or -p:RevitDir, see Jega.Revit.csproj); the installer needs Inno Setup 6
# (winget install JRSoftware.InnoSetup). The version comes from Directory.Build.props, the one place it is set.
# The staging uses the build's own deploy target with the bundle folder pointed here, so the bundle is laid out exactly
# as -p:DeployToRevit=true lays it out in Revit; that target also removes an old-style deploy from this PC's
# %APPDATA%\Autodesk\Revit\Addins\2025, which is fine on a developer's PC.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# The version, e.g. 0.9.0-beta.1, and its four-number form for the installer's file version, e.g. 0.9.0.0.
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version.Trim()
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
$numVersion = (($version -replace '-.*$', '').Split('.') + @('0', '0', '0', '0'))[0..3] -join '.'

$stage = Join-Path $Dist 'stage'
$bundle = Join-Path $stage 'Jega.bundle'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host "Jega ${version}: building Release and staging the bundle in $bundle"
dotnet build (Join-Path $root 'src\Jega.Revit') -c Release -nologo -v q -p:DeployToRevit=true "-p:BundleDir=$bundle"
if ($LASTEXITCODE -ne 0) { throw "The build failed ($LASTEXITCODE)." }

# What the bundle must hold, and must not.
$must = @('PackageContents.xml', 'LICENSE', 'NOTICE', 'DATA.md', 'Contents\Jega.dll', 'Contents\Jega.Core.dll', 'Contents\Jega.deps.json',
  'Contents\Jega.runtimeconfig.json', 'Contents\da\Jega.resources.dll', 'Contents\da\Jega.Core.resources.dll',
  'Contents\2025\Jega.addin', 'Contents\2026\Jega.addin')
foreach ($m in $must) {
  if (-not (Test-Path (Join-Path $bundle $m))) { throw "Missing from the bundle: $m" }
}
$pdbs = Get-ChildItem $bundle -Recurse -Filter *.pdb
if ($pdbs) { throw "PDBs in the bundle: $($pdbs.Name -join ', ')" }
# A file Revit held while a deploy ran is renamed with the PC's name (Jega-<PC>.dll, Jega.Core-<PC>.dll,
# Jega.deps-<PC>.json …); none of those may ship. No file of the bundle's own has a hyphen in its name.
$stray = Get-ChildItem $bundle -Recurse -File | Where-Object { $_.Name -match '-' }
if ($stray) { throw "Stray files in the bundle (an old deploy's?): $($stray.Name -join ', ')" }

# No builder's folder in anything shipped (Release builds map the source to /_/; this checks it held). The profile
# folder, not the bare user name: a short name like "sa" is in ordinary words ("necessary" in the GPL).
$profileDir = $env:USERPROFILE
$needles = @($profileDir, $profileDir.Replace('\', '/'), "\$env:USERNAME\", "/$env:USERNAME/", 'OneDrive')
foreach ($f in Get-ChildItem $bundle -Recurse -File) {
  $bytes = [IO.File]::ReadAllBytes($f.FullName)
  $ascii = [Text.Encoding]::ASCII.GetString($bytes)
  $utf16 = [Text.Encoding]::Unicode.GetString($bytes)
  $hit = $needles | Where-Object { $ascii.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
    $utf16.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
  if ($hit) {
    throw "The user's name or folder is in $($f.FullName). Build as Release, and check PathMap in Directory.Build.props."
  }
}
Write-Host "Bundle checked: $((Get-ChildItem $bundle -Recurse -File).Count) files, no PDBs, no user folder."

# The zip: Jega.bundle\ as its root, so it unzips straight into ApplicationPlugins.
New-Item -ItemType Directory -Force $Dist | Out-Null
$zip = Join-Path $Dist "Jega-$version-bundle.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $bundle -DestinationPath $zip
Write-Host "Zip: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"

if ($NoInstaller) { exit 0 }

# The installer. Inno Setup 6 installs per machine or per user; look in both places, then on the path.
$iscc = @(
  (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
  (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) {
  Write-Host 'Inno Setup 6 not found: no installer made. Install it with  winget install JRSoftware.InnoSetup  and run again, or use -NoInstaller.'
  exit 2
}
& $iscc /Q "/DVersion=$version" "/DNumVersion=$numVersion" "/DStage=$stage" "/O$Dist" (Join-Path $PSScriptRoot 'Jega.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
$setup = Join-Path $Dist "Jega-$version-setup.exe"
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1KB)) KB)"
