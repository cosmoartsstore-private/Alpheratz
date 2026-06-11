# Specification

> Alpheratz の公開仕様リファレンス。実体は `app/Features/`, `app/Core/`, `app/Services/`。

## Overview

Alpheratz は VRChat などの写真をローカルで閲覧・分類する Windows デスクトップアプリケーションである。写真フォルダをスキャンし、EXIF / PNG iTXt / ファイル名から取得できる撮影時刻・ワールド情報を SQLite に保存する。ワールド不明写真は Polaris archive のログ履歴または PDQ 類似度を使って補完できる。

外部サーバ、認証、クラウド同期は扱わない。データはインストール先配下の `Data/` に保存する。

## Architecture

```
WinUI 3 UI
  Features/* Pages and ViewModels
    |
Services
  PhotoService / WorldService / SettingsService / ThumbnailWorker / PhashService
    |
Core
  AlpheratzDb / PhotoScanner / ThumbnailService / PDQ implementation / AppPaths
    |
SQLite + local files
  Data/db/Alpheratz.db
  Data/cache/*/imgCache
  Data/logs
```

旧 TypeScript + WebView2 実装はメイソンリーレイアウト時のメモリ使用量が重くなったため廃止した。現行実装は .NET 8 + WinUI 3 の単一プロセス構成で、Tauri / Rust IPC は使用しない。

## Features

### Gallery

- 設定済みの 1st / 2nd 写真フォルダをスキャンし、写真一覧を表示する。
- 標準グリッドとメイソンリーレイアウトを切り替える。
- ワールド、日付、タグ、お気に入り、不明ワールド、欠損ファイルなどで絞り込む。
- 表示中の画像は `ThumbnailWorker` 経由でサムネイルを非同期生成する。

### Photo Modal

- 選択写真の詳細、タグ、お気に入り、ワールド名、類似写真を表示・編集する。
- 類似検索は PDQ ハッシュのハミング距離で候補を出す。

### Tag Master and Template

- ワールドや写真に紐づくタグを管理する。
- 投稿用テンプレートを保存し、設定から有効なテンプレートを切り替える。

### World Resolve

- Polaris の `archive/output_log_*.txt` から入退室履歴を読み、撮影時刻と照合して世界名を補完する。
- PDQ 計算済みの既知ワールド写真を参照し、不明ワールド写真の候補を提示する。
- 自動確定は行わず、WorldResolve UI で確認した結果を `match_source` 付きで保存する。

### Settings

- 写真フォルダ、2nd 写真フォルダ、テーマ、表示モード、起動設定、投稿テンプレートを保存する。
- StellaRecord がインストール済みの場合はランチャーの `apps` テーブルへ Alpheratz を登録できる。

## Scan Flow

1. `PhotoScanner` が設定フォルダを列挙する。未設定時は `%USERPROFILE%/Pictures/VRChat` を既定候補にする。
2. `png`, `jpg`, `jpeg`, `webp`, `psd`, `xcf` を対象にし、再解析ループを避けるため訪問済みディレクトリを保持する。
3. 既存 DB のメタデータを読み、手動解決済みのワールドやタグを保持しながら写真情報を upsert する。
4. 欠損した既存写真は `is_missing = 1` として残す。
5. orientation / 画像サイズ / PDQ ハッシュをバックグラウンドで補完し、UI へ進捗イベントを送る。

## Persistence

- SQLite: `Data/db/Alpheratz.db`
- サムネイル: `Data/cache/1st-cache/imgCache`, `Data/cache/2nd-cache/imgCache`
- ログ: `Data/logs`
- 設定 JSON: `Data/db` 配下

詳細なテーブル定義は [database.md](database.md) を参照。

## Known Constraints

- Windows 10 Build 19041 以降の x64 環境を対象とする。
- macOS / Linux は対象外。
- Polaris archive は HKCU レジストリの `Software\CosmoArtsStore\Polaris` からインストール先を解決できる場合だけ参照する。
- StellaRecord 連携は HKCU レジストリの `Software\CosmoArtsStore\StellaRecord` から DB パスを解決できる場合だけ実行する。
