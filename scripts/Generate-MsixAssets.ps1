<#
.SYNOPSIS
MSIX パッケージ用のアセットを原画から一括生成する。

.DESCRIPTION
assets-source/ の原画から、scale 修飾子つき派生と targetsize 派生、および
ウィンドウアイコン用の app.ico を生成する。
生成前に既存の生成物を削除するため、命名規則を変更しても旧世代のファイルが
取り残されない（#80）。

既定の入出力パスはスクリプト自身の位置（$PSScriptRoot）基準で解決するため、
どの作業ディレクトリから実行しても結果は変わらない（#85）。

scale 100 は修飾子なしのファイル名（例: SplashScreen.png）で出力する。
`.scale-100.png` を併置すると 100% DPI 環境でそちらが優先され、
非修飾版との差分が事故になるため、どちらか一方に統一する必要がある。

ライト/ダークの使い分け（#88）:
タイル・スタートメニューの各ロゴはマニフェストの BackgroundColor 上に描画され
テーマで変化しないため、ダーク原画のみから生成する。テーマで切り替わるのは
タスクバーの実寸アイコン（altform-unplated / altform-lightunplated）と
スプラッシュ（SplashScreen / SplashScreenLight）の 2 系統。
#>

param(
    [string]$SourceIconDark = (Join-Path $PSScriptRoot "..\assets-source\icon-dark.png"),
    [string]$SourceIconLight = (Join-Path $PSScriptRoot "..\assets-source\icon-light.png"),
    [string]$SourceSplashDark = (Join-Path $PSScriptRoot "..\assets-source\splash-dark.png"),
    [string]$SourceSplashLight = (Join-Path $PSScriptRoot "..\assets-source\splash-light.png"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\src\TachoGraphStudio.App\Assets")
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

# app.ico に収める実寸。Windows はタイトルバー・Alt+Tab・エクスプローラーで
# これらのサイズを直接参照する
$IcoSizes = @(16, 24, 32, 48, 64, 128, 256)

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
        [System.Drawing.Image]$DarkImage,
        [System.Drawing.Image]$LightImage,
        [string]$BaseName,
        [int]$TargetSize,
        [string]$OutputDir
    )

    # 修飾子なし（plated）と unplated はダークテーマ前提の同一画像を別名で提供する。
    # スタートメニューは plated、タスクバーは unplated を参照する。
    # lightunplated はライトテーマのタスクバーが参照する（#88）
    $variants = @(
        @{ Suffix = ""; Image = $DarkImage },
        @{ Suffix = "_altform-unplated"; Image = $DarkImage },
        @{ Suffix = "_altform-lightunplated"; Image = $LightImage }
    )

    foreach ($variant in $variants) {
        $resized = Resize-ImageHighQuality -Image $variant.Image -Width $TargetSize -Height $TargetSize
        $fileName = "$BaseName.targetsize-$TargetSize$($variant.Suffix).png"
        $resized.Save((Join-Path $OutputDir $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()

        Write-Host "  生成: $fileName ($TargetSize x $TargetSize)"
    }
}

function Save-IconFile {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$OutputPath,
        [int[]]$Sizes
    )

    # ICO は各サイズを PNG のまま格納する（Vista 以降が対応）。System.Drawing の
    # Icon 保存はマルチサイズを扱えないためコンテナを直接組み立てる
    $blobs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $Sizes) {
        $resized = Resize-ImageHighQuality -Image $SourceImage -Width $size -Height $size
        $stream = New-Object System.IO.MemoryStream
        $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()
        $blobs.Add($stream.ToArray())
        $stream.Dispose()
    }

    $file = [System.IO.File]::Create($OutputPath)
    try {
        $writer = New-Object System.IO.BinaryWriter($file)

        # ICONDIR
        $writer.Write([uint16]0)              # 予約領域
        $writer.Write([uint16]1)              # 種別: 1 = アイコン
        $writer.Write([uint16]$Sizes.Count)

        # ICONDIRENTRY は 16 バイト固定。画像データは全エントリの直後に続く
        $offset = 6 + 16 * $Sizes.Count
        for ($i = 0; $i -lt $Sizes.Count; $i++) {
            $size = $Sizes[$i]
            # 256 は 1 バイトに収まらないため 0 で表現する
            $dimension = if ($size -ge 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)   # 幅
            $writer.Write([byte]$dimension)   # 高さ
            $writer.Write([byte]0)            # パレット数（真彩色は 0）
            $writer.Write([byte]0)            # 予約領域
            $writer.Write([uint16]1)          # プレーン数
            $writer.Write([uint16]32)         # ビット深度
            $writer.Write([uint32]$blobs[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }

        foreach ($blob in $blobs) {
            $writer.Write($blob)
        }

        $writer.Flush()
    } finally {
        $file.Dispose()
    }

    Write-Host "  生成: $(Split-Path $OutputPath -Leaf) ($($Sizes -join ', ') px)"
}

Write-Host "MSIX アセット生成を開始します"
Write-Host ""

foreach ($source in @($SourceIconDark, $SourceIconLight, $SourceSplashDark, $SourceSplashLight)) {
    if (-not (Test-Path $source)) { throw "原画が見つかりません: $source" }
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# System.Drawing の Save() は相対パスを .NET の CurrentDirectory で解決するが、
# PowerShell の Set-Location はこれを同期しない。一方で下の削除処理は PowerShell の
# 位置基準で走るため、絶対パス化しないと「削除だけ正しい場所で実行され、生成物は
# 別の場所へ書かれる（または書き込み失敗）」という破壊的な不整合になる
$OutputDir = (Resolve-Path $OutputDir).Path

# 旧世代の生成物を除去する。app.ico も本スクリプトの生成対象（#88）
$stale = Get-ChildItem -Path $OutputDir -File | Where-Object { $_.Extension -in @(".png", ".ico") }
if ($stale) {
    Write-Host "既存の生成物 $($stale.Count) 件を削除します"
    $stale | Remove-Item -Force
    Write-Host ""
}

$iconDark = [System.Drawing.Image]::FromFile((Resolve-Path $SourceIconDark))
$iconLight = [System.Drawing.Image]::FromFile((Resolve-Path $SourceIconLight))
$splashDark = [System.Drawing.Image]::FromFile((Resolve-Path $SourceSplashDark))
$splashLight = [System.Drawing.Image]::FromFile((Resolve-Path $SourceSplashLight))

Write-Host "原画:"
Write-Host "  アイコン(dark): $($iconDark.Width)x$($iconDark.Height)"
Write-Host "  アイコン(light): $($iconLight.Width)x$($iconLight.Height)"
Write-Host "  スプラッシュ(dark): $($splashDark.Width)x$($splashDark.Height)"
Write-Host "  スプラッシュ(light): $($splashLight.Width)x$($splashLight.Height)"
Write-Host ""

try {
    # 正方形ロゴ群。タイル背景は BackgroundColor 固定でテーマ非依存のため dark のみ
    foreach ($logo in @(
            @{ Name = "Square44x44Logo"; Size = 44 },
            @{ Name = "Square71x71Logo"; Size = 71 },
            @{ Name = "Square150x150Logo"; Size = 150 },
            @{ Name = "Square310x310Logo"; Size = 310 },
            @{ Name = "StoreLogo"; Size = 50 }
        )) {
        Write-Host "$($logo.Name) を生成しています"
        Save-ScaledAsset -SourceImage $iconDark -BaseName $logo.Name `
            -BaseWidth $logo.Size -BaseHeight $logo.Size -OutputDir $OutputDir
        Write-Host ""
    }

    # タスクバー・エクスプローラー用の実寸アイコン（ここだけテーマで切り替わる）
    Write-Host "Square44x44Logo の targetsize 派生を生成しています"
    foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 72, 80, 96, 256)) {
        Save-TargetSizeAsset -DarkImage $iconDark -LightImage $iconLight `
            -BaseName "Square44x44Logo" -TargetSize $size -OutputDir $OutputDir
    }
    Write-Host ""

    # 横長アセット群。Wide310x150Logo はタイル用のため dark のみ
    Write-Host "Wide310x150Logo を生成しています"
    Save-ScaledAsset -SourceImage $splashDark -BaseName "Wide310x150Logo" `
        -BaseWidth 310 -BaseHeight 150 -OutputDir $OutputDir
    Write-Host ""

    # スプラッシュはテーマごとに別名で出し、起動時にコードが選択する（#88）。
    # MRT のリソース修飾子に theme はないため命名で分けるほかない
    foreach ($splash in @(
            @{ Name = "SplashScreen"; Image = $splashDark },
            @{ Name = "SplashScreenLight"; Image = $splashLight }
        )) {
        Write-Host "$($splash.Name) を生成しています"
        Save-ScaledAsset -SourceImage $splash.Image -BaseName $splash.Name `
            -BaseWidth 620 -BaseHeight 300 -OutputDir $OutputDir
        Write-Host ""
    }

    # ウィンドウのタイトルバー・Alt+Tab 用。AppWindow.SetIcon が参照する
    Write-Host "app.ico を生成しています"
    Save-IconFile -SourceImage $iconDark -OutputPath (Join-Path $OutputDir "app.ico") -Sizes $IcoSizes
    Write-Host ""
} finally {
    $iconDark.Dispose()
    $iconLight.Dispose()
    $splashDark.Dispose()
    $splashLight.Dispose()
}

$pngCount = (Get-ChildItem -Path $OutputDir -Filter "*.png" -File | Measure-Object).Count
$icoCount = (Get-ChildItem -Path $OutputDir -Filter "*.ico" -File | Measure-Object).Count
Write-Host "生成完了: $OutputDir に PNG $pngCount 件 / ICO $icoCount 件"
