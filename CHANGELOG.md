# Changelog

このプロジェクトの特筆すべき変更はすべてこのファイルに記録する。

フォーマットは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づき、バージョニングは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に従う。

## [Unreleased]

### Added

- 要件定義書・UI モックを `docs/` に配置
- アーキテクチャ文書（`docs/architecture.md`）
- README / LICENSE (MIT) / CHANGELOG
- .NET 10 / WinUI 3 ソリューションスケルトン（App / Core / Core.Tests、Windows App SDK 2.3.1）
- CI（GitHub Actions: dotnet format / build / test）
- Renovate 設定（`github>scottlz0310/renovate-config` プリセットを拡張）
- lefthook（pre-commit で `dotnet format --verify-no-changes`）
- MSIX + `.appinstaller` 配布パイプライン（タグ push で署名済み MSIX を GitHub Releases へ発行、self-contained でランタイム同梱）
- 署名証明書生成スクリプト（`scripts/New-SigningCertificate.ps1`）・インストールスクリプト（`scripts/Install-TachoGraphStudio.ps1`）
- Supabase `machine_picklist` の読み取り専用 PostgREST クライアントとオフライン名簿キャッシュ
- シーズン・キーワード・管理番号・`is_tacho_target` による名簿フィルタとフィルタ設定のローカル永続化
- `Core.Settings`: `ISecretStore` 抽象と `SupabaseCredentials`（URL・anon キーのバリデーション付きモデル）
- App: DPAPI（`ProtectedData`, `CurrentUser` スコープ）による `ISecretStore` 実装。暗号化済みペイロードのみを `ApplicationData.LocalCacheFolder` 配下に永続化し、平文の設定ファイルには書かない
- App: Supabase 接続設定 `ContentDialog`。初回起動時（未設定時）に自動表示し、設定ボタンから再入力・変更が可能
- App: 名簿サイドバー実装前の暫定導線として、キー未設定・無効時に `InfoBar` で設定を促す（名簿以外の機能は引き続き利用可能）
- `Core.Settings`: `SupabaseCredentialsValidator`。保存前に `machine_picklist` へ実接続し、フォーマットのみでは検出できない無効な anon キー（401/403 等）を弾く
- `Core.Settings`: `ISecretStore.TryWriteAsync` 拡張メソッド。DPAPI・ディスク書き込み失敗時に例外を伝播させず呼び出し元へ通知する

### Changed

- `.appinstaller` の配置先を GitHub Releases に決定（要件定義書 §1.3・アーキテクチャ §5 に反映）
- インストールスクリプトに UAC 自己昇格を追加（非管理者ターミナルからの実行に対応）
- `AtomicJsonFile<TDocument>` を `Core.Roster` から `Core.Persistence` へ移動し public 化（App 側の `ISecretStore` 実装からも再利用するため）
