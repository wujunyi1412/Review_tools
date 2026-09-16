param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\ReviewApp\Assets\ReviewTool.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $radius
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)

    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(37, 99, 235))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $pale = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(222, 239, 255))
    $teal = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(20, 184, 166))
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 158, 11))
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 10)
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    try {
        $background = New-RoundedPath 12 12 232 232 48
        $graphics.FillPath($blue, $background)
        $background.Dispose()

        $left = New-RoundedPath 35 54 86 135 12
        $right = New-RoundedPath 135 54 86 135 12
        $graphics.FillPath($white, $left)
        $graphics.FillPath($white, $right)
        $left.Dispose()
        $right.Dispose()

        $graphics.FillRectangle($pale, 45, 69, 66, 88)
        $graphics.FillRectangle($pale, 145, 69, 66, 88)
        $graphics.FillEllipse($teal, 83, 79, 18, 18)
        $graphics.FillEllipse($teal, 183, 79, 18, 18)
        $graphics.FillPolygon($teal, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(45, 157), [System.Drawing.PointF]::new(45, 132),
            [System.Drawing.PointF]::new(69, 108), [System.Drawing.PointF]::new(111, 151),
            [System.Drawing.PointF]::new(111, 157)))
        $graphics.FillPolygon($teal, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(145, 157), [System.Drawing.PointF]::new(145, 141),
            [System.Drawing.PointF]::new(174, 109), [System.Drawing.PointF]::new(211, 144),
            [System.Drawing.PointF]::new(211, 157)))
        $graphics.FillEllipse($gold, 153, 146, 74, 74)
        $graphics.DrawLines($line, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(170, 182), [System.Drawing.PointF]::new(183, 195),
            [System.Drawing.PointF]::new(210, 170)))

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally { $stream.Dispose() }
    }
    finally {
        $line.Dispose(); $gold.Dispose(); $teal.Dispose(); $pale.Dispose()
        $white.Dispose(); $blue.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}

$sizes = @(16, 32, 48, 256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) { $images.Add([byte[]](New-IconPng $size)) }
$folder = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $folder 'ReviewTool.png'), [byte[]]$images[3])
$stream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $writer.Write([byte]($sizes[$index] % 256))
        $writer.Write([byte]($sizes[$index] % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
}
finally { $writer.Dispose(); $stream.Dispose() }
