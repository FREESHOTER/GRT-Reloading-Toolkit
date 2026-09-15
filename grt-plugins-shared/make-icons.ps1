<#
  Themed monochrome plugin icons for GRT (white line art on transparent).
  GRT recolors them per color preset when the manifest sets icon_colorize="true".

  Writes <name>_16x16.png and <name>_32x32.png straight into the plugin's icon folder,
  overwriting the committed set - the names below ARE the names com.grt.plugin.xml and the
  launcher ask for. It used to write nine differently-named files (athlon_import, ladder_ocw,
  ...) into ./icons/, which .gitignore excludes and nothing reads, so regenerating the icons
  changed nothing anyone could see.

  icn_toolkit is the exception: it is the only icon GRT itself renders (com.grt.plugin.xml names
  it and nothing else), and it ships in colour with icon_colorize="false". See $Steel below.

  Usage:
    ./make-icons.ps1
    ./make-icons.ps1 -OutDir ..\SomeOtherPlugin\plugin\media\icons
    ./make-icons.ps1 -Ico ..\GrtReloadingToolkit\app.ico   # also write the application icon
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\GrtReloadingToolkit\plugin\media\icons"),
    [string]$Ico = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force $outDir | Out-Null

# Palette for icn_toolkit, which is drawn in colour rather than white line art.
#
# GRT recolours a white glyph only where the manifest asks it to, and in the plugin dropdown it
# does not, so a white icon there is white on a white menu and simply cannot be seen. The three
# plugins GRT ships alongside this one (chrono, miller, seating) all sidestep that by shipping
# full-colour artwork with icon_colorize="false": their icons measure mean luminance 53, 59 and
# 151, against 254 for the white set here. This palette follows them, and is warm for the same
# reason theirs is - brass is what the subject actually looks like.
$Steel      = [System.Drawing.Color]::FromArgb(255, 44, 50, 56)     # reticle ring, outlines
$BrassFill  = [System.Drawing.Color]::FromArgb(255, 199, 158, 74)   # cartridge case body
$BrassEdge  = [System.Drawing.Color]::FromArgb(255, 133, 100, 40)   # case outline, darker brass
$CopperFill = [System.Drawing.Color]::FromArgb(255, 162, 79, 38)    # jacketed bullet

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

# A loaded round: brass case, copper bullet, both outlined. $bw and $nw are the case and neck
# half-widths; the shoulder and neck sit proportionally along the round so the silhouette reads as
# a bottlenecked rifle case rather than a lozenge. Only icn_toolkit uses this today, but it is the
# one shape here with enough geometry to be worth naming.
#
# The case used to be three open strokes in the icn_seating idiom - outline only, no fill - which
# is right for a white glyph that GRT tints, and wrong the moment the icon carries its own colour:
# an unfilled outline in brass reads as a wire drawing, not as brass. So the same points now build
# one closed path that gets filled and then stroked.
function CaseAndBullet($g, $k, $cx, $yBase, $yTip, $bw, $nw, $penW, $edge, $caseBrush, $bulletBrush) {
    $p = New-Object System.Drawing.Pen($edge, $penW)
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Proportions of a real bottlenecked rifle round, near enough: the body is over half the length,
    # the shoulder is a short sharp taper, the neck is shorter still, and the bullet is the rest.
    # An earlier, blunter split (0.40 / 0.12 / 0.10) left the body too short and the ogive too long,
    # and at 32px the whole thing read as a lozenge rather than a cartridge.
    $span = $yBase - $yTip
    $ySh  = $yBase - $span * 0.52   # body starts tapering into the shoulder
    $yNk  = $ySh - $span * 0.14     # top of the shoulder / bottom of the neck
    $yOg  = $yNk - $span * 0.06     # where the ogive takes over

    $case = New-Object System.Drawing.Drawing2D.GraphicsPath
    $case.AddLines(@((Pt ($cx - $bw) $yBase $k), (Pt ($cx - $bw) $ySh $k),
                     (Pt ($cx - $nw) $yNk $k),   (Pt ($cx - $nw) $yOg $k),
                     (Pt ($cx + $nw) $yOg $k),   (Pt ($cx + $nw) $yNk $k),
                     (Pt ($cx + $bw) $ySh $k),   (Pt ($cx + $bw) $yBase $k)))
    $case.CloseFigure()
    $g.FillPath($caseBrush, $case)
    $g.DrawPath($p, $case)

    # The control points pull the curve most of the way up at full width before turning in, which
    # is what makes an ogive an ogive rather than a dome. 0.45 is the height at which it starts to
    # narrow; 0.25 is how close to the axis the tangent comes at the tip.
    $yCtl = $yTip + ($yOg - $yTip) * 0.45
    $og = New-Object System.Drawing.Drawing2D.GraphicsPath
    $og.AddBezier((Pt ($cx - $nw) $yOg $k), (Pt ($cx - $nw) $yCtl $k),
                  (Pt ($cx - $nw * 0.25) $yTip $k), (Pt $cx $yTip $k))
    $og.AddBezier((Pt $cx $yTip $k), (Pt ($cx + $nw * 0.25) $yTip $k),
                  (Pt ($cx + $nw) $yCtl $k), (Pt ($cx + $nw) $yOg $k))
    $og.CloseFigure()
    $g.FillPath($bulletBrush, $og)
    $g.DrawPath($p, $og)

    $case.Dispose()
    $og.Dispose()
    $p.Dispose()
}

