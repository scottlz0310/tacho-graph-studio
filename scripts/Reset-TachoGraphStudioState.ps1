<#
.SYNOPSIS
TachoGraphStudio のローカル状態（設定・テンプレート・資格情報・キャッシュ）を削除する（開発者向け）。

.DESCRIPTION
MSIX パッケージのアプリデータフォルダから、指定したスコープのフォルダを削除する。
削除した範囲はアプリの次回起動時に初期状態で作り直される。

削除対象:
  Settings   LocalState\settings   出力先・処理対象日・様式選択・サイドバー幅・
                                   ウィンドウ配置・PNG DPI・変更点表示済みバージョン・名簿フィルタ
  Templates  LocalState\templates  チャート紙様式テンプレート（自作分も消える）
  Secrets    LocalCache\secrets    Supabase 接続設定（DPAPI 暗号化）
  Cache      LocalCache\roster     名簿・業者マスタのオフラインキャッシュ

管理者権限は不要。アプリ起動中は実行できない（終了時の自動保存で削除内容が書き戻されるため）。

.EXAMPLE
pwsh -File .\Reset-TachoGraphStudioState.ps1 -WhatIf

.EXAMPLE
pwsh -File .\Reset-TachoGraphStudioState.ps1 -Scope Secrets, Cache
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [ValidateSet("All", "Settings", "Templates", "Secrets", "Cache")]
    [string[]]$Scope = @("All"),
    [string]$PackageName = "TachoGraphStudio",
    # 省略時は $PackagesRoot 配下の <PackageName>_<publisher hash> フォルダから解決する
    [string]$PackageRoot,
    [string]$PackagesRoot = (Join-Path $env:LOCALAPPDATA "Packages"),
    # テスト用：exit の代わりに終了コードをパイプラインへ出力する（進捗表示は Write-Host のため混入しない）
    [switch]$Test,
    # テスト用：起動中プロセスの取得を差し替える
    [scriptblock]$GetProcessOverride = $null
)
$ErrorActionPreference = "Stop"

function Invoke-Exit($code) {
    if ($Test) {
        $code
        return
    }
    exit $code
}

$targetsByScope = [ordered]@{
    Settings  = "LocalState\settings"
    Templates = "LocalState\templates"
    Secrets   = "LocalCache\secrets"
    Cache     = "LocalCache\roster"
}

if ($Scope -contains "All") {
    $selected = @($targetsByScope.Keys)
} else {
    $selected = @($Scope | Select-Object -Unique)
}

if (-not $PackageRoot) {
    # Appx モジュールは使わずフォルダ名から解決する。削除対象は LocalState / LocalCache であり、
    # 判断基準はパッケージの登録状態ではなくアプリデータフォルダの実在であるため。
    # Get-AppxPackage は暗黙のモジュール読み込みが -WhatIf を拾って出力を埋める副作用もある
    $candidates = @(
        Get-ChildItem -Path $PackagesRoot -Directory -Filter "${PackageName}_*" -ErrorAction SilentlyContinue
    )

    if ($candidates.Count -eq 0) {
        Write-Host "$PackageName のアプリデータフォルダが $PackagesRoot に見つかりません。未インストールの可能性があります。"
        Invoke-Exit 1
        return
    }

    if ($candidates.Count -gt 1) {
        Write-Host "$PackageName のアプリデータフォルダが複数見つかりました。-PackageRoot で対象を指定してください:"
        $candidates | ForEach-Object { Write-Host "  $($_.FullName)" }
        Invoke-Exit 1
        return
    }

    $PackageRoot = $candidates[0].FullName
}

if (-not (Test-Path -LiteralPath $PackageRoot)) {
    Write-Host "アプリデータフォルダが見つかりません: $PackageRoot"
    Invoke-Exit 1
    return
}

if ($GetProcessOverride) {
    $running = & $GetProcessOverride
} else {
    $running = Get-Process -Name $PackageName -ErrorAction SilentlyContinue
}

if ($running) {
    Write-Host "$PackageName が起動中です。アプリを終了してから再実行してください（起動中に削除すると終了時の自動保存で書き戻されます）。"
    Invoke-Exit 1
    return
}

$removed = 0
foreach ($name in $selected) {
    $path = Join-Path $PackageRoot $targetsByScope[$name]
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "スキップ（存在しません）: $path"
        continue
    }

    if ($PSCmdlet.ShouldProcess($path, "削除")) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "削除しました: $path"
        $removed++
    }
}

Write-Host "完了: $removed 個のフォルダを削除しました。アプリを起動すると初期状態で作り直されます。"
Invoke-Exit 0
