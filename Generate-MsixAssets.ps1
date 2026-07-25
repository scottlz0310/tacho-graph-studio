<#
.SYNOPSIS
MSIX パッケージ用のアセットを原画から一括生成する。

.DESCRIPTION
assets-source/ の原画から、scale 修飾子つき派生と targetsize 派生を生成する。
生成前に既存の生成物を削除するため、命名規則を変更しても旧世代のファイルが
取り残されない（#80）。

scale 100 は修飾子なしのファイル名（例: SplashScreen.png）で出力する。
`.scale-100.png` を併置すると 100% DPI 環境でそちらが優先され、
非修飾版との差分が事故になるため、どちらか一方に統一する必要がある。
#>

param(
    [string]$SourceIcon = "assets-source\icon.png",
    [string]$SourceSplash = "assets-source\splash.png",
    [string]$OutputDir = "src\TachoGraphStudio.App\Assets"
)

$ErrorActionPreference = "Stop"

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

function Save-ScaledAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$BaseWidth,
        [int]$BaseHeight,
        [string]$OutputDir,
        [int[]]$Scales = @(100, 125, 150, 200)
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

        Write-Host "  生成: $fileName ($width x $height)"
    }
}

function Save-TargetSizeAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$TargetSize,
        [string]$OutputDir
    )

    # plated（背景板つき）と unplated（背景板なし）は同一画像を別名で提供する。
    # タスクバーは unplated、スタートメニューは plated を参照する。
    foreach ($suffix in @("", "_altform-unplated")) {
        $resized = Resize-ImageHighQuality -Image $SourceImage -Width $TargetSize -Height $TargetSize
        $fileName = "$BaseName.targetsize-$TargetSize$suffix.png"
        $resized.Save((Join-Path $OutputDir $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()

        Write-Host "  生成: $fileName ($TargetSize x $TargetSize)"
    }
}

Write-Host "MSIX アセット生成を開始します"
Write-Host ""

if (-not (Test-Path $SourceIcon)) { throw "原画が見つかりません: $SourceIcon" }
if (-not (Test-Path $SourceSplash)) { throw "原画が見つかりません: $SourceSplash" }

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# 旧世代の生成物を除去する。app.ico は本スクリプトの生成対象外なので残す。
$stale = Get-ChildItem -Path $OutputDir -Filter "*.png" -File
if ($stale) {
    Write-Host "既存の生成物 $($stale.Count) 件を削除します"
    $stale | Remove-Item -Force
    Write-Host ""
}

$iconImage = [System.Drawing.Image]::FromFile((Resolve-Path $SourceIcon))
$splashImage = [System.Drawing.Image]::FromFile((Resolve-Path $SourceSplash))

Write-Host "原画:"
Write-Host "  アイコン: $($iconImage.Width)x$($iconImage.Height)"
Write-Host "  スプラッシュ: $($splashImage.Width)x$($splashImage.Height)"
Write-Host ""

try {
    # 正方形ロゴ群（アイコン原画由来）
    foreach ($logo in @(
            @{ Name = "Square44x44Logo"; Size = 44 },
            @{ Name = "Square71x71Logo"; Size = 71 },
            @{ Name = "Square150x150Logo"; Size = 150 },
            @{ Name = "Square310x310Logo"; Size = 310 },
            @{ Name = "StoreLogo"; Size = 50 }
        )) {
        Write-Host "$($logo.Name) を生成しています"
        Save-ScaledAsset -SourceImage $iconImage -BaseName $logo.Name `
            -BaseWidth $logo.Size -BaseHeight $logo.Size -OutputDir $OutputDir
        Write-Host ""
    }

    # タスクバー・エクスプローラー用の実寸アイコン
    Write-Host "Square44x44Logo の targetsize 派生を生成しています"
    foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 72, 80, 96, 256)) {
        Save-TargetSizeAsset -SourceImage $iconImage -BaseName "Square44x44Logo" `
            -TargetSize $size -OutputDir $OutputDir
    }
    Write-Host ""

    # 横長アセット群（スプラッシュ原画由来）
    foreach ($wide in @(
            @{ Name = "Wide310x150Logo"; Width = 310; Height = 150 },
            @{ Name = "SplashScreen"; Width = 620; Height = 300 }
        )) {
        Write-Host "$($wide.Name) を生成しています"
        Save-ScaledAsset -SourceImage $splashImage -BaseName $wide.Name `
            -BaseWidth $wide.Width -BaseHeight $wide.Height -OutputDir $OutputDir
        Write-Host ""
    }
} finally {
    $iconImage.Dispose()
    $splashImage.Dispose()
}

$assetCount = (Get-ChildItem -Path $OutputDir -Filter "*.png" -File | Measure-Object).Count
Write-Host "生成完了: $OutputDir に PNG $assetCount 件"
