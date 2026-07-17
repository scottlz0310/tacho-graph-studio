<#
.SYNOPSIS
TachoGraphStudio をインストールする（署名証明書のインポート + .appinstaller 経由の導入）。

.DESCRIPTION
GitHub Releases の最新リリースから公開証明書（.cer）を取得して
LocalMachine\TrustedPeople ストアにインポートし、.appinstaller 経由でアプリをインストールする。
以後はアプリ起動時に新バージョンが自動チェックされる。

管理者権限が必要（証明書ストアへの書き込み）。非管理者のターミナルから実行した場合は
UAC プロンプトで昇格し、新しいウィンドウで再実行される。

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Install-TachoGraphStudio.ps1
#>
param(
    [string]$Repo = "scottlz0310/tacho-graph-studio",
    # 自己昇格で再起動されたことを示す内部用スイッチ（結果確認のためウィンドウを閉じずに待機する）
    [switch]$Elevated,
    # テスト用：テスト実行時に exit せず終了コードを出力するためのスイッチ
    [switch]$Test,
    # テスト用：管理者権限チェックを偽装するためのオブジェクト
    [object]$PrincipalOverride = $null,
    # テスト用：Start-Process の動作を偽装するためのスクリプトブロック
    [scriptblock]$StartProcessOverride = $null
)
$ErrorActionPreference = "Stop"

function Invoke-Exit($code) {
    if ($Test) {
        $code
        return
    }
    exit $code
}

if ($PrincipalOverride) {
    $principal = $PrincipalOverride
} else {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
}

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Output "管理者権限が必要なため、UAC プロンプトで昇格して再実行します..."
    try {
        if ($StartProcessOverride) {
            $proc = &$StartProcessOverride
        } else {
            $proc = Start-Process powershell.exe -Verb RunAs -ArgumentList @(
                "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", "`"$PSCommandPath`"", "-Repo", $Repo, "-Elevated"
            ) -Wait -PassThru
        }
        Invoke-Exit $proc.ExitCode
        return
    }
    catch {
        Write-Error "管理者権限への昇格に失敗しました: $_"
        Invoke-Exit 1
        return
    }
}

$exitCode = 0
try {
    $baseUrl = "https://github.com/$Repo/releases/latest/download"
    $workDir = Join-Path $env:TEMP "TachoGraphStudio-install"
    New-Item -ItemType Directory -Force $workDir | Out-Null

    Write-Output "署名証明書を取得しています..."
    $cerPath = Join-Path $workDir "TachoGraphStudio.cer"
    Invoke-WebRequest "$baseUrl/TachoGraphStudio.cer" -OutFile $cerPath

    Write-Output "証明書を LocalMachine\TrustedPeople にインポートしています..."
    $cerStore = Get-Item Cert:\LocalMachine\TrustedPeople
    if (-not $Test) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation $cerStore | Out-Null
    }

    Write-Output "アプリをインストールしています（.appinstaller 経由）..."
    # Add-AppxPackage の -AppInstallerFile はスイッチであり、.appinstaller はローカルパスを -Path に渡す必要がある
    $appInstallerPath = Join-Path $workDir "TachoGraphStudio.appinstaller"
    Invoke-WebRequest "$baseUrl/TachoGraphStudio.appinstaller" -OutFile $appInstallerPath
    if (-not $Test) {
        Add-AppxPackage -Path $appInstallerPath -AppInstallerFile
    }

    Write-Output ""
    Write-Output "インストールが完了しました。スタートメニューから TachoGraphStudio を起動できます。"
    Write-Output "新バージョンはアプリ起動時に自動チェックされます。"
}
catch {
    Write-Output "エラー: $_"
    $exitCode = 1
}
finally {
    if ($Elevated) {
        $null = Read-Host "Enter キーを押すと閉じます"
    }
}
Invoke-Exit $exitCode