# ------------------------------------------------------------------ definitions --

$icons = @{

  # Athlon import - a bullet dropping into a tray
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

  # Ladder / OCW - ascending bars, one node ringed
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

  # Seating depth - bullet seated in a case, with a depth double-arrow
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

  # Barrel calibration - gauge arc + needle + adjust tick
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

  # Powder temp coefficient - thermometer
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

  # Load card / label - a tag with a hole and lines + a small QR square
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

  # Brass prep - case mouth + caliper jaws
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

  # Toolkit - a loaded round centred in a reticle (single entry-point icon)
  #
  # This replaced a wrench crossed with a screwdriver, which said "tools" and nothing about
  # reloading, while every icon around it names its own job. The 16px branch drops the reticle
  # ticks rather than scaling them: at that size they collide with the ring and read as noise,
  # the same reason icn_cal drops its +/- marks and icn_brass drops its caliper jaws.
  "icn_toolkit" = {
    param($g, $pen, $brush, $k, $S)
    # The shared white $pen and $brush are deliberately unused here: this is the one icon GRT
    # renders itself, so it carries its own colour. See the palette at the top of the file.
    $ringW = if ($S -le 16) { 1.9 } else { 2.4 }
    $pr = New-Object System.Drawing.Pen($Steel, $ringW)
    $pr.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pr.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $bCase   = New-Object System.Drawing.SolidBrush($BrassFill)
    $bBullet = New-Object System.Drawing.SolidBrush($CopperFill)
    if ($S -le 16) {
        # At 16px the round is about 8 device pixels wide and the bullet barely 3 tall, so the
        # outline has to get *thinner* rather than thicker: at 2.0 it ate the copper entirely and
        # the icon read as a plain brass slug. The round is also stretched nearer the ring than at
        # 32px, because the ogive only reads at all once it has three pixels to work with.
        $g.DrawEllipse($pr, (2.5 * $k), (2.5 * $k), (27 * $k), (27 * $k))
        CaseAndBullet $g $k 16 26 5.5 3.9 2.4 1.2 $BrassEdge $bCase $bBullet
    } else {
        $g.DrawEllipse($pr, (3 * $k), (3 * $k), (26 * $k), (26 * $k))
        CaseAndBullet $g $k 16 25 6.5 4.0 2.4 1.5 $BrassEdge $bCase $bBullet
        # reticle ticks, outside the ring at N/S/E/W
        $g.DrawLine($pr, (Pt 16 0 $k),  (Pt 16 3 $k))
        $g.DrawLine($pr, (Pt 16 29 $k), (Pt 16 32 $k))
        $g.DrawLine($pr, (Pt 0 16 $k),  (Pt 3 16 $k))
        $g.DrawLine($pr, (Pt 29 16 $k), (Pt 32 16 $k))
    }
    $bCase.Dispose()
    $bBullet.Dispose()
    $pr.Dispose()
  }

  # Inventory / journal - clipboard with a check and entry lines
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

# One bitmap as an ICO image payload.
#
# Sizes below 256 go in as a DIB, which is what every Windows version since 95 reads; 256 goes in
# as a PNG, because a 256x256 DIB is a quarter of a megabyte of the exe and PNG entries are the
# documented way around that (Vista+). Mixing the two in one file is the ordinary convention, not
# a compromise.
#
# The DIB is a BITMAPINFOHEADER whose biHeight is *doubled*: the format expects a colour bitmap
# followed by a 1-bit AND mask, and the header covers both. The mask is all zeros - "opaque
# everywhere" - because the alpha channel already carries the transparency and every reader that
# understands a 32bpp icon uses it.
function IcoPayload($bmp) {
    $S = $bmp.Width
    if ($S -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose()
        return $bytes
    }

    $maskStride = [int]([Math]::Floor(($S + 31) / 32)) * 4
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([uint32]40); $w.Write([int32]$S); $w.Write([int32]($S * 2))
    $w.Write([uint16]1);  $w.Write([uint16]32)
    $w.Write([uint32]0);  $w.Write([uint32]0)
    $w.Write([int32]0);   $w.Write([int32]0)
    $w.Write([uint32]0);  $w.Write([uint32]0)
    for ($y = $S - 1; $y -ge 0; $y--) {       # DIB rows run bottom-up
        for ($x = 0; $x -lt $S; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskStride * $S)))
    $w.Flush()
    $bytes = $out.ToArray()
    $w.Dispose()
    return $bytes
}

