param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/KeepingYouAwake.App/Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($filled in @($true, $false)) {
    $frames = @()
    foreach ($size in @(16, 20, 24, 32, 48, 64, 256)) {
        $canvas = New-Object System.Drawing.Bitmap ($size * 4), ($size * 4)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size * 4 / 48.0), ($size * 4 / 48.0))
        $color = [System.Drawing.ColorTranslator]::FromHtml('#EABD77')
        $pen = New-Object System.Drawing.Pen $color, 3.2
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $brush = New-Object System.Drawing.SolidBrush $color
        $cup = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cup.AddLine(7, 17, 32, 17)
        $cup.AddLine(32, 17, 32, 30)
        $cup.AddBezier(32, 30, 32, 40, 7, 40, 7, 30)
        $cup.CloseFigure()
        if ($filled) { $graphics.FillPath($brush, $cup) } else { $graphics.DrawPath($pen, $cup) }
        $graphics.DrawBezier($pen, 33, 20, 46, 17, 46, 33, 34, 31)
        $graphics.DrawLine($pen, 5, 43, 37, 43)
        $graphics.DrawBezier($pen, 14, 12, 8, 8, 20, 5, 14, 2)
        $graphics.DrawBezier($pen, 24, 12, 18, 8, 30, 5, 24, 2)
        $small = New-Object System.Drawing.Bitmap $size, $size
        $scaled = [System.Drawing.Graphics]::FromImage($small)
        $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.DrawImage($canvas, 0, 0, $size, $size)
        $stream = New-Object System.IO.MemoryStream
        $small.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
        $stream.Dispose(); $scaled.Dispose(); $small.Dispose()
        $cup.Dispose(); $brush.Dispose(); $pen.Dispose(); $graphics.Dispose(); $canvas.Dispose()
    }
    $name = if ($filled) { 'coffee-solid.ico' } else { 'coffee-outline.ico' }
    $file = [System.IO.File]::Create((Join-Path $OutputDirectory $name))
    $writer = New-Object System.IO.BinaryWriter $file
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    $writer.Dispose(); $file.Dispose()
}
