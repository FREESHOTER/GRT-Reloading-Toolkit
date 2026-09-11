<#
  Themed monochrome plugin icons for GRT (white line art on transparent).
  GRT recolors them per color preset when the manifest sets icon_colorize="true".

  Writes <name>_16x16.png and <name>_32x32.png straight into the plugin's icon folder,
  overwriting the committed set — the names below ARE the names com.grt.plugin.xml and the
  launcher ask for. It used to write nine differently-named files (athlon_import, ladder_ocw,
  ...) into ./icons/, which .gitignore excludes and nothing reads, so regenerating the icons
  changed nothing anyone could see.

  Usage:
    ./make-icons.ps1
    ./make-icons.ps1 -OutDir ..\SomeOtherPlugin\plugin\media\icons
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\GrtReloadingToolkit\plugin\media\icons")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

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

# A loaded round drawn in the icn_seating idiom: outlined case, filled ogive. $bw and $nw are the
# case and neck half-widths; the shoulder and neck sit proportionally along the round so the
# silhouette reads as a bottlenecked rifle case rather than a lozenge. Only icn_toolkit uses this
# today, but it is the one shape here with enough geometry to be worth naming.
function CaseAndBullet($g, $brush, $k, $cx, $yBase, $yTip, $bw, $nw, $penW) {
    $p = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $span = $yBase - $yTip
    $ySh  = $yBase - $span * 0.40   # body starts tapering into the shoulder
    $yNk  = $ySh - $span * 0.12     # top of the shoulder / bottom of the neck
    $yOg  = $yNk - $span * 0.10     # where the ogive takes over

    $g.DrawLines($p, @((Pt ($cx - $bw) $yBase $k), (Pt ($cx - $bw) $ySh $k),
                       (Pt ($cx - $nw) $yNk $k),   (Pt ($cx - $nw) $yOg $k)))
    $g.DrawLines($p, @((Pt ($cx + $bw) $yBase $k), (Pt ($cx + $bw) $ySh $k),
                       (Pt ($cx + $nw) $yNk $k),   (Pt ($cx + $nw) $yOg $k)))
    $g.DrawLine($p, (Pt ($cx - $bw) $yBase $k), (Pt ($cx + $bw) $yBase $k))

    $og = New-Object System.Drawing.Drawing2D.GraphicsPath
    $og.AddBezier((Pt ($cx - $nw) $yOg $k), (Pt ($cx - $nw) ($yTip + ($yOg - $yTip) * 0.18) $k),
                  (Pt ($cx - $nw * 0.55) $yTip $k), (Pt $cx $yTip $k))
    $og.AddBezier((Pt $cx $yTip $k), (Pt ($cx + $nw * 0.55) $yTip $k),
                  (Pt ($cx + $nw) ($yTip + ($yOg - $yTip) * 0.18) $k), (Pt ($cx + $nw) $yOg $k))
    $og.CloseFigure()
    $g.FillPath($brush, $og)
    $p.Dispose()
}

# ------------------------------------------------------------------ definitions --

$icons = @{

  # Athlon import — a bullet dropping into a tray
  "icn_athlon" = {
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
  "icn_ocw" = {
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
  "icn_seating" = {
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
  "icn_cal" = {
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
  "icn_temp" = {
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
  "icn_label" = {
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
  "icn_brass" = {
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

  # Toolkit — a loaded round centred in a reticle (single entry-point icon)
  #
  # This replaced a wrench crossed with a screwdriver, which said "tools" and nothing about
  # reloading, while every icon around it names its own job. The 16px branch drops the reticle
  # ticks rather than scaling them: at that size they collide with the ring and read as noise,
  # the same reason icn_cal drops its +/- marks and icn_brass drops its caliper jaws.
  "icn_toolkit" = {
    param($g, $pen, $brush, $k, $S)
    $ringW = if ($S -le 16) { 1.9 } else { 2.4 }
    $pr = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $ringW)
    $pr.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pr.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    if ($S -le 16) {
        $g.DrawEllipse($pr, (2.5 * $k), (2.5 * $k), (27 * $k), (27 * $k))
        CaseAndBullet $g $brush $k 16 22.5 10.5 2.9 1.55 1.8
    } else {
        $g.DrawEllipse($pr, (3 * $k), (3 * $k), (26 * $k), (26 * $k))
        CaseAndBullet $g $brush $k 16 24 9 3.6 2.0 2.6
        # reticle ticks, outside the ring at N/S/E/W
        $g.DrawLine($pr, (Pt 16 0 $k),  (Pt 16 3 $k))
        $g.DrawLine($pr, (Pt 16 29 $k), (Pt 16 32 $k))
        $g.DrawLine($pr, (Pt 0 16 $k),  (Pt 3 16 $k))
        $g.DrawLine($pr, (Pt 29 16 $k), (Pt 32 16 $k))
    }
    $pr.Dispose()
  }

  # Inventory / journal — clipboard with a check and entry lines
  "icn_log" = {
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
