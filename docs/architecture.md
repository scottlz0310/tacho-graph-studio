# tacho-graph-studio アーキテクチャ

- 作成日: 2026-07-18
- ステータス: ドラフト（Phase 1 スケルトン配置の指針。実装の進行に合わせて更新する）
- 前提: [要件定義書](./tacho-graph-studio-requirements.md) を正とする。技術選定の根拠は要件定義書 §1.3・§6 を参照

---

## 1. 全体構成

単一の WinUI 3 デスクトップアプリ。UI 層（App）とドメイン層（Core）を分離し、ドメイン層は UI 非依存のクラスライブラリとして DI 経由でテスト可能にする（要件定義書 §6 テスト方針）。

```
┌─────────────────────────────────────────────┐
│ TachoGraphStudio.App (WinUI 3, MSIX)        │
│  View (XAML) ── ViewModel (MVVM Toolkit)    │
│  DI コンテナ構成 / 設定 UI / 秘匿ストア実装      │
└──────────────────┬──────────────────────────┘
                   │ 参照（一方向）
┌──────────────────▼──────────────────────────┐
│ TachoGraphStudio.Core (クラスライブラリ)       │
│  Imaging / Roster / Templates / Naming /    │
│  Settings（抽象）                            │
└─────────────────────────────────────────────┘
```

- App → Core の一方向参照のみ。Core は Windows App SDK・WinUI に依存しない
- 外部リソース（ネットワーク・ファイルシステム・資格情報ストア）は Core 側でインターフェース化し、実装の注入またはモック差し替えを可能にする

## 2. プロジェクト構成

```
tacho-graph-studio/
├── docs/                              # 要件定義・アーキテクチャ・UI モック
├── src/
│   ├── TachoGraphStudio.App/          # WinUI 3（single-project MSIX）
│   └── TachoGraphStudio.Core/         # ドメインロジック（UI 非依存）
├── tests/
│   └── TachoGraphStudio.Core.Tests/   # xUnit（パラメータ化テスト基本）
└── TachoGraphStudio.sln
```

| プロジェクト | 種別 | 責務 | 主要依存 |
|---|---|---|---|
| TachoGraphStudio.App | WinUI 3 / MSIX packaged | View・ViewModel、DI 構成、設定 UI、秘匿ストア実装 | Windows App SDK, CommunityToolkit.Mvvm |
| TachoGraphStudio.Core | クラスライブラリ | 画像処理パイプライン、名簿取得・キャッシュ、テンプレート、出力命名、設定モデル | OpenCvSharp |
| TachoGraphStudio.Core.Tests | xUnit | Core のユニットテスト | xUnit |

小規模アプリのためプロジェクト分割は最小限とし、Core 内は名前空間で責務を分ける：

| 名前空間 | 責務 | 対応要件 |
|---|---|---|
| `Core.Imaging` | シート読込・円盤分割・背景除去・回転・文字入れ合成・PNG 出力 | FR-01〜08, FR-18〜19 |
| `Core.Roster` | Supabase（PostgREST）読み取りクライアント・オフラインキャッシュ・フィルタ | FR-09〜12 |
| `Core.Templates` | チャート紙様式テンプレートの定義・シリアライズ・旧 GIMP 版 JSON インポート | FR-16, FR-24〜26 |
| `Core.Naming` | 出力ファイル命名（`YYYYMMDD_登録番号_運転者.png`、手書きスキップ対応） | FR-17, FR-20 |
| `Core.Settings` | 設定モデルと永続化抽象（`ISecretStore` 等） | FR-22〜23 |

## 3. 主要コンポーネント設計

### 3.1 画像処理パイプライン（Core.Imaging）

段階処理として構成し、各段階はパラメータ（しきい値・パディング等）を引数で受けるステートレスなサービスとする：

```
Load(PDF/JPEG) → Split(円盤検出・分割) → RemoveBackground(楕円フィット・アルファ化)
→ [プレビュー: 回転・文字入れは非破壊レイヤーとして UI 側で合成]
→ Export(回転・文字入れを本合成した透過 PNG)
```

