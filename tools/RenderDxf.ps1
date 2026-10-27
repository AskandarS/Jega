param([string]$Dxf, [string]$Png, [int]$Px = 2400, [double]$Zoom = 1.0, [string]$Fit = 'GRUND')
# Draws a Jega site plan DXF (R12 POLYLINE/VERTEX and TEXT) to a PNG, for a look without CAD. Fit: the layer whose
# extent fills the picture (times 1/Zoom), or 'ALL'. Texts are drawn a little larger than Revit draws them, and
# \U+ escapes (æøå, m²) are shown as they are written.
Add-Type -AssemblyName System.Drawing
$inv = [Globalization.CultureInfo]::InvariantCulture
$l = (Get-Content $Dxf -Raw) -split "`n"
$polys = New-Object System.Collections.ArrayList; $texts = New-Object System.Collections.ArrayList
$cur = $null; $kind = ''; $inHead = $false
for ($i = 0; $i -lt $l.Count - 1; $i += 2) {
  $c = $l[$i].Trim(); $v = $l[$i + 1].TrimEnd("`r")
  if ($c -eq '0') {
    if ($v -eq 'POLYLINE') { $cur = @{ L = ''; C = $false; P = New-Object System.Collections.ArrayList }; [void]$polys.Add($cur); $kind = 'P'; $inHead = $true }
    elseif ($v -eq 'VERTEX' -and $kind -eq 'P') { $inHead = $false; [void]$cur.P.Add([double[]]@(0, 0)) }
    elseif ($v -eq 'TEXT') { $cur = @{ L = ''; X = 0.0; Y = 0.0; AX = $null; AY = $null; H = 0.0; S = ''; J = 0; R = 0.0 }; [void]$texts.Add($cur); $kind = 'T' }
    else { $kind = ''; $cur = $null }
    continue
  }
  if ($kind -eq 'P') {
    if ($c -eq '8' -and $inHead) { $cur.L = $v }
    elseif ($c -eq '70' -and $inHead) { $cur.C = ([int]$v -band 1) -eq 1 }
    elseif ($c -eq '10' -and -not $inHead) { $cur.P[$cur.P.Count - 1][0] = [double]::Parse($v, $inv) }
    elseif ($c -eq '20' -and -not $inHead) { $cur.P[$cur.P.Count - 1][1] = [double]::Parse($v, $inv) }
  } elseif ($kind -eq 'T') {
    switch ($c) { '8' { $cur.L = $v } '10' { $cur.X = [double]::Parse($v, $inv) } '20' { $cur.Y = [double]::Parse($v, $inv) }
      '11' { $cur.AX = [double]::Parse($v, $inv) } '21' { $cur.AY = [double]::Parse($v, $inv) } '40' { $cur.H = [double]::Parse($v, $inv) }
      '1' { $cur.S = $v } '72' { $cur.J = [int]$v } '50' { $cur.R = [double]::Parse($v, $inv) } }
  }
}
$fitPts = if ($Fit -eq 'ALL') { $polys | ForEach-Object { $_.P } } else { $polys | Where-Object L -eq $Fit | ForEach-Object { $_.P } }
$xs = $fitPts | ForEach-Object { $_[0] }; $ys = $fitPts | ForEach-Object { $_[1] }
$minX = ($xs | Measure-Object -Minimum).Minimum; $maxX = ($xs | Measure-Object -Maximum).Maximum
$minY = ($ys | Measure-Object -Minimum).Minimum; $maxY = ($ys | Measure-Object -Maximum).Maximum
$cx = ($minX + $maxX) / 2; $cy = ($minY + $maxY) / 2
$span = [math]::Max($maxX - $minX, $maxY - $minY) * 1.08 / $Zoom
$k = $Px / $span
function Sx($x) { [float](($x - $cx) * $k + $Px / 2) }
function Sy($y) { [float]($Px / 2 - ($y - $cy) * $k) }
$colors = @{ GRUND = 'Red'; SKEL = 'Silver'; BYGNINGER = 'Black'; BYGNINGER_NABO = 'Gray'; VEJKANT = 'SaddleBrown'; STI = 'Peru';
  VAND = 'DodgerBlue'; SKELMAAL = 'Red'; AFSTAND_SKEL = 'Magenta'; KOTER = 'DarkOrange'; TAGKOTER = 'Blue'; ADRESSEPUNKT = 'Blue'; RAMME = 'Black'; TEKST = 'Black';
  BROENDE = 'Navy'; RISTE = 'Green'; HUSNUMRE = 'DimGray'; VEJNAVNE = 'Black'; TRAEER = 'ForestGreen'; HEGN = 'Sienna' }
$bmp = New-Object System.Drawing.Bitmap $Px, $Px
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'; $g.Clear([System.Drawing.Color]::White)
foreach ($p in $polys) {
  if ($p.P.Count -lt 2) { continue }
  $col = $colors[$p.L]; if (-not $col) { $col = 'Gray' }
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromName($col)), ($(if ($p.L -eq 'GRUND') { 3 } else { 1 }))
  $pts = [System.Drawing.PointF[]]($p.P | ForEach-Object { New-Object System.Drawing.PointF (Sx $_[0]), (Sy $_[1]) })
  if ($p.C) { $g.DrawPolygon($pen, $pts) } else { $g.DrawLines($pen, $pts) }
  $pen.Dispose()
}
foreach ($t in $texts) {
  $hpx = [float]($t.H * $k); if ($hpx -lt 2) { continue }
  $col = $colors[$t.L]; if (-not $col) { $col = 'Black' }
  $font = New-Object System.Drawing.Font 'Arial', ([float][math]::Max(1, $hpx * 1.1)), ([System.Drawing.GraphicsUnit]::Pixel)
  $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromName($col))
  $x = if ($null -ne $t.AX -and $t.J -ne 0) { $t.AX } else { $t.X }; $y = if ($null -ne $t.AY -and $t.J -ne 0) { $t.AY } else { $t.Y }
  $sz = $g.MeasureString($t.S, $font)
  $state = $g.Save(); $g.TranslateTransform((Sx $x), (Sy $y)); $g.RotateTransform([float](-$t.R))
  $dx = switch ($t.J) { 1 { -$sz.Width / 2 } 2 { -$sz.Width } default { 0 } }
  $g.DrawString($t.S, $font, $brush, [float]$dx, [float](-$hpx * 1.2))
  $g.Restore($state); $font.Dispose(); $brush.Dispose()
}
$bmp.Save($Png, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
"wrote $Png ($($polys.Count) polylines, $($texts.Count) texts)"
