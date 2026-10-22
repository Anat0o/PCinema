# Rebuild the original vector logo as PNG and a multi-resolution Windows icon.
Add-Type -AssemblyName PresentationCore,WindowsBase
$assetDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../PCinema/Assets'))
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size/256.0),($size/256.0)))
    $bg = New-Object Windows.Media.LinearGradientBrush ([Windows.Media.ColorConverter]::ConvertFromString('#303641')),([Windows.Media.ColorConverter]::ConvertFromString('#101216')),45
    $edge = New-Object Windows.Media.Pen ([Windows.Media.BrushConverter]::new().ConvertFromString('#59616D')),2
    $dc.DrawRoundedRectangle($bg,$edge,(New-Object Windows.Rect 6,6,244,244),55,55)
    $p = [Windows.Media.Geometry]::Parse('M 73,202 L 73,58 L 135,58 C 174,58 195,78 195,112 C 195,146 174,166 135,166 L 105,166 L 105,202 Z M 110,86 L 110,139 L 156,112 Z')
    $dc.DrawGeometry([Windows.Media.Brushes]::White,$null,$p)
    $dc.Pop(); $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    $frames += ,($stream.ToArray()); $stream.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $assetDir 'pcinema-logo.png'),$frames[-1])
$output = [IO.File]::Create((Join-Path $assetDir 'pcinema.ico'))
$writer = New-Object IO.BinaryWriter $output
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
$sizes = @(16,24,32,48,64,128,256)
for ($i=0;$i -lt $frames.Count;$i++) {
    $dimension = $sizes[$i] % 256
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
    $writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach($frame in $frames){$writer.Write([byte[]]$frame)}
$writer.Dispose();$output.Dispose()
