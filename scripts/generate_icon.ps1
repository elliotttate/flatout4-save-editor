# Regenerates Assets\FlatOut4SaveEditor.ico, the icon of the app, its launchers and the Start menu
# shortcut: the title bar's car glyph (Segoe Fluent Icons E804) on a rounded purple square.
#   powershell -File scripts\generate_icon.ps1
# Each size is drawn on its own (not scaled down from 256) so the small sizes stay crisp.

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$fontName = "Segoe Fluent Icons"
$probe = New-Object System.Drawing.FontFamily($fontName)
if ($probe.Name -ne $fontName) {
    throw "$fontName is not installed (it ships with Windows 11)."
}

$top = [System.Drawing.Color]::FromArgb(255, 0xB0, 0x5C, 0xD0)
$bottom = [System.Drawing.Color]::FromArgb(255, 0x7A, 0x2E, 0x9C)
$glyph = [string][char]0xE804

function New-IconFrame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square filling the frame, corner radius ~22% like the title bar logo.
    $radius = [Math]::Max(2.0, $size * 0.22)
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillPath($brush, $path)

    # The glyph as a path, scaled to fill ~66% of the square (more at tiny sizes) and centred
    # on its actual outline rather than the font's line box.
    $glyphPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $glyphPath.AddString($glyph, $probe, 0, 100.0, (New-Object System.Drawing.PointF(0, 0)), $format)
    $bounds = $glyphPath.GetBounds()
    $fill = if ($size -le 24) { 0.76 } else { 0.66 }
    $scale = ($size * $fill) / [Math]::Max($bounds.Width, $bounds.Height)
    $matrix = New-Object System.Drawing.Drawing2D.Matrix
    $matrix.Translate($size / 2.0, $size / 2.0)
    $matrix.Scale($scale, $scale)
    $matrix.Translate(-($bounds.X + $bounds.Width / 2.0), -($bounds.Y + $bounds.Height / 2.0))
    $glyphPath.Transform($matrix)
    $g.FillPath([System.Drawing.Brushes]::White, $glyphPath)

    $g.Dispose()
    return $bitmap
}

# ICO with PNG-compressed frames (supported since Windows Vista).
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = New-IconFrame $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , @($size, $stream.ToArray())
}

$out = Join-Path $RepoRoot "Assets\FlatOut4SaveEditor.ico"
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $size = $frame[0]
    $bytes = $frame[1]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame[1])
}
$writer.Flush()
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
$writer.Dispose()
Write-Host "Wrote $out ($($sizes -join ', ') px)"
