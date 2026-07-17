<#
.SYNOPSIS
TachoGraphStudio をインストールする（署名証明書のインポート + .appinstaller 経由の導入）。

.DESCRIPTION
GitHub Releases の最新リリースから公開証明書（.cer）を取得して
LocalMachine\TrustedPeople ストアにインポートし、.appinstaller 経由でアプリをインストールする。
以後はアプリ起動時に新バージョンが自動チェックされる。

管理者権限の PowerShell で実行すること（証明書ストアへの書き込みに必要）。

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Install-TachoGraphStudio.ps1
#>
param(
    [string]$Repo = "scottlz0310/tacho-graph-studio"
)
$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "管理者権限の PowerShell で実行してください（証明書のインポートに必要です）。"
}

$baseUrl = "https://github.com/$Repo/releases/latest/download"
$workDir = Join-Path $env:TEMP "TachoGraphStudio-install"
New-Item -ItemType Directory -Force $workDir | Out-Null

Write-Output "署名証明書を取得しています..."
$cerPath = Join-Path $workDir "TachoGraphStudio.cer"
Invoke-WebRequest "$baseUrl/TachoGraphStudio.cer" -OutFile $cerPath

Write-Output "証明書を LocalMachine\TrustedPeople にインポートしています..."
Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

Write-Output "アプリをインストールしています（.appinstaller 経由）..."
# Add-AppxPackage の -AppInstallerFile はスイッチであり、.appinstaller はローカルパスを -Path に渡す必要がある
$appInstallerPath = Join-Path $workDir "TachoGraphStudio.appinstaller"
Invoke-WebRequest "$baseUrl/TachoGraphStudio.appinstaller" -OutFile $appInstallerPath
Add-AppxPackage -Path $appInstallerPath -AppInstallerFile

Write-Output ""
Write-Output "インストールが完了しました。スタートメニューから TachoGraphStudio を起動できます。"
Write-Output "新バージョンはアプリ起動時に自動チェックされます。"
