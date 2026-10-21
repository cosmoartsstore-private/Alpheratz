# Alpheratz

Alpheratz は、VRChat の写真をローカルで閲覧・分類する Windows 向け写真管理アプリケーションです。設定した 1st / 2nd 写真フォルダを走査し、撮影時刻、ワールド情報、タグ、お気に入り、画像寸法、PDQ 知覚ハッシュを SQLite に保存します。検索・分類、投稿準備、ワールド名補完、確認後の完全一致写真削除を行えます。

通常の利用で外部 API、認証、テレメトリ送信は行いません。投稿画面、VRChat ワールドページ、クレジットのリンクは、利用者が操作したときだけ既定のブラウザーで開きます。

## Features

- **Gallery** — 標準グリッドまたはメイソンリーレイアウトで写真を表示します。撮影日、ワールド、向き、タグ、お気に入りで絞り込み、撮影日時の降順またはワールド名順で並べ替えられます。
- **World search** — ワールド名候補を最大5件表示し、Enter で入力文字列を検索条件として確定します。入力中に自動検索は実行しません。
- **Grouping** — 標準グリッドではワールド単位にグループ化し、グループ内の写真へドリルダウンできます。
- **Photo details** — 写真の拡大表示、前後移動、お気に入り切替、複数タグの追加・削除、Explorer での選択表示、VRChat ワールドページの表示、投稿画面の起動を行えます。
- **Bulk actions** — 複数写真のお気に入り切替、複数タグ追加、folder pickerで選んだフォルダへのコピーを行います。お気に入り・タグの部分失敗では未完了写真を選択へ残し、コピーは元の選択を維持して成功・失敗件数を通知します。同名ファイルは上書きしません。
- **Tag master** — 25文字以内のタグを登録・削除し、ギャラリーと写真詳細の候補として利用します。
- **Post templates** — 複数の投稿テンプレートとアクティブテンプレートを保存します。X の加重文字数を基準に280文字まで入力でき、ワールド名、日時、ファイル名、タグなどを投稿時に展開します。
- **World resolve** — Polaris の `<InstallLocation>/Data/archive` の訪問ログでワールド名を補完します。残った未判定写真は PDQ 距離から一致率71%以上の候補を提示し、利用者が候補または手入力のワールド名を確認してから保存します。
- **Duplicate photos** — ファイルサイズと PDQ で候補を絞り、SHA-256 が一致する写真をまとめます。各組に1枚以上を残す選択制約と確認画面を設け、削除直前に対象と保持写真の内容を再確認します。
- **Photo user data backup** — 現在の写真のお気に入りとタグをファイル名単位で同じ DB 内へ保存し、フォルダ再読込後に復元できます。写真原本や DB ファイル全体のバックアップではありません。
- **Settings** — 写真フォルダ、自動起動、投稿時のワールドページ表示、テーマ、重複写真、タグ、投稿テンプレート、クレジットを左ナビゲーションで切り替えて管理します。
- **Bootstrap** — 起動中はロゴと著作権表示だけをフェード表示します。処理量と対応しない疑似進捗は表示しません。

## Architecture

```text
Alpheratz.exe (launcher)
└─ app/Alpheratz.Frontend.exe (.NET 8 + WinUI 3)
   ├─ reads: 1st / 2nd photo folders, Polaris Data/archive
   ├─ stores: <install>/Data (SQLite, JSON, thumbnails, logs)
   └─ explicit actions: clipboard, browser, Explorer, copy / duplicate delete
```

UI、写真走査、画像解析、DB 更新は同じ .NET プロセス内で動作します。process、全moduleの責務、状態所有、起動・停止経路は [Architecture](docs/architecture.md) を参照してください。

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

