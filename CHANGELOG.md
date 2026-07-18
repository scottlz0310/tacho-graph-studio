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
- App: 名簿サイドバー UI（`WinUI.TableView` によるグリッド、シーズン・タコ対象フィルタ、キーワード検索、管理番号ジャンプ）。行クリック選択・選択行ハイライトに対応(UR-01, UR-02)
- App: `RosterViewModel`（`CommunityToolkit.Mvvm`）。名簿の取得・フィルタ適用・選択状態・読み込み中/エラー/未接続の表示状態を管理し、シーズン・タコ対象フィルタの変更をローカル永続化と連動させる
- `Core.Imaging`: シート読込（FR-01, FR-02）。`SheetLoader` が JPEG / PDF（複数ページ）/ 複数ファイルのバッチ投入を `IAsyncEnumerable<SheetImage>` で逐次供給する。Core はエンコード済み画像バイト列のみを扱い、デコードは円盤分割（issue #8, OpenCvSharp）に委ねる
- App: `WindowsPdfRasterizer`。`Windows.Data.Pdf`（OS 標準）による `IPdfRasterizer` 実装。既定 600dpi でページをラスタライズする（NFR-03）
- `Core.Imaging`: 円盤の自動検出・分割（FR-03）。`SheetSplitter` が GIMP 版 `split_by_auto_detect` の実績値（threshold=15・padding=20px・円盤径 123.5mm・最小サイズ=径の 2/3・解析長辺 1200px）を移植し、しきい値・パディング・最大枚数・DPI を `DiscSplitOptions` で調整可能にする。OpenCvSharp4 はこの変更で導入
- `Core.Imaging`: 充填率フィルタ（`MinFillRatio`、既定 0.4）。スキャナ縁の黒帯がページを一周し bbox がシート全体になる誤検出（実スキャンで fill=0.013、実円盤は 0.77 前後）を除外する
- `Core.Imaging`: 背景除去（FR-05）。`BackgroundRemover` が前景輪郭への楕円フィット（`Cv2.FitEllipse`）で白地背景を除去し BGRA へアルファ化、楕円 bbox へクロップする。しきい値・楕円パディングは `BackgroundRemovalOptions` で調整可能。フィット楕円（`RotatedRect`）は回転補正の十字ガイド基準（FR-06）用に結果へ公開する
- App: ステージ UI 第 1 弾（FR-04）。「シート取込」ボタン（`FileOpenPicker`、PDF/JPEG 複数選択）から読込→分割→背景除去のパイプライン（`StagePipeline`）を通し、円盤をサムネイルナビ（No.1〜・未処理/処理済み/スキップ表示）へ逐次展開、選択円盤を透過プレビュー表示する。ズーム・十字ガイド・回転補正は第 2 弾で実装
- App: 名簿サイドバーの幅をスプリッタで可変化（#25）。サイドバーとステージの間に `GridSplitter`（CommunityToolkit.WinUI.Controls.Sizers — キーボード操作・AutomationPeer 対応）を配置し、幅 240〜720px の範囲で調整できる。永続化はウィンドウ状態と合わせて #15 で対応
- App: ステージ UI 第 2 弾（FR-06〜08）。`DiscPreviewControl` に画像と独立した十字ガイド、`RotateTransform` による非破壊のリアルタイム回転、ビューポート中心を保つズームイン/アウトを実装。回転補正はスライダー（-180°〜+180°）・数値入力・リセットで操作し、角度は円盤ごとに保持。全画面拡大はウィンドウ全面のオーバーレイで同一プレビューを表示する

- `Core.Templates`: チャート紙様式テンプレート（FR-16, FR-25）。GIMP 版 JSON フォーマットの**継承を決定**（#11・要件定義書 未決事項 #2）し、互換モデル（`ChartTemplate`、snake_case・欠落キーは旧実装と同じ既定値）・検証付きシリアライザ（round-trip）・文字入れ座標計算（`CalculatePlacement`: 位置=画像サイズ比、フォントサイズ=短辺比）を実装。実運用テンプレート（Task-Meter / Yazaki45）を fixture に互換性を回帰テストで固定。インポーター（FR-26）は不要と判断
- `Core.Templates`: テンプレートの永続化（FR-24 第 1 弾）。`FileTemplateStore` が 1 テンプレート = 1 JSON ファイル（GIMP 互換フォーマット）をディレクトリで管理する。原子的書き込み（一時ファイル + Move）、一部ファイル破損時も残りを利用できる読込結果（`TemplateStoreListResult`）、テンプレート名からのファイル名生成（サニタイズ・重複回避）
- App: `TemplateEditorViewModel`（FR-24 第 1 弾）。テンプレートの一覧・新規作成（旧 GIMP 版標準フィールドキーをシード）・複製・削除・保存（dirty 管理・フィールド名重複検証）・旧テンプレート JSON の取り込み、フィールドの追加/削除/編集（位置・フォント・色・整列・表示/必須。NumberBox の NaN・範囲外値は状態境界でサニタイズ）を管理する。編集 UI（全画面オーバーレイ・ドラッグ位置調整）は第 2 弾で実装
- App: テンプレート編集 UI（FR-24 第 2 弾）。トップバーの「テンプレート」ボタンから開く全画面オーバーレイ `TemplateEditorOverlay`。テンプレート一覧（新規・複製・削除・未保存マーカー）、プレビューキャンバス（フィールドラベルを比率座標に描画し**ドラッグで位置調整**。背景はステージ選択中の円盤またはプレースホルダー円、参考サイズのアスペクト比でフィット）、フィールドプロパティパネル（位置・フォント・サイズ比・色・太字/斜体・水平/垂直基準・表示/必須）、旧 GIMP 版テンプレート JSON のインポート（`FileOpenPicker`）、未保存変更の破棄確認・削除確認ダイアログを実装
- `Core.Templates`: `ChartTextComposer`（FR-16, FR-18）。テンプレートのフィールド名（date_year/date_month/date_day/vehicle_no/driver/vehicle_type）と入力値（`ChartTextValues`）の対応付け + 配置計算。日付は「2026/12/25」形式の文字列を区切り文字で分解し、和暦等の非数値表記も通す。プレビューの文字レイヤー（#13）と確定保存の本合成（#14）が共有する
- App: 文字入れメタデータ（FR-13〜15, FR-17 第 1 弾）。`DiscMetadata`（印字日付・登録番号・運転者名・車種・手書きスキップ）を円盤ごとに保持し、名簿の行選択で選択中円盤へ自動反映（FR-13）、処理対象日 `TargetDate` の変更で全円盤の印字日付へ一括同期（FR-14）、個別の手修正は次の一括指定まで保持（FR-15）。`StageViewModel` にチャート紙様式の選択（`FileTemplateStore` から読込、FR-16）を追加。トップバー・メタデータエディタ・プレビュー文字レイヤーの UI は第 2 弾で実装

### Changed

- `.appinstaller` の配置先を GitHub Releases に決定（要件定義書 §1.3・アーキテクチャ §5 に反映）
- インストールスクリプトに UAC 自己昇格を追加（非管理者ターミナルからの実行に対応）
- `AtomicJsonFile<TDocument>` を `Core.Roster` から `Core.Persistence` へ移動し public 化（App 側の `ISecretStore` 実装からも再利用するため）
- App: Supabase 未設定・無効時の導線をトップレベル `InfoBar` から名簿サイドバー内の表示に統合(アーキテクチャ §3.3 の方針どおり、名簿パネル実装後は同パネル内の導線に一本化)
