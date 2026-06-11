# Tech Stack

> Alpheratz の技術スタックと主要な設計判断。

## Stack

| Layer | Technology |
| --- | --- |
| Runtime | .NET 8.0 |
| UI | WinUI 3 / Windows App SDK `1.6.250205002` |
| Architecture | MVVM with `CommunityToolkit.Mvvm` `8.4.0` |
| DI / Host | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting` |
| Database | SQLite via `Microsoft.Data.Sqlite` `8.0.11` |
| Imaging | Windows imaging APIs, custom PDQ implementation |
| Target | Windows 10 Build 19041+ / x64 |
| Distribution | NSIS installer, unpackaged Windows app |

## Build and Distribution

Development build:

```powershell
dotnet build app/Alpheratz.Frontend.csproj
dotnet run --project app/Alpheratz.Frontend.csproj
```

Release build:

```powershell
dotnet publish app/Alpheratz.Frontend.csproj -c Release --self-contained
.\BuildWorks\scripts\build-release.ps1
```

Release は self-contained 配布を前提に、.NET runtime と Windows App SDK の必要ファイルを AppLocal に同梱する。Debug は通常の `dotnet build` を優先し、システムにインストール済みの Windows App Runtime を使う。

## Design Decisions

### WinUI 3 single-process app

旧 TypeScript + WebView2 構成はメイソンリーレイアウトのメモリ使用量が大きく、写真ギャラリー用途では制御しづらかった。現行は WinUI 3 の単一プロセスに寄せ、Rust/Tauri IPC と WebView 境界を廃止した。

### Cohesive SQLite façade

`AlpheratzDb` はスキーマ初期化、クエリ、更新を集約する。現状の DB 利用は写真・タグ・ワールド解決という 1 つのローカル永続化境界に閉じているため、過度に細かい repository 分割は採用しない。

### Background enrichment

スキャン後の orientation / 画像サイズ / PDQ 計算 / サムネイル生成は UI と分離して実行する。UI は `LocalEventBus` と ViewModel の状態更新で進捗を受け取る。

### Local integrations

Polaris archive と StellaRecord は外部 API ではなく、同一 Windows ユーザーのローカルレジストリと SQLite ファイルを参照する。対象が見つからない場合は連携だけを諦め、Alpheratz 本体の起動や基本機能は継続する。
