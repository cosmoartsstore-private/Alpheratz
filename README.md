# Alpheratz

Alpheratz は、VRChat の写真をローカルで閲覧・分類する Windows 向け写真管理アプリケーションです。設定した 1st / 2nd 写真フォルダを走査し、撮影時刻、ワールド情報、タグ、お気に入り、画像寸法、PDQ 知覚ハッシュを SQLite に保存します。

通常の利用で外部 API、認証、テレメトリ送信は行いません。投稿画面、VRChat ワールドページ、クレジットのリンクは、利用者が操作したときだけ既定のブラウザーで開きます。

## Features

- **Gallery** — 標準グリッドまたはメイソンリーレイアウトで写真を表示します。撮影日、ワールド、向き、タグ、お気に入りで絞り込み、撮影日時の降順またはワールド名順で並べ替えられます。
- **World search** — ワールド名候補を最大5件表示し、Enter で入力文字列を検索条件として確定します。入力中に自動検索は実行しません。
- **Grouping** — 標準グリッドではワールド単位にグループ化し、グループ内の写真へドリルダウンできます。
- **Photo details** — 写真の拡大表示、前後移動、お気に入り切替、複数タグの追加・削除、Explorer での選択表示、VRChat ワールドページの表示、投稿画面の起動を行えます。
- **Bulk actions** — 複数写真のお気に入り切替、複数タグの追加、別の写真フォルダへのコピーを行います。部分失敗時は未完了の写真だけを選択状態に残します。
- **Tag master** — 25文字以内のタグを登録・削除し、ギャラリーと写真詳細の候補として利用します。
- **Post templates** — 複数の投稿テンプレートとアクティブテンプレートを保存します。X の加重文字数を基準に280文字まで入力でき、ワールド名、日時、ファイル名、タグなどを投稿時に展開します。
- **World resolve** — Polaris archive の訪問履歴でワールド名を補完します。残った未判定写真は PDQ 距離から一致率71%以上の候補を提示し、利用者が候補または手入力のワールド名を確認してから保存します。
- **Settings** — 写真フォルダ、自動起動、投稿時のワールドページ表示、テーマ、タグ、投稿テンプレート、クレジットを左ナビゲーションで切り替えて管理します。
- **Bootstrap** — 起動中はロゴと著作権表示だけをフェード表示します。処理量と対応しない疑似進捗は表示しません。

## Architecture

```text
Windows 10 / 11 (x64)
└─ Alpheratz.exe                         launcher
   └─ app/Alpheratz.Frontend.exe         .NET 8 + WinUI 3
      ├─ Features                        Pages / ViewModels / UI state
      ├─ Services                        use-case orchestration
      ├─ Core                            scan / imaging / database / paths
      └─ Data
         ├─ db/Alpheratz.db              metadata and tags
         ├─ db/setting.json              settings and templates
         ├─ cache/{1st,2nd}-cache        thumbnails
         └─ logs/info.log                application log
```

Tauri、WebView2、Rust IPC は現行アプリでは使用しません。UI、写真走査、画像解析、DB 更新は同じ .NET プロセス内で動作します。

## Tech Stack

| Layer | Technology |
| --- | --- |
| Runtime / UI | .NET 8、WinUI 3、Windows App SDK `1.6.250205002` |
| State | MVVM、`CommunityToolkit.Mvvm 8.4.0` |
| DI | `Microsoft.Extensions.DependencyInjection 8.0.1` |
| Database | SQLite、`Microsoft.Data.Sqlite 8.0.11` |
| Imaging | Windows imaging APIs、独自 PDQ 実装 |
| Messages | UTF-8 `messages.ja.properties`、`MessageCatalog.getMsg` |
| Tests | xUnit `2.5.3`、coverlet `6.0.0` |
| Distribution | x64 self-contained publish、unpackaged NSIS current-user installer |

## Requirements

### Runtime

- Windows 10 Build 19041 以降、または Windows 11
- x64

Release 配布物には .NET、Windows App SDK、Microsoft Visual C++ Redistributable（x64）を同梱します。利用者がこれらを事前に導入する必要はありません。

### Development

