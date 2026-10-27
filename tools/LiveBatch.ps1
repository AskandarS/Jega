param([string]$Out = (Join-Path $env:TEMP 'jega-live-batch'), [string]$Only = '', [switch]$Eaves, [string]$Setback = '',
  [string]$List = (Join-Path $PSScriptRoot 'live-addresses.txt'))
# The live test batch: every address in $List (tools/live-addresses.txt; another list, e.g. candidates for a test case,
# with -List) fetched as Load from Address does (LiveTests,
# Any_address_to_files), its DXF and CSV kept in $Out\<name>\, the site plan drawn to plan.png (RenderDxf.ps1), and a
# line per address: the scale, the buildings on the lot and the roof levels shown. Needs JEGA_DATAFORDELER_KEY (never
# in the code). One address at a time, a few seconds apart: the WFS answers 500 to many calls in a row. $Only: one name
# from the list. $Eaves: the roofs as ridge over eaves. $Setback: the setback line's distance, m (e.g. 3).
$root = Split-Path $PSScriptRoot -Parent
if (-not $env:JEGA_DATAFORDELER_KEY) { throw 'Set JEGA_DATAFORDELER_KEY to a Datafordeler key first.' }
$env:JEGA_LIVE_EAVES = if ($Eaves) { '1' } else { '' }
$env:JEGA_LIVE_SETBACK = $Setback
$lines = Get-Content $List -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith('#') }
foreach ($line in $lines) {
  $name, $address, $what = $line -split '\|' | ForEach-Object { $_.Trim() }
  if ($Only -and $name -ne $Only) { continue }
  $dir = Join-Path $Out $name
  New-Item -ItemType Directory -Force $dir | Out-Null
  $env:JEGA_LIVE_ADDRESS = $address; $env:JEGA_LIVE_OUT = $dir
  $result = dotnet test (Join-Path $root 'tests/Jega.Core.Tests') --filter Any_address_to_files -nologo -v q 2>$null | Select-String 'Passed!|Failed!'
  $dxfPath = Join-Path $dir 'jega-live-siteplan.dxf'
  if ($result -match 'Passed!' -and (Test-Path $dxfPath)) {
    $dxf = Get-Content $dxfPath -Raw
    $scale = [regex]::Match($dxf, '1:(\d+) \(A3\)').Groups[1].Value
    $buildings = ([regex]::Matches($dxf, "0\nPOLYLINE\n8\nBYGNINGER\n")).Count
    # Less the legend's sample (one text, or two with the eaves).
    $roofs = ([regex]::Matches($dxf, "0\nTEXT\n8\nTAGKOTER\n")).Count - $(if ($Eaves) { 2 } else { 1 })
    & (Join-Path $PSScriptRoot 'RenderDxf.ps1') -Dxf $dxfPath -Png (Join-Path $dir 'plan.png') -Px 1600 -Fit RAMME | Out-Null
    "{0,-13} 1:{1,-5} buildings on the lot {2,3}, roof levels {3,3}  ({4})" -f $name, $scale, $buildings, [math]::Max(0, $roofs), $what
  } else {
    "{0,-13} FAILED  ({1})" -f $name, $address
  }
  Start-Sleep -Seconds 3
}
"Pictures: $Out\<name>\plan.png"
