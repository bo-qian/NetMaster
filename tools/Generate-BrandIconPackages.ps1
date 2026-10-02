$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repoRoot 'assets/branding/netmaster-icon.png'
$sourceBytes = [System.IO.File]::ReadAllBytes($sourcePath)
$sourceBase64 = [Convert]::ToBase64String($sourceBytes)
$svg = '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1254 1254" role="img" aria-label="NetMaster app icon"><image x="0" y="0" width="1254" height="1254" xlink:href="data:image/png;base64,' + $sourceBase64 + '"/></svg>'
[System.IO.File]::WriteAllText((Join-Path $repoRoot 'assets/branding/netmaster-icon.svg'), $svg, [System.Text.UTF8Encoding]::new($false))

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$pngEntries = @()
$source = [System.Drawing.Bitmap]::new($sourcePath)
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
        }
        finally { $graphics.Dispose() }
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            if ($size -eq 16 -or $size -eq 32) {
                $bitmap.Save((Join-Path $repoRoot "assets/branding/netmaster-icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            $pngEntries += ,@($size, $stream.ToArray())
        }
        finally { $stream.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }

$output = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$pngEntries.Count)
    $offset = 6 + (16 * $pngEntries.Count)
    foreach ($entry in $pngEntries) {
        $size = [int]$entry[0]
        $bytes = [byte[]]$entry[1]
        $dimension = $size
        if ($size -ge 256) { $dimension = 0 }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($entry in $pngEntries) { $writer.Write([byte[]]$entry[1]) }
    [System.IO.File]::WriteAllBytes((Join-Path $repoRoot 'assets/branding/netmaster-icon.ico'), $output.ToArray())
}
finally { $writer.Dispose(); $output.Dispose() }

Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $repoRoot 'windows/NetMaster/Assets/NetMaster.png') -Force
Write-Host "Generated embedded SVG, $($sizes.Count)-size PNG ICO, and WinUI title-bar asset."
