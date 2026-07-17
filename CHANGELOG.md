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

### Changed

- `.appinstaller` の配置先を GitHub Releases に決定（要件定義書 §1.3・アーキテクチャ §5 に反映）
