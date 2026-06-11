# Alpheratz

写真ギャラリー＆管理アプリケーション。画像のワールド別タグ付け、メタデータ管理、知覚ハッシュ (PDQ) による重複検出を備えた Windows ネイティブデスクトップアプリケーション。

完全ローカル運用。外部 API・認証・ネットワーク通信は使用しない。

---

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Requirements](#requirements)
- [Installation](#installation)
- [Build from Source](#build-from-source)
- [Project Structure](#project-structure)
- [Data and Privacy](#data-and-privacy)
- [Security](#security)
- [Documentation](#documentation)
- [Acknowledgements](#acknowledgements)
- [License](#license)

---

## Overview

Alpheratz は VRChat などの写真をローカルで閲覧・分類する Windows デスクトップアプリケーションである。設定した 1st / 2nd 写真フォルダをスキャンし、撮影時刻・ワールド情報・タグ・お気に入り・PDQ ハッシュを SQLite に保存する。

旧 TypeScript + WebView2 実装ではメイソンリーレイアウト時のメモリ使用量が重くなったため、現行版は .NET 8 + WinUI 3 の単一プロセス構成に移行している。

---

## Features

- **Gallery** — 標準グリッド / メイソンリーレイアウトで写真を表示し、日付・ワールド・タグ・お気に入り・欠損状態で絞り込む。
- **Photo Modal** — 写真詳細、タグ、お気に入り、ワールド名、類似写真を表示・編集する。
- **Tag Master** — 写真に付与するタグを管理する。
- **Template** — 投稿用テンプレートを保存し、選択中テンプレートを切り替える。
- **Duplicate Detection** — PDQ 知覚ハッシュで類似写真を検出する。
- **World Resolve** — Polaris archive と PDQ 類似度から世界不明写真の候補を提示する。
- **Settings** — 写真フォルダ、テーマ、表示モード、起動設定、投稿テンプレート、StellaRecord 登録を管理する。
- **Bootstrap** — 起動時の初期化状態をスプラッシュ画面に表示する。

---

## Tech Stack

| Layer | Technology |
| --- | --- |
| UI | .NET 8.0 + WinUI 3 / Windows App SDK 1.6 |
| Architecture | MVVM (`CommunityToolkit.Mvvm`) |
| DI / Host | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting` |
| Database | SQLite (`Microsoft.Data.Sqlite`) |
| Imaging | Windows imaging APIs, custom PDQ implementation |
| Testing | xUnit + coverlet |
| Distribution | NSIS installer, unpackaged Windows app |

技術選定の詳細と意思決定記録は [docs/tech-stack.md](docs/tech-stack.md) を参照。

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  Windows 10 / 11 (x64)                      │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              Alpheratz.exe (Launcher)               │    │
│  │                       │                             │    │
│  │                       ▼                             │    │
│  │        Alpheratz.Frontend.exe (WinUI 3)             │    │
│  │                                                     │    │
│  │   Features/* Pages + ViewModels                     │    │
│  │              │                                      │    │
│  │              ▼                                      │    │
│  │   Services / Core / AlpheratzDb                     │    │
│  └──────────────┬──────────────────────────────────────┘    │
│                 ▼                                           │
│        SQLite: Data/db/Alpheratz.db                         │
│        Cache : Data/cache/*/imgCache                        │
│        Logs  : Data/logs                                    │
└─────────────────────────────────────────────────────────────┘
```

Tauri / Rust IPC は使用しない。UI、スキャン、画像解析、DB 更新は .NET プロセス内で完結する。詳細は [docs/spec.md](docs/spec.md) を参照。

---

## Requirements

### Runtime

- Windows 10 Build 19041 以降 / Windows 11 (x64)
- VC++ Redistributable（未導入時は起動時に DLL 不足として失敗する）

### Build

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows App SDK 1.6
- Visual Studio 2022 または VS Code + C# Dev Kit
- NSIS（インストーラ生成時）

---

## Installation

### From Installer

1. `Alpheratz-v2-Installer.exe` を実行
2. インストール先を選択する
3. 既定のインストール先は `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz`

管理者権限は不要。Program Files 配下へのインストールは installer 側で拒否する。

### Uninstallation

Windows の「アプリと機能」または `uninstall.exe` からアンインストールする。

アンインストール時はアプリ本体、ショートカット、`Data/db`, `Data/logs`, `Data/cache`, レジストリキーを削除する。写真フォルダ本体はアプリ管理外のため削除しない。

---

## Build from Source

```powershell
# 開発ビルド
dotnet build app/Alpheratz.Frontend.csproj
dotnet run --project app/Alpheratz.Frontend.csproj

# テスト
dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj

# 本番ビルド + NSIS インストーラ
.\BuildWorks\scripts\build-release.ps1
```

---

## Project Structure

```
.
├── README.md
├── docs/                         公開技術ドキュメント
│   ├── spec.md                   機能仕様書
│   ├── database.md               データベース定義書
│   ├── tech-stack.md             技術スタックと ADR
│   └── basic-design.html         画面レイアウト基本設計書
├── app/                          WinUI 3 アプリケーション
│   ├── Features/                 画面・ViewModel
│   ├── Core/                     DB、スキャン、画像処理、パス解決
│   ├── Services/                 ビジネスロジック
│   ├── Models/                   DTO / UI モデル
│   ├── Shared/                   共通コントロール、コンバーター、サービス
│   └── Themes/                   WinUI 3 スタイル
├── tests/Alpheratz.Tests/        xUnit テスト
└── BuildWorks/                   launcher、publish、NSIS installer
```

---

## Data and Privacy

本アプリはローカル完結で動作する。

| Data | Location | Purpose |
| --- | --- | --- |
| SQLite データベース | `<install>\Data\db\Alpheratz.db` | 写真メタデータ、タグ、ワールド解決履歴 |
| 設定 JSON | `<install>\Data\db\setting.json` | フォルダ、テーマ、表示モード、テンプレート |
| サムネイルキャッシュ | `<install>\Data\cache\1st-cache\imgCache`, `<install>\Data\cache\2nd-cache\imgCache` | 一覧表示用画像 |
| ログ | `<install>\Data\logs` | アプリ実行ログ |
| インストール先 | Windows Registry `HKCU\Software\CosmoArtsStore\Alpheratz` | installer とアプリのパス解決 |

外部通信・テレメトリ送信は行わない。Polaris / StellaRecord 連携は同一 Windows ユーザーのローカルレジストリとローカルファイルだけを参照する。

---

## Security

### Application

- ネットワーク API、認証、外部サーバ送信を持たない。
- SQLite 書き込みはパラメータバインドを使う。
- Polaris archive と StellaRecord DB はレジストリから解決できる場合のみ参照する。

### Installation

- current user install。管理者権限を要求しない。
- Program Files 配下へのインストールは拒否する。
- コード署名は未実装。

### Known Risks

- コード署名なしのため SmartScreen 警告が出る場合がある。
- 写真ファイル、Polaris archive、StellaRecord DB のアクセス権は OS ユーザー権限に依存する。

---

## Documentation

| Document | Description |
| --- | --- |
| [docs/spec.md](docs/spec.md) | 機能仕様書（アーキテクチャ、各機能、データフロー、状態管理） |
| [docs/database.md](docs/database.md) | データベース定義書（スキーマ、インデックス、マイグレーション） |
| [docs/tech-stack.md](docs/tech-stack.md) | 技術スタック詳細と意思決定記録（ADR） |
| [docs/basic-design.html](docs/basic-design.html) | 画面レイアウト基本設計書 |

---

## Acknowledgements

- PDQ perceptual hash algorithm
- .NET, WinUI 3, Windows App SDK
- SQLite

---

## License

Proprietary — Copyright (c) CosmoArtsStore. All rights reserved.
