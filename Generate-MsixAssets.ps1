# MSIX Asset Generator for TachoGraphStudio
# Generates all required scale and targetsize variants from source images

param(
    [string]$SourceIcon = "C:\Users\dev\Dropbox\工事データ\TachoGraphStudio\assets\icon.png",
    [string]$SourceSplash = "C:\Users\dev\Dropbox\工事データ\TachoGraphStudio\assets\splash_logo.png",
    [string]$OutputDir = "src\TachoGraphStudio.App\Assets"
)

Add-Type -AssemblyName System.Drawing

function Resize-ImageHighQuality {
    param(
        [System.Drawing.Image]$Image,
        [int]$Width,
        [int]$Height
    )

    $destRect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $destImage = New-Object System.Drawing.Bitmap($Width, $Height)

    $destImage.SetResolution($Image.HorizontalResolution, $Image.VerticalResolution)

    $graphics = [System.Drawing.Graphics]::FromImage($destImage)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $wrapMode = New-Object System.Drawing.Imaging.ImageAttributes
    $wrapMode.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

    $graphics.DrawImage($Image, $destRect, 0, 0, $Image.Width, $Image.Height, [System.Drawing.GraphicsUnit]::Pixel, $wrapMode)
    $graphics.Dispose()
    $wrapMode.Dispose()

    return $destImage
}

function Save-Asset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$BaseSize,
        [string]$OutputDir,
        [int[]]$Scales = @(100, 125, 150, 200, 400)
    )

    foreach ($scale in $Scales) {
        $size = [math]::Round($BaseSize * $scale / 100)
        $resized = Resize-ImageHighQuality -Image $SourceImage -Width $size -Height $size

        $fileName = if ($scale -eq 100) {
            "$BaseName.png"
        } else {
            "$BaseName.scale-$scale.png"
        }

        $outputPath = Join-Path $OutputDir $fileName
        $resized.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()

        Write-Host "Generated: $fileName ($size x $size)"
    }
}

function Save-TargetSizeAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$TargetSize,
        [string]$OutputDir,
        [bool]$Unplated = $false
    )

    $resized = Resize-ImageHighQuality -Image $SourceImage -Width $TargetSize -Height $TargetSize

    $suffix = if ($Unplated) { "_altform-unplated" } else { "" }
    $fileName = "$BaseName.targetsize-$TargetSize$suffix.png"

    $outputPath = Join-Path $OutputDir $fileName
    $resized.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()

    Write-Host "Generated: $fileName ($TargetSize x $TargetSize)"
}

function Save-SplashAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$BaseWidth,
        [int]$BaseHeight,
        [string]$OutputDir,
        [int[]]$Scales = @(100, 125, 150, 200, 400)
    )

    foreach ($scale in $Scales) {
        $width = [math]::Round($BaseWidth * $scale / 100)
        $height = [math]::Round($BaseHeight * $scale / 100)
        $resized = Resize-ImageHighQuality -Image $SourceImage -Width $width -Height $height

        $fileName = if ($scale -eq 100) {
            "$BaseName.png"
        } else {
            "$BaseName.scale-$scale.png"
        }

        $outputPath = Join-Path $OutputDir $fileName
        $resized.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()

        Write-Host "Generated: $fileName ($width x $height)"
    }
}

Write-Host "Starting MSIX asset generation..."
Write-Host ""

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Load source images
$iconImage = [System.Drawing.Image]::FromFile($SourceIcon)
$splashImage = [System.Drawing.Image]::FromFile($SourceSplash)

Write-Host "Source images loaded:"
Write-Host "  Icon: $($iconImage.Width)x$($iconImage.Height)"
Write-Host "  Splash: $($splashImage.Width)x$($splashImage.Height)"
Write-Host ""

# Square44x44Logo (App icon) - scale variants
Write-Host "Generating Square44x44Logo (scale variants)..."
Save-Asset -SourceImage $iconImage -BaseName "Square44x44Logo" -BaseSize 44 -OutputDir $OutputDir

# Square44x44Logo - targetsize variants
Write-Host ""
Write-Host "Generating Square44x44Logo (targetsize variants)..."
$targetSizes = @(16, 24, 32, 48, 256)
foreach ($size in $targetSizes) {
    Save-TargetSizeAsset -SourceImage $iconImage -BaseName "Square44x44Logo" -TargetSize $size -OutputDir $OutputDir -Unplated $false
    Save-TargetSizeAsset -SourceImage $iconImage -BaseName "Square44x44Logo" -TargetSize $size -OutputDir $OutputDir -Unplated $true
}

# Square150x150Logo (Medium tile) - scale variants
Write-Host ""
Write-Host "Generating Square150x150Logo (scale variants)..."
Save-Asset -SourceImage $iconImage -BaseName "Square150x150Logo" -BaseSize 150 -OutputDir $OutputDir

# Wide310x150Logo (Wide tile) - scale variants (non-square, use splash)
Write-Host ""
Write-Host "Generating Wide310x150Logo (scale variants)..."
Save-SplashAsset -SourceImage $splashImage -BaseName "Wide310x150Logo" -BaseWidth 310 -BaseHeight 150 -OutputDir $OutputDir

# StoreLogo - scale variants
Write-Host ""
Write-Host "Generating StoreLogo (scale variants)..."
Save-Asset -SourceImage $iconImage -BaseName "StoreLogo" -BaseSize 50 -OutputDir $OutputDir

# SplashScreen - scale variants
Write-Host ""
Write-Host "Generating SplashScreen (scale variants)..."
Save-SplashAsset -SourceImage $splashImage -BaseName "SplashScreen" -BaseWidth 620 -BaseHeight 300 -OutputDir $OutputDir

# Clean up
$iconImage.Dispose()
$splashImage.Dispose()

Write-Host ""
Write-Host "Asset generation completed!"
Write-Host "Total assets in $OutputDir :"
$assetCount = (Get-ChildItem -Path $OutputDir -Filter "*.png" | Measure-Object).Count
Write-Host "  $assetCount PNG files"
