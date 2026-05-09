# Alpheratz

写真ギャラリー＆管理アプリケーション。画像のワールド別タグ付け、メタデータ管理、知覚ハッシュ (PDQ) による重複検出を備えた Windows ネイティブデスクトップアプリです。

## Architecture

旧実装（TypeScript + WebView2）ではマソンリーレイアウト動作時にメモリ問題が発生し、最悪ケースで 1500MB に達していた。WinUI 3 に移行することで C# による明示的なメモリ管理が可能になり、Rust/Tauri の IPC 構成も廃止して単一プロセスで完結する構成とした。

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 8.0 + WinUI 3 (Windows App SDK 1.6) |
| Architecture | MVVM (CommunityToolkit.Mvvm 8.4) |
| DI | Microsoft.Extensions.DependencyInjection |
| Database | SQLite (Microsoft.Data.Sqlite 8.0) |
| Target | Windows 10 Build 19041+ (x64) |
| Distribution | NSIS Installer (unpackaged) |

## Features

- **Gallery** — メイソンリーレイアウトでの写真閲覧・フィルタリング
- **Photo Modal** — 詳細表示・編集
- **Tag Master** — ワールドベースのタグ管理
- **Template** — テンプレート管理
- **Duplicate Detection** — PDQ 知覚ハッシュによる重複画像検出
- **Settings** — テーマ切替、フォルダ設定、表示モード変更
- **Bootstrap** — 初期化ステータス付きスプラッシュスクリーン

## Project Structure

```
app/
  ├── Features/             # UI 機能モジュール
  │   ├── Gallery/          #   ギャラリー (フィルタ, グリッド, メイソンリー)
  │   ├── PhotoModal/       #   写真詳細・編集
  │   ├── TagMaster/        #   タグ管理
  │   ├── Template/         #   テンプレート管理
  │   ├── Settings/         #   設定
  │   ├── Shell/            #   アプリシェル・ナビゲーション
  │   └── Bootstrap/        #   起動・初期化
  ├── Core/                 # コアロジック
  │   ├── Database/         #   SQLite スキーマ・アクセス
  │   ├── Imaging/          #   サムネイル生成, PDQ ハッシュ
  │   └── Scanner/          #   写真スキャナー
  ├── Services/             # ビジネスロジック
  ├── Models/               # データモデル
  ├── Shared/               # 共通コントロール・コンバーター・アニメーション
  └── Themes/               # WinUI 3 スタイリング
BuildWorks/
  ├── nsis/                 # NSIS インストーラ設定
  └── scripts/              # PowerShell ビルド・発行スクリプト
```

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK 1.6 Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- Visual Studio 2022 (推奨) または VS Code + C# Dev Kit
- Windows 10 Build 19041+

### Development

```bash
dotnet build app/Alpheratz.Frontend.csproj
dotnet run --project app/Alpheratz.Frontend.csproj
```

### Release Build

```powershell
dotnet publish app/Alpheratz.Frontend.csproj -c Release --self-contained
# または
.\BuildWorks\scripts\build-release.ps1
```

## License

Proprietary — CosmoArtsStore