- 中間成果物は円盤単位のワークアイテムとして保持し、サムネイルナビ（No.1〜6）・処理ステータス（未処理/処理済み/スキップ）に対応させる
- プレビューのリアルタイム追従（FR-07, FR-18）は UI 側の合成で実現し、確定保存時のみ Core でフル解像度の本合成を行う。600dpi 級での実用速度（NFR-03）はこの分離で担保する
- PDF 読込は `Windows.Data.Pdf`（OS 標準）を第一選択とする。WinRT API のため読込アダプタは App 側実装とし、Core は画像バイト列を受ける

### 3.2 名簿連携（Core.Roster）

- `IRosterClient`: `machine_picklist` ビューへの PostgREST GET のみ（読み取り専用、書き込み API は持たない）。`HttpClient` を注入しテストでモック可能にする
- `RosterCache`: 最終取得分をローカル JSON にキャッシュし、オフライン時はキャッシュで動作（FR-10 / NFR-04）
- フィルタ（シーズン・キーワード・管理番号ジャンプ・`is_tacho_target` デフォルト絞り込み）は machinery-report-system の `MachineSelectDialog` のロジックを Core 側に移植する（FR-11〜12）

### 3.3 設定・秘匿情報（Core.Settings + App）

- 一般設定（出力先・前回日付・テンプレート選択・フィルタ状態・ウィンドウ状態）: `ApplicationData` 配下の JSON
- Supabase URL・anon キー: Core は `ISecretStore` 抽象のみを持ち、実装は App 側で `PasswordVault` または DPAPI により暗号化保存（FR-23）。平文の設定ファイル・リポジトリ・MSIX には含めない
- キー未設定・無効時は名簿パネルに設定導線を出し、名簿以外の機能は動作継続する（FR-23）

### 3.4 テンプレート（Core.Templates）

- 現行 GIMP 版 JSON フォーマットの継承を第一候補とし、評価の結果再設計する場合は旧形式インポーターを設ける（FR-25〜26）。判断は Phase 4 開始前（要件定義書 §9）
- GUI 編集 UI（FR-24）は App 側。Core はテンプレートの検証・シリアライズ・文字入れ座標計算を担う

## 4. データフロー（逐次処理ワークフロー）

```
シート投入（PDF/JPEG、複数可）
  → 自動分割 → サムネイルナビに円盤を展開
  → [円盤ごとに] 背景除去 → 回転補正（十字ガイド基準）
  → 名簿行選択 → 登録番号・運転者を自動反映（手修正可）
  → ファイル名プレビュー確認 → 確定保存して次へ（自動遷移）
```

処理対象日・チャート紙様式・手書きスキップはトップバーで一括指定し、全円盤に適用される（FR-14, FR-16〜17）。

## 5. 配布・更新

- **MSIX + `.appinstaller`**（NFR-02b）。single-project MSIX とし、CI で自己署名証明書により署名する
- `.appinstaller` の配置先は **GitHub Releases**。CI のリリースジョブで MSIX と `.appinstaller` を Releases へ発行し、`.appinstaller` 内の更新 URL は `releases/latest/download/…` の固定 URL を指す
- 証明書インポートを含むセットアップスクリプト（PowerShell）を同梱する

## 6. CI / 品質ゲート

- GitHub Actions（windows-latest）:
  1. `dotnet format --verify-no-changes`
  2. `dotnet build`
  3. `dotnet test`（Core.Tests）
  4. リリース時のみ MSIX パッケージング・署名・発行
- `.editorconfig` + Roslyn analyzers
- Renovate: `github>scottlz0310/renovate-config` を拡張（`pnpm dlx @scottlz0310/renovate-config-init` で初期化）

## 7. 本書のスコープ外

- machinery-report-system 側の変更（`is_tacho_target` マイグレーション・タスクペイン拡張）— 同リポジトリで管理
- 画像処理アルゴリズムの詳細（しきい値・楕円フィットのパラメータ設計）— 実装時に GIMP 版の実績値を起点に調整
