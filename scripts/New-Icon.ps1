$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$assetDirectory = Join-Path $PSScriptRoot '..\Assets'
$markup = @'
<Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="256" Height="256"><Canvas Width="256" Height="256">
<Rectangle Width="256" Height="256" RadiusX="60" RadiusY="60" Fill="#1b3023"/>
<Path Data="M157 60H74a18 18 0 0 0-18 18v104a18 18 0 0 0 18 18h52" Stroke="#a8f0cd" StrokeThickness="17" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
<Path Data="M114 94v62l44-31z" Fill="#f2f5f3"/>
<Path Data="M190 56v27" Stroke="#f4a6b8" StrokeThickness="17" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
<Path Data="M183 143v62m-24-23 24 24 24-24" Stroke="#a8f0cd" StrokeThickness="17" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
</Canvas></Viewbox>
'@
$frames = @()
foreach ($size in @(16,32,48,64,128,256)) {
    $visual = [Windows.Markup.XamlReader]::Parse($markup)
    $visual.Width = $size
    $visual.Height = $size
    $visual.Measure([Windows.Size]::new($size,$size))
    $visual.Arrange([Windows.Rect]::new(0,0,$size,$size))
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    $encoder.Save($memory)
    $frames += [PSCustomObject]@{Size=$size; Bytes=$memory.ToArray()}
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'logo.png'),$memory.ToArray()) }
    $memory.Dispose()
}
$iconFile = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($iconFile)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) {0} else {$frame.Size}
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Dispose()
Write-Output 'Created original SVG, PNG and multi-resolution Windows icon.'