SDKの宣言値、Debug / Releaseの違い、toolの探索先は [Tech Stack](docs/tech-stack.md#build-and-packaging-contract) を参照してください。

## Installation

`Alpheratz-Installer.exe` を実行します。既定のインストール先は `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz` で、Program Files 配下は選択できません。Microsoft Visual C++ Redistributableの導入または更新が必要な場合だけ、WindowsのUAC承認を求めます。必要versionが導入済みなら管理者権限は使いません。

更新または再インストールでは launcher と `app` を置き換え、既存の `Data` は保持します。実行中アプリを閉じてから導入してください。通常終了時に全background処理の完了を待つ保証はありません。

アンインストールはアプリ本体、ショートカット、標準 `Data/db`・`Data/logs`・`Data/cache`、Runと製品のHKCU登録を削除します。DB内のお気に入り・タグ・復元用バックアップも失われます。写真原本、Polaris archive、共有VC++ runtimeは残し、`Data` とinstall rootは空の場合だけ削除します。詳細は [保存物の寿命](docs/database.md#deletion-and-retention) を参照してください。

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

インストーラは `BuildWorks\Alpheratz-Installer.exe` に生成されます。Release の XAML 配置とVC++同梱が必要なため、配布物は `build-release.ps1` から作成してください。通常はMicrosoft公式配布物を取得・署名検証し、取得失敗時は既存の有効cacheを再利用します。`-UseCachedVcRedist` は有効cacheを優先する指定です。

CIはmainへのpushとmain宛PRでDebug testと最終NSIS installer生成を実行します。Release test、coverage閾値、installed app操作は自動gateに含みません。実commandと対象は [Verification Commands and CI](docs/tech-stack.md#verification-commands-and-ci) を参照してください。

## Data and Privacy

| Data | Location | Purpose |
| --- | --- | --- |
| SQLite | `<install>\Data\db\Alpheratz.db` | 写真メタデータ、タグ、訪問履歴、お気に入り・タグのバックアップ |
| Settings | `<install>\Data\db\setting.json` | フォルダ、テーマ、表示、起動、投稿、テンプレート |
| Thumbnails | `<install>\Data\cache\1st-cache\imgCache`、`2nd-cache\imgCache` | 一覧・候補表示 |
| Logs | `<install>\Data\logs\info.log` | fatal ログ。詳細ログは明示的に有効化した場合のみ |
| Registry | `HKCU\Software\CosmoArtsStore\Alpheratz` | インストール先と frontend の場所 |

写真フォルダの初回設定は直接適用し、既存フォルダの変更・リセットは確認後に行います。進行中の走査とサムネイル生成を止め、対象slotのindexとcacheを整理します。整理要求をJSONへ保存するため、途中終了後も次回起動で再実行します。原本は保持し、1st / 2ndの同一・親子フォルダは拒否します。

重複写真の削除だけは写真原本へ作用し、Windows の `StorageDeleteOption.Default` に従って削除します。ごみ箱を使用できない保存先では完全削除になる場合があります。ファイル削除後の DB 整理に失敗しても原本の削除は取り消せません。

お気に入り・タグのバックアップは `photo_user_data_backup` に保存するため、フォルダのリセット後も残りますが、DB の削除やアンインストールでは失われます。同名写真は大文字小文字を区別せず情報を統合し、復元時は一致するすべての写真へ反映します。完全な手動バックアップはアプリ終了後に `Data\db` をコピーしてください。

通常は `Fatal` だけを保存し、flush前に1 MiB超を確認して1世代rotateします。1 MiBは厳密な最大file sizeではありません。`ALPHERATZ_VERBOSE_LOGS=1` で `Error`・`Warn`・`Info`、さらに `TRACE_LOGGING` buildで `Trace` も対象になります。JSON・registry・cacheを含む詳細は [Database Schema](docs/database.md) を参照してください。

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
| [Architecture](docs/architecture.md) | process、module責務、状態所有、event、停止と解放 |
| [Database Schema](docs/database.md) | SQLite・JSON・cache・log・registry、更新単位、保持・削除・backup |
| [Tech Stack](docs/tech-stack.md) | version、採用理由、開発command、CI実gate、配布・導入 |
| [Basic Design](docs/basic-design.html) | 現行画面の構成と操作状態を示す設計資料。操作manualではない |

利用開始はREADME→必要な機能のSpecification→Basic Design、保守参加はREADME→Architecture→対象機能→保存契約→開発・配布手順の順に参照してください。

## License

Proprietary — Copyright (c) CosmoArtsStore. All rights reserved.