- .NET 8 SDK
- Windows 10 SDK を利用できる Windows 開発環境
- Debug 実行時に対応する Windows App Runtime 1.6
- NSIS（インストーラを作成する場合）

## Installation

`Alpheratz-Installer.exe` を実行します。既定のインストール先は `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz` で、Program Files 配下は選択できません。Microsoft Visual C++ Redistributableの導入または更新が必要な場合だけ、WindowsのUAC承認を求めます。必要versionが導入済みなら管理者権限は使いません。

更新または再インストールでは launcher と `app` ディレクトリを置き換え、既存の `Data` は保持します。アンインストールではアプリ本体、ショートカット、`Data`、関連する HKCU レジストリキーを削除します。参照元の写真フォルダは削除しません。

## Build and Test

```powershell
# 開発ビルド
dotnet build app/Alpheratz.Frontend.csproj

# Debug 実行
dotnet run --project app/Alpheratz.Frontend.csproj

# 全テスト
dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj

# Release publish、launcher、NSIS installer
.\BuildWorks\scripts\build-release.ps1
```

インストーラは `BuildWorks\Alpheratz-Installer.exe` に生成されます。Release の XAML 配置とMicrosoft Visual C++ Redistributableの同梱には通常の `dotnet publish` 以外の処理が必要なため、配布物は必ず `build-release.ps1` から作成してください。scriptはMicrosoft公式配布物を取得して署名を検証します。通信できない環境で検証済みcacheを再利用するときだけ、`-UseCachedVcRedist`を指定します。

`.github/workflows/ci.yml` は、mainへのpushとmain宛PRで全テストと最終NSIS installer生成を実行します。同一branchの古い実行は新しいpush時にcancelします。

## Data and Privacy

| Data | Location | Purpose |
| --- | --- | --- |
| SQLite | `<install>\Data\db\Alpheratz.db` | 写真メタデータ、タグ、訪問履歴 |
| Settings | `<install>\Data\db\setting.json` | フォルダ、テーマ、表示、起動、投稿、テンプレート |
| Thumbnails | `<install>\Data\cache\1st-cache\imgCache`、`2nd-cache\imgCache` | 一覧・候補表示 |
| Logs | `<install>\Data\logs\info.log` | fatal ログ。詳細ログは明示的に有効化した場合のみ |
| Registry | `HKCU\Software\CosmoArtsStore\Alpheratz` | インストール先と frontend の場所 |

写真フォルダを変更またはリセットすると、進行中の走査とサムネイル生成を止め、対象 source slot の DB 行とキャッシュを整理してから再走査します。整理要求は `setting.json` に保存されるため、途中で終了しても次回起動時に再実行されます。1st と 2nd に同一フォルダまたは親子関係のフォルダは設定できません。

通常は `Fatal` だけを最大1 MiBのローテーションログへ保存します。`ALPHERATZ_VERBOSE_LOGS=1` を設定した場合は `Error`、`Warn`、`Info` も保存し、`TRACE_LOGGING` を有効にしたビルドでは `Trace` も対象になります。

## Project Structure

```text
.
├─ app/                           WinUI 3 application
│  ├─ Features/                   screen-local UI and state
│  ├─ Services/                   application services
│  ├─ Core/                       database, scanner, imaging, paths
│  ├─ Messages/                   Japanese UI message catalog
│  ├─ Shared/                     shared controls and services
│  └─ Themes/                     XAML resources
├─ tests/Alpheratz.Tests/         xUnit tests
├─ BuildWorks/                    launcher, publish scripts, NSIS
├─ docs/                          public current-state documents
└─ .claude/                       agent-only rules and handoffs
```

## Documentation

| Document | Scope |
| --- | --- |
| [Specification](docs/spec.md) | 現行機能、処理フロー、並行処理、制約 |
| [Database Schema](docs/database.md) | SQLite スキーマ、クエリ上の意味、トランザクション |
| [Tech Stack](docs/tech-stack.md) | バージョン、ビルド・配布契約、採用理由 |
| [Basic Design](docs/basic-design.html) | 現行画面の構成と操作状態 |

## License

Proprietary — Copyright (c) CosmoArtsStore. All rights reserved.
