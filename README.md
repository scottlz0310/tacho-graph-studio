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

## 開発

必要環境: Windows 11 + .NET 10 SDK

```powershell
dotnet build TachoGraphStudio.slnx -c Release -p:Platform=x64   # ビルド
dotnet test tests/TachoGraphStudio.Core.Tests                   # テスト
dotnet format TachoGraphStudio.slnx --verify-no-changes         # フォーマット検証
```

MSIX パッケージング・セットアップスクリプトは Phase 1 の残タスクとして追って整備する。

## License

[MIT](LICENSE)
