# tacho-graph-studio

タコグラフチャート画像処理のための Windows デスクトップアプリ **TachoGraphStudio**。

A3 スキャン画像（PDF/JPEG）から円盤チャートを自動分割し、背景除去・回転補正・名簿連携による文字入れを単一画面で行い、透過 PNG として保存する。[TachoGraphWizard](https://github.com/scottlz0310/TachoGraphWizard)（GIMP 3 プラグイン）の後継。

> **Status**: 開発初期（Phase 1: プロジェクト骨格）。動作するリリースはまだありません。

## ドキュメント

- [要件定義書](docs/tacho-graph-studio-requirements.md)
- [アーキテクチャ](docs/architecture.md)
- [UI モック](docs/ui-mock.html)

## 技術スタック

- C# / .NET 10 (LTS) + WinUI 3（Windows App SDK）
- OpenCvSharp（画像処理）
- Supabase（名簿の読み取り専用連携）
- MSIX + `.appinstaller`（配布・自動更新）

## インストール

自己署名証明書を使用しているため、初回インストール時に証明書のインポートが必要（管理者権限）。

1. [最新リリース](https://github.com/scottlz0310/tacho-graph-studio/releases/latest)から `Install-TachoGraphStudio.ps1` をダウンロード
2. 内容を確認の上、管理者 PowerShell で実行:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-TachoGraphStudio.ps1
   ```

   署名証明書を `LocalMachine\TrustedPeople` にインポートし、`.appinstaller` 経由でアプリをインストールする。以後、新バージョンはアプリ起動時に自動チェックされる。

## 開発

必要環境: Windows 11 + .NET 10 SDK

```powershell
dotnet build TachoGraphStudio.slnx -c Release -p:Platform=x64   # ビルド
dotnet test tests/TachoGraphStudio.Core.Tests                   # テスト
dotnet format TachoGraphStudio.slnx --verify-no-changes         # フォーマット検証
```

### リリース

1. 初回のみ: `scripts/New-SigningCertificate.ps1` で署名証明書を生成し、出力される値を GitHub Secrets（`SIGNING_CERTIFICATE_BASE64` / `SIGNING_CERTIFICATE_PASSWORD`）に登録する
2. CHANGELOG を更新し、`v<major>.<minor>.<patch>` タグを push する。Release ワークフローが署名済み MSIX・`.appinstaller`・公開証明書・インストールスクリプトを GitHub Releases に発行する

## License

[MIT](LICENSE)
