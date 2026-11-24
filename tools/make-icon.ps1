# Draws Battery's icon at every size Windows asks for and writes a multi-size .ico (PNG entries).
# A dark battery with three usage bars inside, one per app, in the strip's identity colours.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$Out = "src\Battery.App\Battery.ico",
    [string]$PreviewDir = ""
)
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$body = [System.Drawing.ColorTranslator]::FromHtml("#2D2F37")
$edge = [System.Drawing.ColorTranslator]::FromHtml("#8A8D99")
$bars = @(
    @{ Colour = "#E08A5B"; Fill = 0.80 },   # Claude
    @{ Colour = "#7FB0FF"; Fill = 0.55 },   # Codex
    @{ Colour = "#5CC8B0"; Fill = 0.35 }    # OpenCode Go
)

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.01, [Math]::Min($r * 2, [Math]::Min($w, $h)))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'Half'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Whole pixels at small sizes keep the bars crisp in the tray.
    $stroke = [Math]::Max(1, [Math]::Round($s / 20))
    $nubW = [Math]::Max(2, [Math]::Round($s * 0.08))
    $bodyX = 0; $bodyW = $s - $nubW
    $bodyH = [Math]::Max(10, [Math]::Round($s * 0.6)); $bodyY = [Math]::Round(($s - $bodyH) / 2)
    $r = [Math]::Max(1.5, $s * 0.1)

    $outline = RoundRect ($bodyX + $stroke / 2) ($bodyY + $stroke / 2) ($bodyW - $stroke) ($bodyH - $stroke) $r
    $g.FillPath((New-Object System.Drawing.SolidBrush $body), $outline)
    $g.DrawPath((New-Object System.Drawing.Pen $edge, $stroke), $outline)

    $nubH = [Math]::Max(2, [Math]::Round($bodyH * 0.36))
    $nub = RoundRect ($bodyW) ([Math]::Round(($s - $nubH) / 2)) $nubW $nubH ([Math]::Max(0.5, $nubW * 0.4))
    $g.FillPath((New-Object System.Drawing.SolidBrush $edge), $nub)

    # Three bars inside, equal height, equal gaps.
    $pad = $stroke + $(if ($s -lt 24) { 0 } else { [Math]::Round($s / 24) })
    $innerX = $bodyX + $pad; $innerW = $bodyW - 2 * $pad
    $innerY = $bodyY + $pad; $innerH = $bodyH - 2 * $pad
    $gap = [Math]::Max(1, [Math]::Round($innerH / 9))
    $barH = [Math]::Floor(($innerH - 2 * $gap) / 3)
    $top = $innerY + [Math]::Floor(($innerH - (3 * $barH + 2 * $gap)) / 2)
    for ($i = 0; $i -lt 3; $i++) {
        $w = [Math]::Max($barH, [Math]::Round($innerW * $bars[$i].Fill))
        $y = $top + $i * ($barH + $gap)
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($bars[$i].Colour))
        if ($s -lt 32) { $g.FillRectangle($brush, $innerX, $y, $w, $barH) }
        else { $g.FillPath($brush, (RoundRect $innerX $y $w $barH ([Math]::Min($barH / 2, $s / 32)))) }
    }
    $g.Dispose()
    return $bmp
}

$pngs = foreach ($s in $sizes) {
    $bmp = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PreviewDir) { $bmp.Save((Join-Path $PreviewDir "icon-$s.png")) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICONDIR + ICONDIRENTRY per image, PNG data after.
$fs = [System.IO.File]::Create((Join-Path (Get-Location) $Out))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
"wrote $Out ($($sizes -join ', ') px)"
