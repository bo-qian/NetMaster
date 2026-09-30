param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repoRoot 'assets/branding/netmaster-icon.png'
$assetDirectory = Join-Path $repoRoot 'windows/NetMaster.WinUI/Assets'
$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $assets = @(
        @{ Name = 'StoreLogo.png'; Width = 50; Height = 50; Icon = 50 },
        @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88; Icon = 88 },
        @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24; Icon = 24 },
        @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300; Icon = 280 },
        @{ Name = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300; Icon = 260 },
        @{ Name = 'SplashScreen.scale-200.png'; Width = 1240; Height = 600; Icon = 360 },
        @{ Name = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48; Icon = 48 }
    )
    foreach ($asset in $assets) {
        $bitmap = [System.Drawing.Bitmap]::new($asset.Width, $asset.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $x = [int](($asset.Width - $asset.Icon) / 2)
            $y = [int](($asset.Height - $asset.Icon) / 2)
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new($x, $y, $asset.Icon, $asset.Icon))
            $bitmap.Save((Join-Path $assetDirectory $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }
