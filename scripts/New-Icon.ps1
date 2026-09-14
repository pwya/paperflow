$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\PaperProgress\Assets'
[IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$images = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $g.Clear([Drawing.Color]::Transparent)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(8,8,64,64,180,90); $shape.AddArc(184,8,64,64,270,90)
    $shape.AddArc(184,184,64,64,0,90); $shape.AddArc(8,184,64,64,90,90); $shape.CloseFigure()
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new(256,256),[Drawing.ColorTranslator]::FromHtml('#289481'),[Drawing.ColorTranslator]::FromHtml('#164F54'))
    $g.FillPath($gradient,$shape)
    $paper = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F4FFF9'))
    $g.FillRectangle($paper,62,44,128,164)
    $line = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#A8CFC4'),11)
    $line.StartCap = $line.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($line,85,78,153,78); $g.DrawLine($line,85,105,142,105)
    $g.DrawLine($line,85,132,128,132)
    $tick = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#25846F'),16)
    $tick.StartCap = $tick.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawLines($tick, [Drawing.Point[]]@([Drawing.Point]::new(111,169),[Drawing.Point]::new(131,188),[Drawing.Point]::new(174,143)))
    $stream = [IO.MemoryStream]::new(); $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $images.Add($stream.ToArray())
    $stream.Dispose(); $tick.Dispose(); $line.Dispose(); $paper.Dispose(); $gradient.Dispose(); $shape.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $assetDirectory 'app.ico')); $writer = [IO.BinaryWriter]::new($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }; $writer.Dispose()
Write-Output 'Generated application icon (7 sizes).'
