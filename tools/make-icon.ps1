# Рисует иконку (ТВ на градиенте) и сохраняет PNG + ICO (PNG внутри ICO)
param([string]$OutIco, [string]$OutPng)
Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90); $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $p.CloseFigure(); return $p
}

$bg = RoundRect 8 8 240 240 90
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0,0), (New-Object System.Drawing.Point 256,256), ([System.Drawing.Color]::FromArgb(255,124,92,255)), ([System.Drawing.Color]::FromArgb(255,255,92,160))
$g.FillPath($brush, $bg)
# блик
$hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0,8), (New-Object System.Drawing.Point 0,130), ([System.Drawing.Color]::FromArgb(110,255,255,255)), ([System.Drawing.Color]::FromArgb(0,255,255,255))
$g.FillPath($hl, (RoundRect 16 14 224 120 80))
# телевизор
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245,255,255,255))
$g.FillPath($white, (RoundRect 54 70 148 100 26))
$g.FillRectangle($white, 112, 170, 32, 14)
$g.FillPath($white, (RoundRect 84 180 88 14 12))
# play
$purple = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,150,80,230))
$g.FillPolygon($purple, [System.Drawing.Point[]]@((New-Object System.Drawing.Point 114,96), (New-Object System.Drawing.Point 114,144), (New-Object System.Drawing.Point 152,120)))
$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
if ($OutPng) { [System.IO.File]::WriteAllBytes($OutPng, $png) }

$fs = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$png.Length); $w.Write([uint32]22)
$w.Write($png); $w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $fs.ToArray())
