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
- PDQ ハッシュで類似写真とワールド名不明写真の候補を検出する
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
│  - LocalEventBus: scan / PDQ progress and completion          │
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
| Thumbnail worker | `ThumbnailWorker` | アプリ全体（各生成要求を追跡） |
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
│   ├── Settings/               設定、タグ、テンプレート、クレジット
│   ├── TagMaster/              タグ管理 ViewModel
│   ├── Template/               投稿テンプレート ViewModel
│   └── WorldResolve/           ワールド名不明写真の候補提示
├── Core/
│   ├── Database/               SQLite schema and queries
│   ├── Scanner/                写真列挙、メタデータ抽出、Polaris ログ読取
│   ├── Imaging/                サムネイル、PDQ
│   ├── AppConfig.cs            setting.json 読み書き
│   └── AppPaths.cs             データパス、レジストリ参照
├── Services/                   画面横断の操作サービス
├── Models/                     DTO / UI model
├── Messages/                   UI 文言カタログと UTF-8 properties
├── Shared/                     共通 UI、サービス、コンバーター
└── Themes/                     XAML resources
```

### Tests (`tests/Alpheratz.Tests/`)

xUnit で DB、スキャナ、PDQ、ViewModel、UI ロジック、ローカル連携の振る舞いを特性化する。

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
- 対象フォルダを最後まで列挙できた場合、存在しなくなった写真とタグ関連を DB から削除する。途中で読めない場所があった場合は誤削除を避けるため、そのスロットの削除を見送る。
- グループドリルダウンは中位モーダルとして Shell の `ModalContent` に表示する。

### Photo Modal

**Purpose**: 選択写真の詳細情報と編集操作を提供する。

#### Components

- `PhotoModalPage.xaml(.cs)`
- `PhotoModalViewModel`
- `PhotoModalState`

#### Behavior

- 写真、タグ、お気に入り、ワールド名、ワールド名の解決元を表示する。
- 前後の写真へ移動し、投稿、ワールドページ表示、Explorer でのファイル表示を行う。
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

**Purpose**: ワールド名不明写真に対し、Polaris archive または PDQ 類似度から候補を提示する。

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
- 画面を閉じるときは候補生成を停止し、サムネイル生成と UI 更新の完了を待ってから画面状態を破棄する。

### Settings

**Purpose**: アプリ設定、タグ、テンプレート、クレジットを管理する。

#### Components

- `SettingsPage.xaml(.cs)`
- `SettingsViewModel`
- `SettingsCompositeViewModel`
- `SettingsService`

#### Behavior

- General / Tag Master / Template / Credits の 4 セクションを左ナビゲーションで切り替える。
- General では 1st / 2nd 写真フォルダ、起動設定、投稿後のワールドリンク表示、テーマ、ワールド名の推測を扱う。
- 1st / 2nd 写真フォルダには、同一パスまたは親子関係になるパスを重複して設定できない。
- 表示モードは Shell ヘッダーで切り替え、`setting.json` に保存する。
- 投稿テンプレートと選択中テンプレートを `setting.json` に保存する。
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
4. Shell が画面用 ViewModel とイベント購読を初期化する。
5. 未完了のフォルダ初期化が記録されている場合、Gallery の読込とスキャンより前に処理を再開する。
6. Gallery の初期表示と写真スキャンを開始する。

### Photo Scan

1. `PhotoScanner` が設定フォルダから PNG / JPEG / WebP / PSD / XCF を列挙する。
2. 画像ファイルから撮影時刻、ワールド ID、ワールド名を抽出する。
3. `AlpheratzDb` が写真メタデータをバッチで upsert する。
4. 同じパスのファイル更新時刻が変化した場合は内容差し替えとして、旧画像由来のワールド情報と PDQ ハッシュを破棄する。新しい PNG メタデータを取得できた場合は、そのワールド情報を設定する。
5. フォルダを最後まで列挙できたスロットだけ、存在しなくなった写真とタグ関連を削除する。
6. orientation / 画像サイズ / PDQ / サムネイルをバックグラウンドで補完する。

### Photo Folder Change

1. `SettingsService` が新しいフォルダパスと未完了処理マーカーを `setting.json` へ同時に保存する。
2. 進行中のスキャンと Gallery のサムネイル生成を停止して完了を待つ。
3. アプリ全体の `ThumbnailWorker` を停止し、新しい生成要求も待機させる。
4. 対象スロットのタグ関連と写真をトランザクションで削除し、コミット後にサムネイルキャッシュを初期化する。
5. 未完了処理マーカーを消去してサムネイル生成を再開する。

途中で失敗した場合はマーカーを保持し、次回起動時に同じ処理を再開する。起動時の再開に失敗した場合は、旧フォルダ由来の Gallery 読込と写真スキャンを開始しない。

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

### Persisted State

| State | Persistence |
| --- | --- |
| 写真メタデータ、タグ、ワールド解決履歴 | SQLite |
| 写真フォルダ、テーマ、表示モード、起動・投稿設定、テンプレート、未完了のフォルダ初期化 | `setting.json` |
| サムネイル | `Data/cache/*/imgCache` |

### UI Messages

- ユーザー向け文言は `MessageCatalog.getMsg` から取得し、埋め込みの `Messages/messages.ja.properties` に一元管理する。
- properties は UTF-8 とし、動的な値は `{name}` 形式の名前付きプレースホルダーへ渡す。
- キー不足、不正な行、重複キーは読み込み時または取得時のエラーとして扱う。

---

## Concurrency Model

### Shared State

- 設定保存は `SettingsService` の lock で直列化する。
- スキャンはキャンセルトークンで中断を受け付ける。
- UI 更新は WinUI dispatcher 経由で行う。
- 写真フォルダ変更時はスキャン後処理を含む生成処理の停止完了を待ってから DB とキャッシュを初期化する。
- singleton の `ThumbnailWorker` が Gallery と World Resolve の生成要求を追跡し、初期化中は新しい書き込みを受け付けない。
- World Resolve の終了処理は候補生成と関連する UI 更新を待ち、終了済みの画面へ結果を反映しない。

### Write Exclusion

SQLite は接続ごとに WAL / NORMAL / FK ON / 5 秒の busy timeout を設定する。複数行更新は `AlpheratzDb` 内でトランザクション化する。

### Read Concurrency

WAL により、スキャン書き込み中も UI の SELECT がブロックされにくい。一覧の件数、ページ本体、タグは 1 つの読み取りトランザクションで同じスナップショットから取得する。

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

- ローカル画像ファイルと Polaris archive は同一ユーザーが読み取り可能なファイルとして扱う。
- ネットワーク越しの入力は扱わない。

### Mitigations

| Threat | Mitigation |
| --- | --- |
| SQL インジェクション | 値はパラメータバインドで渡す |
| シンボリックリンク等による再帰ループ | 訪問済み正規化パスでディレクトリ列挙を制御する |
| 巨大ログ行 | `MaxLogLineLength` で 1 行長を制限する |
| ローカル連携先不在 | Polaris が見つからない場合は archive 補完だけをスキップする |

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

### LocalStorage

該当なし。

---

## Known Limitations

| Limitation | Impact | Note |
| --- | --- | --- |
| Windows のみ対応 | 他 OS で動作不可 | WinUI 3 / Windows App SDK 前提 |
| コード署名なし | SmartScreen 警告が出る場合がある | 商用配布時に対応予定 |
| Polaris archive 依存の世界名補完 | Polaris 未導入環境では使用不可 | PDQ 解決と手動編集は利用可能 |
