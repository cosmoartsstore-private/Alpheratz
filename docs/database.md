# Database

> Alpheratz の SQLite スキーマリファレンス。一次情報は `app/Core/Database/AlpheratzDb.cs`。

## Overview

| Item | Value |
| --- | --- |
| Engine | SQLite via `Microsoft.Data.Sqlite` |
| File | `Data/db/Alpheratz.db` |
| Tables | `photos`, `tags`, `photo_tags`, `archive_world_visits` |
| Indexes | 6 explicit indexes |
| PRAGMA | `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON` |

接続は短命で、各操作ごとに開いて破棄する。SQLite の接続プールを前提に、接続直後に WAL / NORMAL / FK ON を毎回適用する。

## Tables

### photos

写真 1 ファイルを 1 行で表す主テーブル。

| Column | Type | Nullable | Default | Description |
| --- | --- | --- | --- | --- |
| `photo_path` | TEXT | NO | - | 正規化済みファイルパス。主キー |
| `photo_filename` | TEXT | NO | - | ファイル名 |
| `world_id` | TEXT | YES | NULL | 画像メタデータから取得したワールド ID |
| `world_name` | TEXT | YES | NULL | ワールド表示名 |
| `timestamp` | TEXT | NO | - | 撮影時刻 |
| `last_modified_utc` | TEXT | YES | NULL | ファイル更新時刻 |
| `phash` | TEXT | YES | NULL | PDQ ハッシュの hex 文字列 |
| `phash_version` | INTEGER | YES | `0` | PDQ ハッシュの生成バージョン |
| `orientation` | TEXT | YES | NULL | 画像向き |
| `image_width` | INTEGER | YES | NULL | 画像幅 |
| `image_height` | INTEGER | YES | NULL | 画像高さ |
| `source_slot` | INTEGER | YES | `1` | 1st / 2nd 写真フォルダの識別 |
| `is_favorite` | INTEGER | YES | `0` | お気に入りフラグ |
| `match_source` | TEXT | YES | NULL | ワールド名解決元 |
| `is_missing` | INTEGER | YES | `0` | 再スキャン時にファイルが見つからなかった状態 |

### tags

タグマスタ。

| Column | Type | Nullable | Default | Description |
| --- | --- | --- | --- | --- |
| `id` | INTEGER | NO | AUTOINCREMENT | 主キー |
| `name` | TEXT | NO | - | タグ名。UNIQUE |

### photo_tags

写真とタグの多対多テーブル。

| Column | Type | Nullable | Description |
| --- | --- | --- | --- |
| `photo_path` | TEXT | YES | `photos.photo_path` を参照 |
| `tag_id` | INTEGER | YES | `tags.id` を参照 |

`PRIMARY KEY (photo_path, tag_id)`。新規 DB では `ON DELETE CASCADE` を付与する。既存 DB へ外部キー制約を後付けしないため、キャッシュリセット時に孤児行削除も行う。

### archive_world_visits

Polaris archive の VRChat ログから読み取ったワールド訪問履歴。

| Column | Type | Nullable | Default | Description |
| --- | --- | --- | --- | --- |
| `id` | INTEGER | NO | AUTOINCREMENT | 主キー |
| `source_log_name` | TEXT | NO | - | 元ログファイル名 |
| `world_name` | TEXT | NO | - | 入室したワールド名 |
| `join_time` | TEXT | NO | - | 入室時刻 |
| `leave_time` | TEXT | YES | NULL | 退室時刻。ログ上で確定できない場合は NULL |

## Indexes

| Index | Columns | Purpose |
| --- | --- | --- |
| `idx_photos_timestamp` | `photos(timestamp)` | 日付フィルタ、月別表示 |
| `idx_photos_world_name` | `photos(world_name)` | ワールド検索 |
| `idx_photos_is_favorite` | `photos(is_favorite)` | お気に入り抽出 |
| `idx_photos_is_missing` | `photos(is_missing)` | 欠損ファイル抽出 |
| `idx_archive_world_visits_join_time` | `archive_world_visits(join_time)` | 撮影時刻からの訪問履歴照合 |
| `idx_archive_world_visits_source_log_name` | `archive_world_visits(source_log_name)` | ログ単位の再投入 |

## Migration Strategy

`AlpheratzDb.EnsureSchema` は `CREATE TABLE IF NOT EXISTS` と `CREATE INDEX IF NOT EXISTS` で現行スキーマを作成し、既存 DB には `PRAGMA table_info` の列存在確認後に `ALTER TABLE ADD COLUMN` を実行する。

現行コードで追加列として扱うものは `orientation`, `image_width`, `image_height`, `source_slot`, `is_favorite`, `match_source`, `is_missing`, `phash_version`, `last_modified_utc`。旧実装の `photo_embeddings` は現行スキーマでは使わないため、残っていれば削除する。

スキーマバージョン番号テーブルは持たない。現状の変更は列追加と不要テーブル削除に収まっており、冪等な防御的マイグレーションで扱う。
