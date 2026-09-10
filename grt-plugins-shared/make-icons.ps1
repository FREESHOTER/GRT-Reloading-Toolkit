<#
  Themed monochrome plugin icons for GRT (white line art on transparent).
  GRT recolors them per color preset when the manifest sets icon_colorize="true".
  Output: ./icons/<name>_16x16.png and ./icons/<name>_32x32.png
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "icons"
New-Item -ItemType Directory -Force $outDir | Out-Null

function Draw([int]$S, [scriptblock]$Body) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $S / 32.0
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ($(if ($S -le 16) { 1.9 } else { 2.6 })))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    & $Body $g $pen $brush $k $S
    $g.Dispose()
    $bmp
}
function Pt($x, $y, $k) { New-Object System.Drawing.PointF(($x * $k), ($y * $k)) }

# ------------------------------------------------------------------ definitions --

$icons = @{

  # Athlon import — a bullet dropping into a tray
  "athlon_import" = {
    param($g, $pen, $brush, $k, $S)
    # tray (open top)
    $g.DrawLines($pen, @((Pt 7 19 $k), (Pt 7 27 $k), (Pt 25 27 $k), (Pt 25 19 $k)))
    if ($S -le 16) {
        # chunky down-triangle
        $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
        $tri.AddLines(@((Pt 10 9 $k), (Pt 22 9 $k), (Pt 16 20 $k)))
        $g.FillPath($brush, $tri)
    } else {
        # bullet: ogive nose down + short body
        $b = New-Object System.Drawing.Drawing2D.GraphicsPath
        $b.AddBezier((Pt 12 8 $k), (Pt 12 15 $k), (Pt 14 20 $k), (Pt 16 22 $k))
        $b.AddBezier((Pt 16 22 $k), (Pt 18 20 $k), (Pt 20 15 $k), (Pt 20 8 $k))
        $b.CloseFigure()
        $g.FillPath($brush, $b)
        $g.DrawLine($pen, (Pt 12 8 $k), (Pt 20 8 $k))
        # motion ticks
        $g.DrawLine($pen, (Pt 16 3 $k), (Pt 16 5 $k))
    }
  }

  # Ladder / OCW — ascending bars, one node ringed
  "ladder_ocw" = {
    param($g, $pen, $brush, $k, $S)
    $base = 27
    $g.DrawLine($pen, (Pt 4 $base $k), (Pt 28 $base $k))
    if ($S -le 16) {
        foreach ($b in @(@(9, 21), @(16, 15), @(23, 9))) { $g.DrawLine($pen, (Pt $b[0] $base $k), (Pt $b[0] $b[1] $k)) }
        $r = 2.6 * $k
        $g.FillEllipse($brush, ((16 * $k) - $r), ((15 * $k) - $r), (2 * $r), (2 * $r))
    } else {
        foreach ($b in @(@(6, 22), @(12, 18), @(18, 13), @(24, 8))) { $g.DrawLine($pen, (Pt $b[0] $base $k), (Pt $b[0] $b[1] $k)) }
        $r = 5.5 * $k
        $g.DrawEllipse($pen, ((18 * $k) - $r), ((13 * $k) - $r), (2 * $r), (2 * $r))
    }
  }

  # Seating depth — bullet seated in a case, with a depth double-arrow
  "seating_depth" = {
    param($g, $pen, $brush, $k, $S)
    # case: open-top U
    $g.DrawLines($pen, @((Pt 8 8 $k), (Pt 8 27 $k), (Pt 19 27 $k), (Pt 19 8 $k)))
    # bullet ogive, base seated below the case mouth
    $b = New-Object System.Drawing.Drawing2D.GraphicsPath
    $b.AddBezier((Pt 9 14 $k), (Pt 9 6 $k), (Pt 18 6 $k), (Pt 18 14 $k))
    $b.AddLine((Pt 18 14 $k), (Pt 9 14 $k))
    $b.CloseFigure()
    $g.FillPath($brush, $b)
    if ($S -gt 16) {
        # depth double-arrow from case mouth (y=8) to bullet base (y=14)
        $ax = 24
        $g.DrawLine($pen, (Pt $ax 8 $k), (Pt $ax 14 $k))
        $g.DrawLines($pen, @((Pt ($ax - 2.5) 10 $k), (Pt $ax 8 $k), (Pt ($ax + 2.5) 10 $k)))
        $g.DrawLines($pen, @((Pt ($ax - 2.5) 12 $k), (Pt $ax 14 $k), (Pt ($ax + 2.5) 12 $k)))
        $g.DrawLine($pen, (Pt 19 8 $k), (Pt 27 8 $k))
        $g.DrawLine($pen, (Pt 18 14 $k), (Pt 27 14 $k))
    }
  }

  # Barrel calibration — gauge arc + needle + adjust tick
  "barrel_cal" = {
    param($g, $pen, $brush, $k, $S)
    # gauge arc (open bottom)
    $g.DrawArc($pen, (5 * $k), (7 * $k), (22 * $k), (22 * $k), 160, 220)
    # needle
    $g.DrawLine($pen, (Pt 16 18 $k), (Pt 22 10 $k))
    $g.FillEllipse($brush, ((16 * $k) - 1.6 * $k), ((18 * $k) - 1.6 * $k), (3.2 * $k), (3.2 * $k))
    if ($S -gt 16) {
        # small +/- adjust marks under the gauge
        $g.DrawLine($pen, (Pt 10 26 $k), (Pt 13 26 $k))
        $g.DrawLine($pen, (Pt 19 26 $k), (Pt 22 26 $k))
        $g.DrawLine($pen, (Pt 20.5 24.5 $k), (Pt 20.5 27.5 $k))
    }
  }

  # Powder temp coefficient — thermometer
  "temp_coeff" = {
    param($g, $pen, $brush, $k, $S)
    # bulb
    $g.FillEllipse($brush, (12 * $k), (21 * $k), (8 * $k), (8 * $k))
    $g.DrawEllipse($pen, (12 * $k), (21 * $k), (8 * $k), (8 * $k))
    # stem
    $g.DrawLines($pen, @((Pt 14 22 $k), (Pt 14 7 $k)))
    $g.DrawLines($pen, @((Pt 18 22 $k), (Pt 18 7 $k)))
    $g.DrawArc($pen, (14 * $k), (4 * $k), (4 * $k), (6 * $k), 180, 180)
    # mercury column
    $g.DrawLine((New-Object System.Drawing.Pen([System.Drawing.Color]::White, (2.6))), (Pt 16 25 $k), (Pt 16 13 $k))
    if ($S -gt 16) {
        foreach ($y in 10, 14, 18) { $g.DrawLine($pen, (Pt 19 $y $k), (Pt 22 $y $k)) }
    }
  }

  # Load card / label — a tag with a hole and lines + a small QR square
  "load_label" = {
    param($g, $pen, $brush, $k, $S)
    # tag outline (pointed left)
    $tag = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tag.AddLines(@((Pt 12 7 $k), (Pt 27 7 $k), (Pt 27 25 $k), (Pt 12 25 $k), (Pt 5 16 $k)))
    $tag.CloseFigure()
    $g.DrawPath($pen, $tag)
    # hole
    $g.DrawEllipse($pen, (10 * $k), (14.5 * $k), (3 * $k), (3 * $k))
    if ($S -gt 16) {
        # text lines
        $g.DrawLine($pen, (Pt 15 12 $k), (Pt 24 12 $k))
        $g.DrawLine($pen, (Pt 15 16 $k), (Pt 24 16 $k))
        # tiny QR
        $g.DrawRectangle($pen, (20 * $k), (19 * $k), (4.5 * $k), (4.5 * $k))
        $g.FillRectangle($brush, (21 * $k), (20 * $k), (1.3 * $k), (1.3 * $k))
        $g.FillRectangle($brush, (23 * $k), (22 * $k), (1.3 * $k), (1.3 * $k))
    } else {
        $g.DrawLine($pen, (Pt 15 20 $k), (Pt 23 20 $k))
    }
  }

  # Brass prep — case mouth + caliper jaws
  "brass_prep" = {
    param($g, $pen, $brush, $k, $S)
    # case (open top, tapered neck)
    $g.DrawLines($pen, @((Pt 9 27 $k), (Pt 9 15 $k), (Pt 12 10 $k), (Pt 12 6 $k)))
    $g.DrawLines($pen, @((Pt 21 27 $k), (Pt 21 15 $k), (Pt 18 10 $k), (Pt 18 6 $k)))
    $g.DrawLine($pen, (Pt 9 27 $k), (Pt 21 27 $k))
    if ($S -gt 16) {
        # caliper jaws around the neck
        $g.DrawLine($pen, (Pt 6 6 $k), (Pt 6 12 $k))
        $g.DrawLine($pen, (Pt 6 8 $k), (Pt 12 8 $k))
        $g.DrawLine($pen, (Pt 24 6 $k), (Pt 24 12 $k))
        $g.DrawLine($pen, (Pt 24 8 $k), (Pt 18 8 $k))
    }
  }

  # Toolkit — a wrench crossed with a screwdriver (single entry-point icon)
  "toolkit" = {
    param($g, $pen, $brush, $k, $S)
    $thick = if ($S -le 16) { 2.4 } else { 3.4 }
    $p2 = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $thick)
    $p2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p2.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    # screwdriver (top-left to bottom-right)
    $g.DrawLine($p2, (Pt 7 7 $k), (Pt 22 22 $k))
    $g.DrawLine($p2, (Pt 5 5 $k), (Pt 8 8 $k))          # handle stub
    # wrench (bottom-left to top-right) with an open jaw
    $g.DrawLine($p2, (Pt 8 24 $k), (Pt 20 12 $k))
    $jaw = New-Object System.Drawing.Drawing2D.GraphicsPath
    $jaw.AddArc((18 * $k), (5 * $k), (9 * $k), (9 * $k), 110, 230)
    $g.DrawPath($p2, $jaw)
    $p2.Dispose()
  }

  # Inventory / journal — clipboard with a check and entry lines
  "inventory_log" = {
    param($g, $pen, $brush, $k, $S)
    $g.DrawRectangle($pen, (6 * $k), (6 * $k), (20 * $k), (23 * $k))
    $g.DrawRectangle($pen, (12 * $k), (3 * $k), (8 * $k), (5 * $k))
    if ($S -le 16) {
        $g.DrawLines($pen, @((Pt 10 17 $k), (Pt 13 20 $k), (Pt 18 13 $k)))
        $g.DrawLine($pen, (Pt 10 24 $k), (Pt 22 24 $k))
    } else {
        $g.DrawLines($pen, @((Pt 9 15 $k), (Pt 12 18 $k), (Pt 16 12 $k)))
        $g.DrawLine($pen, (Pt 18 15 $k), (Pt 23 15 $k))
        $g.DrawLine($pen, (Pt 10 21 $k), (Pt 23 21 $k))
        $g.DrawLine($pen, (Pt 10 25 $k), (Pt 23 25 $k))
    }
  }
}

foreach ($name in $icons.Keys) {
    foreach ($size in 16, 32) {
        $bmp = Draw $size $icons[$name]
        $bmp.Save((Join-Path $outDir ("{0}_{1}x{1}.png" -f $name, $size)), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    Write-Host "  $name  (16 + 32)"
}
Write-Host "done -> $outDir"
