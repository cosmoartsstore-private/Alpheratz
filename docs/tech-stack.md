# Tech Stack and Architecture Decisions

> Alpheratz で採用した技術の詳細リファレンスと、主要な技術選定の意思決定記録 (ADR: Architecture Decision Record)。

## Table of Contents

- [Tech Stack Reference](#tech-stack-reference)
  - [Frontend](#frontend)
  - [Backend](#backend)
  - [Build and Distribution](#build-and-distribution)
  - [Quality and Tooling](#quality-and-tooling)
  - [Testing and CI](#testing-and-ci)
- [Architecture Decision Records](#architecture-decision-records)
  - [ADR-001 Application Framework: WinUI 3](#adr-001-application-framework-winui-3)
  - [ADR-002 State Management: MVVM](#adr-002-state-management-mvvm)
  - [ADR-003 Database: SQLite via Microsoft.Data.Sqlite](#adr-003-database-sqlite-via-microsoftdatasqlite)
  - [ADR-004 Imaging: Windows Imaging + custom PDQ](#adr-004-imaging-windows-imaging--custom-pdq)
  - [ADR-005 Styling: XAML Resource Dictionaries](#adr-005-styling-xaml-resource-dictionaries)
  - [ADR-006 Installer: NSIS unpackaged current-user install](#adr-006-installer-nsis-unpackaged-current-user-install)
- [Rejected Technologies](#rejected-technologies)

---

## Tech Stack Reference

### Frontend

| Layer | Technology | Version | License |
| --- | --- | --- | --- |
| Language | C# | latest / .NET 8 | MIT |
| UI Framework | WinUI 3 | Windows App SDK 1.6.250205002 | MIT |
| XAML | WinUI XAML | - | - |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 | MIT |
| Styling | XAML Resource Dictionaries | - | - |
| UI messages | `MessageCatalog.getMsg` + UTF-8 properties | - | Project code |

### Backend

| Layer | Technology | Version | License |
| --- | --- | --- | --- |
| Runtime | .NET | 8.0 | MIT |
| DI | Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT |
| Database | Microsoft.Data.Sqlite | 8.0.11 | MIT |
| Imaging | Windows imaging APIs | Windows SDK | Microsoft |
| PDQ | Custom C# port | - | Project code |
| Registry I/O | Microsoft.Win32.Registry | .NET | MIT |

### Build and Distribution

| Layer | Technology | Configuration |
| --- | --- | --- |
| App publish | `dotnet publish` | `Release`, `win-x64`, self-contained |
| Windows App SDK | Self-contained AppLocal | `WindowsAppSDKSelfContained=true` |
| Launcher | .NET single-file launcher | `Alpheratz.exe` |
| Installer | NSIS | `BuildWorks/nsis/Installer.nsi` |
| Install Mode | currentUser | `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz` |

Frontend publish では XBF を publish root と assembly mirror へ同期し、Windows App SDK の Controls PRI を root の `resources.pri` として配置する。`App.xbf`, `MainWindow.xbf`, `resources.pri` が揃わない場合は release build を失敗させる。launcher は self-contained single-file として別途 publish する。

NSIS の更新または再インストールでは launcher と `app` だけを入れ替え、既存の `Data` を保持する。アンインストールではアプリ管理下の `Data/db`, `Data/logs`, `Data/cache` も削除する。

### Quality and Tooling

| Layer | Technology | Version |
| --- | --- | --- |
| C# compiler | Roslyn via .NET SDK | 8.x |
| Nullable analysis | `<Nullable>enable</Nullable>` | - |
| Text format | `.editorconfig` | UTF-8 / LF / 末尾改行 / 行末空白除去（NSIS は UTF-16LE / CRLF） |
| Test runner | xUnit runner | 2.5.3 |
| Coverage | coverlet | 6.0.0 |

### Testing and CI

- **単体テスト**: `tests/Alpheratz.Tests` の xUnit テスト。
- **ローカル検証**: `dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj`。
- **Release 検証**: `BuildWorks/scripts/build-release.ps1` で frontend、launcher、NSIS installer を順に生成し、`BuildWorks/Alpheratz-Installer.exe` を確認する。
- **CI**: リポジトリ内の CI 定義に従う。公開 docs ではローカルで実行すべき検証コマンドを一次情報として扱う。

---

## Architecture Decision Records

各意思決定は以下のテンプレートで記述する。

```
- Status:    Accepted | Superseded | Deprecated
- Date:      決定日
- Context:   何を解決しようとしているか
- Decision:  何を採用したか
- Rationale: なぜそれを採用したか
- Alternatives Considered: 検討した他の選択肢と却下理由
- Consequences: 採用後に発生する影響
```

### ADR-001 Application Framework: WinUI 3

- **Status**: Accepted
- **Date**: 2026-04

**Context**

写真ギャラリーは大量画像、メイソンリーレイアウト、サムネイル生成、バックグラウンド解析を扱う。旧 TypeScript + WebView2 構成ではメモリ使用量が大きくなりやすかった。

**Decision**

.NET 8 + WinUI 3 の単一プロセス構成を採用する。依存性注入は `ServiceCollection` から直接構築し、Generic Host は使用しない。

**Rationale**

- Windows デスクトップアプリとして画像表示と OS 統合を直接扱える
- C# でバックグラウンド処理、SQLite、画像処理を同一プロセスに収められる
- Tauri / Rust IPC 境界を廃止できる

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| Tauri + React | 旧実装のメモリ問題と IPC 境界の複雑さが残る |
| WPF | WinUI 3 の現行 Windows UI への追従を優先 |
| Electron | 配布サイズとメモリ使用量が大きい |

**Consequences**

- (+) 画像処理と UI 状態を C# で一貫して扱える
- (+) self-contained 配布により .NET runtime 依存を減らせる
- (-) Windows 専用になる
- (-) WinUI 3 の unpackaged 配布制約を build script で扱う必要がある

### ADR-002 State Management: MVVM

- **Status**: Accepted
- **Date**: 2026-04

**Context**

Gallery、PhotoModal、Settings、WorldResolve は画面状態と非同期処理を多く持つ。

**Decision**

CommunityToolkit.Mvvm による MVVM を採用する。

**Rationale**

- XAML binding と相性が良い
- ViewModel 単位でテストしやすい
- UI イベントと永続化処理を分離できる

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| code-behind 中心 | テスト対象が UI に寄りすぎる |
| 独自 observable 実装 | CommunityToolkit で足りる |

**Consequences**

- (+) ViewModel の単体テストを増やせる
- (-) Page と ViewModel のイベント接続が多い画面では接続解除の管理が必要

### ADR-003 Database: SQLite via Microsoft.Data.Sqlite

- **Status**: Accepted
- **Date**: 2026-04

**Context**

写真メタデータ、タグ、ワールド訪問履歴をローカルで保持する必要がある。

**Decision**

SQLite を `Microsoft.Data.Sqlite` から利用する。

**Rationale**

- 単一ファイルでバックアップしやすい
- WAL により読み書き並行性を確保できる
- 写真本体を DB に入れず、メタデータだけを保持する要件に合う

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| JSON ファイル | 検索・タグ関連・インデックスに向かない |
| LiteDB | SQLite の検証済み運用とツール互換を優先 |

**Consequences**

- (+) 外部サーバなしで検索可能なデータを保持できる
- (-) スキーマ変更は防御的マイグレーションで管理する必要がある

### ADR-004 Imaging: Windows Imaging + custom PDQ

- **Status**: Accepted
- **Date**: 2026-04

**Context**

サムネイル生成、画像サイズ取得、PDQ ハッシュ計算をローカルで行う必要がある。

**Decision**

Windows imaging APIs と C# 実装の PDQ を使う。

**Rationale**

- Windows 画像デコーダを利用できる
- PDQ をアプリ内で完結できる
- サムネイルキャッシュと解析結果を DB に分離して保持できる

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| 外部画像処理 CLI | 配布物と失敗要因が増える |
| サーバ側解析 | ローカル完結要件に反する |

**Consequences**

- (+) 画像解析をオフラインで実行できる
- (-) PDQ 実装の正しさをテストで保つ必要がある

### ADR-005 Styling: XAML Resource Dictionaries

- **Status**: Accepted
- **Date**: 2026-04

**Context**

WinUI 3 の画面全体で色、余白、コントロールスタイルを統一する必要がある。

**Decision**

`Themes/` 配下の XAML Resource Dictionary を採用する。

**Rationale**

- XAML の標準的なスタイル解決に乗せられる
- テーマ切替を WinUI の resource 解決で扱える

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| コントロールごとの inline style | 再利用性と一貫性が落ちる |
| 独自 CSS 風テーマ | WinUI の仕組みと重複する |

**Consequences**

- (+) 画面間のスタイルを共通化できる
- (-) Resource 名の変更は XAML 全体に影響する

### ADR-006 Installer: NSIS unpackaged current-user install

- **Status**: Accepted
- **Date**: 2026-04

**Context**

管理者権限なしで配布し、データを `Data/` 配下にまとめる必要がある。

**Decision**

NSIS による current-user install を採用し、WinUI 3 は unpackaged self-contained で配布する。

**Rationale**

- `%LOCALAPPDATA%` 配下へ管理者権限なしでインストールできる
- installer でレジストリ、ショートカット、アンインストールを制御できる
- MSIX 固有の制約を避けられる

**Alternatives Considered**

| Option | Rejected Reason |
| --- | --- |
| MSIX | 配布とランタイム制約が増える |
| zip 配布 | レジストリとアンインストール情報を管理しづらい |

**Consequences**

- (+) launcher と WinUI 本体の配置先をレジストリで共有できる
- (+) 更新または再インストール時に既存の `Data` を保持できる
- (-) アンインストールでは管理対象の `Data` も削除するため、必要に応じて事前バックアップが必要になる
- (-) コード署名なしでは SmartScreen 警告が出る場合がある

---

## Rejected Technologies

| Technology | Reason |
| --- | --- |
| Tauri + React | 旧実装でメモリ問題があり、画像ギャラリー用途では WinUI 3 に寄せる判断となった |
| Electron | 配布サイズとメモリ使用量が要件に合わない |
| JSON-only storage | タグ、検索、ワールド解決履歴に対する検索性能と整合性が不足する |
