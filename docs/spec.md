# Specification

> Alpheratz の機能仕様書。アーキテクチャ・モジュール構成・データフロー・状態管理・性能特性をリファレンス形式で記述する。

## Table of Contents

- [Overview](#overview)
- [Glossary](#glossary)
- [Architecture](#architecture)
- [Module Organization](#module-organization)
- [Feature Specifications](#feature-specifications)
  - [Gallery](#gallery)
  - [Photo Modal](#photo-modal)
  - [Tag Master](#tag-master)
  - [Template](#template)
  - [World Resolve](#world-resolve)
  - [Settings](#settings)
  - [Bootstrap](#bootstrap)
- [Data Flow](#data-flow)
- [State Management](#state-management)
- [Concurrency Model](#concurrency-model)
- [Performance Characteristics](#performance-characteristics)
- [Security Model](#security-model)
- [Persistence](#persistence)
- [Known Limitations](#known-limitations)

---

## Overview

Alpheratz はローカル写真フォルダをスキャンし、写真メタデータ、タグ、ワールド名、PDQ ハッシュを SQLite に保管する Windows デスクトップアプリケーションである。

### Goals

- 写真を日付・ワールド・タグ・お気に入りで検索可能にする
- 標準グリッドとメイソンリーレイアウトで写真を閲覧する
- PDQ ハッシュで類似写真と世界不明写真の候補を検出する
- Polaris archive の VRChat ログから撮影時刻に対応するワールド名を補完する
- ローカル完結で動作し、外部サーバ依存を持たない

### Non-Goals

- 写真ファイル自体のクラウド同期
- VRChat 生ログの常時監視
- 複数ユーザー・サーバ運用
- macOS / Linux 対応

---

## Glossary

| 用語 | 定義 |
| --- | --- |
| **写真** | スキャン対象の画像ファイル。`photo_path` を主識別子とする |
| **1st / 2nd 写真フォルダ** | 設定画面で指定する 2 系統のスキャン元 |
| **ワールド不明写真** | `world_name` が未設定または空の写真 |
| **PDQ** | 知覚ハッシュ。類似画像検出と世界名候補の算出に使う |
| **Polaris archive** | Polaris が保持する VRChat ログ archive。ワールド訪問履歴の補完に使う |
| **StellaRecord** | 姉妹アプリ。ランチャーへ Alpheratz を登録できる |

---

## Architecture

### Layered View

```
┌──────────────────────────────────────────────────────────────┐
│ Presentation Layer (WinUI 3 + XAML)                          │
│  - Features/*: Page, ViewModel, UI logic                      │
├──────────────────────────────────────────────────────────────┤
│ Application Layer                                             │
│  - Services: PhotoService / WorldService / SettingsService    │
│  - LocalEventBus: scan / PDQ / thumbnail progress             │
├──────────────────────────────────────────────────────────────┤
│ Core Layer                                                    │
│  - AlpheratzDb / PhotoScanner / ThumbnailService / PDQ        │
│  - AppPaths / AppConfig / AppLogger                           │
├──────────────────────────────────────────────────────────────┤
│ Storage Layer                                                 │
│  - SQLite / setting.json / thumbnails / logs / registry       │
└──────────────────────────────────────────────────────────────┘
```

### Process Model

| Thread | Owner | Lifetime |
| --- | --- | --- |
| UI thread | WinUI 3 dispatcher | プロセス全寿命 |
| Scan worker | `PhotoScanner.ScanAsync` | スキャン 1 回ごと |
| Thumbnail worker | `ThumbnailWorker` | 表示要求ごと |
| PDQ worker | `PhashService` | 未計算写真の解析中 |

Tauri / Rust IPC は使用しない。UI とバックグラウンド処理は同一 .NET プロセス内で動作する。

---

## Module Organization

### Application (`app/`)

```
app/
├── App.xaml.cs                 DI 登録、起動初期化
├── Features/
│   ├── Bootstrap/              起動状態表示
│   ├── Shell/                  メインシェル、ヘッダー、モーダル階層
│   ├── Gallery/                写真一覧、フィルタ、グリッド、メイソンリー
│   ├── PhotoModal/             写真詳細
│   ├── Settings/               設定、タグ、テンプレート、連携
│   ├── TagMaster/              タグ管理 ViewModel
│   ├── Template/               投稿テンプレート ViewModel
│   └── WorldResolve/           世界不明写真の候補提示
├── Core/
│   ├── Database/               SQLite schema and queries
│   ├── Scanner/                写真列挙、メタデータ抽出、Polaris ログ読取
│   ├── Imaging/                サムネイル、PDQ
│   ├── AppConfig.cs            setting.json 読み書き
│   └── AppPaths.cs             データパス、レジストリ参照
├── Services/                   画面横断の操作サービス
├── Models/                     DTO / UI model
├── Shared/                     共通 UI、サービス、コンバーター
└── Themes/                     XAML resources
```

### Tests (`tests/Alpheratz.Tests/`)

xUnit で DB、スキャナ、PDQ、ViewModel、UI ロジック、外部連携の振る舞いを特性化する。

---

## Feature Specifications

### Gallery

**Purpose**: 写真一覧を表示し、検索・絞り込み・選択・サムネイル生成を統括する。

#### Components

- `GalleryPage.xaml(.cs)` — 一覧 UI
- `GalleryViewModel` — フィルタ、選択、グループ表示の状態
- `GalleryPhotosState` — DB 取得と `PhotoThumbnailItem` 更新
- `GalleryMasonryView` / `PhotoGridItemsView` — 表示モード別コントロール

#### Behavior

- 1st / 2nd 写真フォルダを `source_slot` で区別する。
- 表示対象の写真だけサムネイル生成を要求する。
- 欠損ファイルは `is_missing` で管理し、再スキャン時に消さず状態を更新する。
- グループドリルダウンは中位モーダルとして Shell の `ModalContent` に表示する。

### Photo Modal

**Purpose**: 選択写真の詳細情報と編集操作を提供する。

#### Components

- `PhotoModalPage.xaml(.cs)`
- `PhotoModalViewModel`
- `PhotoModalState`

#### Behavior

- 写真、タグ、お気に入り、ワールド名、類似候補を表示する。
- PDQ 距離から類似写真を検索する。
- 編集後は `PhotoThumbnailItem` を更新し、一覧側の表示と同期する。

### Tag Master

**Purpose**: 写真へ付与するタグのマスタを管理する。

#### Components

- `TagMasterViewModel`
- Settings 内のタグセクション

#### Behavior

- `tags` と `photo_tags` を使い、タグ名を一意に管理する。
- タグ削除時は写真との関連も削除する。

### Template

**Purpose**: 投稿用テンプレートを保存・編集する。

#### Components

- `TemplatePageViewModel`
- Settings 内のテンプレートセクション

#### Behavior

- テンプレート配列と選択中テンプレートは `setting.json` に保存する。
- 編集中のテンプレートと保存済みテンプレートを分離して扱う。

### World Resolve

**Purpose**: 世界不明写真に対し、Polaris archive または PDQ 類似度から候補を提示する。

#### Components

- `WorldResolvePage.xaml(.cs)`
- `WorldResolveViewModel`
- `WorldService`
- `PhotoScanner.ResolveUnknownWorldsFromArchiveAsync`

#### Behavior

- Polaris archive が存在する場合、`output_log_*.txt` から入退室時刻とワールド名を読み込む。
- 撮影時刻が訪問区間に一致する写真へ `match_source = polaris_archive` を付与してワールド名を保存する。
- PDQ 解決は自動確定せず、WorldResolve UI の確認操作を唯一の確定経路にする。
- WorldResolve UI は PDQ 候補探索中に対象写真数ベースの進捗を表示する。

### Settings

**Purpose**: アプリ設定、タグ、テンプレート、外部連携を管理する。

#### Components

- `SettingsPage.xaml(.cs)`
- `SettingsViewModel`
- `SettingsCompositeViewModel`
- `SettingsService`

#### Behavior

- 写真フォルダ、2nd 写真フォルダ、テーマ、表示モード、起動設定、テンプレートを保存する。
- StellaRecord が利用可能な場合、`apps` テーブルへ `Alpheratz` を登録する。説明は「VRChat写真ギャラリー化・ワールドリンク展開サポートアプリ」。
- Settings は Shell の中位モーダルとして表示する。

### Bootstrap

**Purpose**: アプリ起動時の初期化状態を表示する。

#### Components

- `BootstrapPage`
- `AppLifecycleService`

#### Behavior

- DB 初期化、設定読込、スキャン準備などの状態を表示する。
- 初期化完了後に Shell へ遷移する。

---

## Data Flow

### Startup

1. `App.xaml.cs` が DI コンテナを構築する。
2. `AlpheratzDb.Initialize` がスキーマを作成・補完する。
3. `SettingsService.GetSettingAsync` が `setting.json` を読み込む。
4. Shell が Gallery と Settings 用 ViewModel を初期化する。

### Photo Scan

1. `PhotoScanner` が設定フォルダを列挙する。
2. 画像ファイルから撮影時刻、ワールド ID、ワールド名を抽出する。
3. `AlpheratzDb` が写真メタデータを upsert する。
4. 存在しなくなった写真は `is_missing = 1` に更新する。
5. orientation / 画像サイズ / PDQ / サムネイルをバックグラウンドで補完する。

### World Resolve

1. Polaris archive から訪問履歴を読み、`archive_world_visits` へ投入する。
2. 未解決写真の撮影時刻で訪問履歴を検索する。
3. 一致した写真の `world_name` と `match_source` を更新する。
4. PDQ 候補は UI で確認後に保存する。

---

## State Management

### State Hierarchy

| Scope | Owner |
| --- | --- |
| アプリ全体の初期化状態 | `AppLifecycleService` |
| Shell 状態、モーダル階層、ヘッダー操作 | `ShellViewModel`, `ShellPage` |
| 写真一覧、フィルタ、選択 | `GalleryViewModel`, `GalleryPhotosState` |
| 写真詳細 | `PhotoModalViewModel`, `PhotoModalState` |
| 設定、タグ、テンプレート | `SettingsViewModel`, `TagMasterViewModel`, `TemplatePageViewModel` |

### Persistence

| State | Persistence |
| --- | --- |
| 写真メタデータ、タグ、ワールド解決履歴 | SQLite |
| 写真フォルダ、テーマ、表示モード、テンプレート | `setting.json` |
| サムネイル | `Data/cache/*/imgCache` |

---

## Concurrency Model

### Shared State

- 設定保存は `SettingsService` の lock で直列化する。
- スキャンはキャンセルトークンで中断を受け付ける。
- UI 更新は WinUI dispatcher 経由で行う。

### Write Exclusion

SQLite は接続ごとに WAL / NORMAL / FK ON を設定する。複数行更新は `AlpheratzDb` 内でトランザクション化する。

### Read Concurrency

WAL により、スキャン書き込み中も UI の SELECT がブロックされにくい。

---

## Performance Characteristics

| Area | Characteristic |
| --- | --- |
| Gallery | 表示対象のサムネイルだけを要求し、一覧全体の画像読み込みを避ける |
| Scan | 既存 DB 情報を先に読み、再スキャン時の不要更新を抑制する。更新対象は小さなバッチにまとめて SQLite へ書き込む |
| PDQ | 未計算写真のみ対象にし、`phash_version` で再計算条件を管理する |
| Archive logs | 1 行の上限を 64 KiB に制限し、巨大行でメモリを使い切らない |

---

## Security Model

### Threat Model

- ローカル画像ファイル、Polaris archive、StellaRecord DB は同一ユーザーが読み取り可能なファイルとして扱う。
- ネットワーク越しの入力は扱わない。

### Mitigations

| Threat | Mitigation |
| --- | --- |
| SQL インジェクション | 値はパラメータバインドで渡す |
| シンボリックリンク等による再帰ループ | 訪問済み正規化パスでディレクトリ列挙を制御する |
| 巨大ログ行 | `MaxLogLineLength` で 1 行長を制限する |
| 外部連携先不在 | Polaris / StellaRecord が見つからない場合は連携だけをスキップする |

### Out-of-Scope

- 写真ファイルの暗号化
- OS ユーザー権限を超えるアクセス制御
- ネットワーク連携

---

## Persistence

### Filesystem Layout

| Path | Description |
| --- | --- |
| `<install>\Data\db\Alpheratz.db` | メイン DB |
| `<install>\Data\db\setting.json` | 設定 |
| `<install>\Data\cache\1st-cache\imgCache` | 1st 写真フォルダ用サムネイル |
| `<install>\Data\cache\2nd-cache\imgCache` | 2nd 写真フォルダ用サムネイル |
| `<install>\Data\logs` | アプリログ |

### Windows Registry

| Key | Value Name | Description |
| --- | --- | --- |
| `HKCU\Software\CosmoArtsStore\Alpheratz` | `InstallLocation` | installer が書き込むインストール先 |
| `HKCU\Software\CosmoArtsStore\Alpheratz` | `RuntimeLocation` | WinUI 本体配置先 |
| `HKCU\Software\CosmoArtsStore\Polaris` | `InstallLocation` | Polaris archive 解決時に参照 |
| `HKCU\Software\CosmoArtsStore\StellaRecord` | `InstallLocation` | StellaRecord 登録時に `Data\db\stellarecord.db` を優先参照 |
| `HKCU\Software\CosmoArtsStore\StellaRecord` | `DbPath` | `InstallLocation` から DB を解決できない場合の fallback |

### LocalStorage

該当なし。

---

## Known Limitations

| Limitation | Impact | Note |
| --- | --- | --- |
| Windows のみ対応 | 他 OS で動作不可 | WinUI 3 / Windows App SDK 前提 |
| コード署名なし | SmartScreen 警告が出る場合がある | 商用配布時に対応予定 |
| Polaris archive 依存の世界名補完 | Polaris 未導入環境では使用不可 | PDQ 解決と手動編集は利用可能 |
| StellaRecord 登録 | StellaRecord 未導入環境では使用不可 | Alpheratz 本体機能には影響しない |
