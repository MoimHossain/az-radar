param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string] $BotClientId,

    [string] $OutputPath = "$PSScriptRoot\artifacts\CloudLens-Teams-App.zip"
)

$ErrorActionPreference = 'Stop'
$outputDirectory = Split-Path -Parent $OutputPath
$stagingDirectory = Join-Path $outputDirectory 'package'

New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null

$manifest = Get-Content "$PSScriptRoot\manifest.json" -Raw
$manifest = $manifest.Replace('00000000-0000-0000-0000-000000000000', $BotClientId)
Set-Content -Path (Join-Path $stagingDirectory 'manifest.json') -Value $manifest -Encoding utf8NoBOM

Add-Type -AssemblyName System.Drawing

$sourceIcon = [System.Drawing.Bitmap]::new("$PSScriptRoot\cloud-computing.png")

function New-TeamsIcon {
    param(
        [string] $Path,
        [int] $Size,
        [bool] $Outline
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        if ($Outline) {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $symbolSize = $Size
        }
        else {
            $graphics.Clear([System.Drawing.Color]::White)
            $symbolSize = 120
        }
        $scale = [Math]::Min($symbolSize / $sourceBounds.Width, $symbolSize / $sourceBounds.Height)
        $width = [int][Math]::Round($sourceBounds.Width * $scale)
        $height = [int][Math]::Round($sourceBounds.Height * $scale)
        $rect = [System.Drawing.Rectangle]::new([int](($Size - $width) / 2), [int](($Size - $height) / 2), $width, $height)
        $graphics.DrawImage($sourceIcon, $rect, $sourceBounds, [System.Drawing.GraphicsUnit]::Pixel)

        if ($Outline) {
            # Preserve the artwork's transparency and antialiasing, but use Teams' white-only glyph.
            for ($y = 0; $y -lt $Size; $y++) {
                for ($x = 0; $x -lt $Size; $x++) {
                    $alpha = $bitmap.GetPixel($x, $y).A
                    $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))
                }
            }
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    # Trim source padding so the outline fills its canvas and the color mark fits the safe area.
    $left = $sourceIcon.Width
    $top = $sourceIcon.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $sourceIcon.Height; $y++) {
        for ($x = 0; $x -lt $sourceIcon.Width; $x++) {
            if ($sourceIcon.GetPixel($x, $y).A -gt 0) {
                $left = [Math]::Min($left, $x)
                $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x)
                $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    if ($right -lt 0) {
        throw 'The source icon contains no visible pixels.'
    }
    $sourceBounds = [System.Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
    New-TeamsIcon -Path (Join-Path $stagingDirectory 'color.png') -Size 192 -Outline $false
    New-TeamsIcon -Path (Join-Path $stagingDirectory 'outline.png') -Size 32 -Outline $true
}
finally {
    $sourceIcon.Dispose()
}

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath
}
Compress-Archive -Path (Join-Path $stagingDirectory 'manifest.json'), (Join-Path $stagingDirectory 'color.png'), (Join-Path $stagingDirectory 'outline.png') -DestinationPath $OutputPath
Remove-Item -Recurse $stagingDirectory

Write-Host "Created CloudLens Teams app package: $OutputPath"