# A multi-size .ico: ICONDIR header, one 16-byte ICONDIRENTRY per image, then the payloads.
function WriteIco($path, $bitmaps) {
    # [byte[]] on the way in, because a function returning an array emits its elements one at a
    # time: without the cast each payload arrives as a loose stream of bytes and the entries end
    # up one byte long.
    $payloads = New-Object System.Collections.ArrayList
    foreach ($b in $bitmaps) { [void]$payloads.Add([byte[]](IcoPayload $b)) }

    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$payloads.Count)
    $offset = 6 + 16 * $payloads.Count
    for ($i = 0; $i -lt $payloads.Count; $i++) {
        $S = $bitmaps[$i].Width
        # 256 is written as 0: the field is one byte, so 256 does not fit and 0 means 256.
        $w.Write([byte]($S % 256)); $w.Write([byte]($S % 256))
        $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$payloads[$i].Length); $w.Write([uint32]$offset)
        $offset += $payloads[$i].Length
    }
    foreach ($p in $payloads) { $w.Write($p) }
    $w.Flush()
    [IO.File]::WriteAllBytes($path, $out.ToArray())
    $w.Dispose()
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

if ($Ico -ne "") {
    # The app icon is the same drawing as the plugin icon, at the four sizes Windows asks for:
    # 16 in the title bar, 32 on the taskbar, 48 in Explorer's medium view, 256 for the large ones.
    # Resolve against the caller's location, not the process working directory, which is where a
    # relative path passed to [IO.File] would otherwise land.
    $icoPath = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $Ico))
    $bitmaps = @(16, 32, 48, 256 | ForEach-Object { Draw $_ $icons["icn_toolkit"] })
    WriteIco $icoPath $bitmaps
    $bitmaps | ForEach-Object { $_.Dispose() }
    Write-Host "done -> $icoPath  (16 + 32 + 48 + 256)"
}
