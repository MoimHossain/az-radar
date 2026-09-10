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

function New-TeamsIcon {
    param(
        [string] $Path,
        [int] $Size,
        [bool] $Outline
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $margin = [Math]::Max(2, [int]($Size * 0.12))
        $rect = [System.Drawing.Rectangle]::new($margin, $margin, $Size - (2 * $margin), $Size - (2 * $margin))
        if ($Outline) {
            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [Math]::Max(2, $Size * 0.08))
            try { $graphics.DrawEllipse($pen, $rect) } finally { $pen.Dispose() }
        }
        else {
            $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0, 120, 212))
            try { $graphics.FillEllipse($brush, $rect) } finally { $brush.Dispose() }
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-TeamsIcon -Path (Join-Path $stagingDirectory 'color.png') -Size 192 -Outline $false
New-TeamsIcon -Path (Join-Path $stagingDirectory 'outline.png') -Size 32 -Outline $true

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath
}
Compress-Archive -Path "$stagingDirectory\*" -DestinationPath $OutputPath
Remove-Item -Recurse $stagingDirectory

Write-Host "Created CloudLens Teams app package: $OutputPath"
