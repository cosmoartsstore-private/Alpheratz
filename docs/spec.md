# Specification

この文書は、現行の .NET / WinUI 3 実装から確認できる Alpheratz の機能仕様を記録する。将来案、旧 WebView 実装、完了済みの改修履歴は含めない。

## Scope

Alpheratz は、利用者が指定した2つまでの写真フォルダをローカルで管理する Windows x64 デスクトップアプリケーションである。

対象範囲は次のとおり。

- 写真ファイルの再帰走査とメタデータ保存
- 標準グリッド／メイソンリーによる閲覧
- 検索、フィルタ、並べ替え、ワールド単位のグループ化
- お気に入り、タグ、複数写真への一括操作
- 投稿テンプレートと Twitter/X Web Intent の起動
- Polaris archive と PDQ 類似度を使ったワールド名補完
- 設定、サムネイル、ログのローカル保存

現行範囲に含まれないものは、クラウド同期、アカウント、外部 API による投稿、オンラインストレージ、写真原本の編集、StellaRecord 連携 UI である。

## Terms

| Term | Meaning |
| --- | --- |
| 1st / primary | `source_slot = 1` の写真フォルダ |
| 2nd / secondary | `source_slot = 2` の写真フォルダ |
| Unknown world | `world_name` が null・空・空白だけで、かつ `world_id IS NULL` の写真 |
| Known world | 空でない `world_name` を持つ写真 |
| Standard view | 固定6列を基準にした標準グリッド |
| Gallery view | 画像比率に応じたメイソンリーレイアウト |
| PDQ | 256 bit の知覚ハッシュ。0°、90°、180°、270°の4バリアントを `|` 区切りで保持する |
| Match distance | PDQ 間のハミング距離。小さいほど近い |
| Display match rate | `round((1 - distance / 256) * 100)` で表示する一致率 |

## Runtime Architecture

```text
Alpheratz.exe (launcher)
└─ Alpheratz.Frontend.exe
   ├─ App / MainWindow / ShellPage
   ├─ Features
   │  ├─ Gallery
   │  ├─ PhotoModal
   │  ├─ Settings + TagMaster + Template
   │  └─ WorldResolve
   ├─ Services
   │  ├─ Photo / Thumbnail / Settings
   │  ├─ World / PDQ / Orientation
   │  └─ Dialog / Toast / Dispatcher
   └─ Core
      ├─ PhotoScanner / PDQ imaging
      ├─ AlpheratzDb
      ├─ LocalEventBus
      └─ AppConfig / AppPaths / AppLogger
```

プロセスは frontend の単一 .NET プロセスである。依存関係は起動時に DI コンテナーへ singleton として登録する。WinUI に結び付く状態変更は `DispatcherService` を介して UI スレッドへ戻す。

## Startup

1. Debug は明示的な Windows App SDK bootstrap、Release は self-contained の reg-free WinRT 初期化を行う。
2. WinUI resources、DI、SQLite schema、`MainWindow` を初期化する。
3. `BootstrapPage` にロゴと著作権表示を出す。起動直後に1秒待ち、500 msでフェードインする。
4. 設定、イベント購読、未完了のフォルダ整理、写真・タグ・フィルタ候補を初期化する。
5. 写真フォルダが設定済みなら、写真走査をバックグラウンドで開始する。
6. Shell の初期化が完了し、フェードイン完了後から最低1.5秒を経過したら、350 msでスプラッシュをフェードアウトして `ShellPage` へ切り替える。

スプラッシュには初期化フェーズやパーセントを表示しない。

## Photo Scan

### Inputs

- 1st と 2nd のうち、存在する設定済みフォルダ
- 対応拡張子: `.png`, `.jpg`, `.jpeg`
- 除外ディレクトリ名: `node_modules`, `vendor`, `cache`, `$recycle.bin`, `system volume information`, `thumbnails`

1st と 2nd が同一ディレクトリ、または一方が他方の配下である場合は走査を開始しない。ディレクトリの再訪を検出し、シンボリックリンク等による循環走査を防ぐ。

### Change Detection

既存レコードとファイルのパス、ファイル名、source slot、更新時刻、欠落状態を比較する。

- 新規、再出現、slot 変更、内容変更: 対応する全写真を初回解析する。
- ファイル名だけの変更: 既存メタデータを保持してパス情報を更新する。
- ワールド情報も解析結果も未記録の PNG: 未完了の初回解析として metadata だけを取得する。
- 変更なし: `match_source = unresolved` を含めて再解析せず、DB 書込みも行わない。

内容変更を検出した場合は、旧画像に属する可能性があるワールド情報と PDQ を破棄し、新しい画像から再構築する。
初回解析で PNG 内にワールド情報が無い場合は `match_source = unresolved` とし、「解析済み・情報なし」を表す。通常スキャンでは再試行しない。

### Metadata

- VRChat ファイル名から撮影日時を取得する。取得できない場合はファイル時刻を使う。
- PNG の XMP/iTXt から `world_name` と `world_id` を取得できた場合、`match_source = metadata` とする。
- 画像ヘッダーから幅、高さ、向きを取得する。JPEG は EXIF 回転後の見た目に合わせて縦横を判定する。読めない画像は再試行を繰り返さない状態として扱う。
- DB の写真パスは `/` 区切りへ正規化する。

画像解析は論理 CPU 数の半分を基準に1〜4並列で実行し、最大500件を1 DB batch とする。進捗通知は100 ms間隔を上限とし、最後に必ず `processed = total` を発行する。並列処理のため、中間通知に含まれるワールド名の順序は保証しない。

フォルダを完全に列挙できた slot だけ、今回見つからなかった DB 行を削除する。アクセス拒否や I/O エラーで不完全だった slot は削除を延期し、警告する。

### Post-scan Workflow

`scan:completed` 後は、DB 書込みが競合しないよう次の処理を直列実行する。

1. Polaris archive による未知ワールド補完
2. 未確定の向き・寸法の補完
3. 未計算 PDQ の生成
4. ワールド候補・タグ件数の再読込
5. `scan:enrich_completed` の発行とギャラリー再読込

## Gallery

### Display Modes

- **Standard**: 固定6列を基準とするグリッド。ページサイズは表示領域から4行分を算出する。
- **Gallery**: 表示幅に応じたメイソンリー。グループ化は利用できない。
- 表示モードは `setting.json` に保存する。

標準表示では `none` または `world` のグループ化を選べる。null、空文字、空白だけのワールド名は同じ「不明なワールド」グループにまとめる。グループカードが保持する写真一覧をドリルダウンへ引き継ぎ、保持されていない場合だけ DB から同じワールド名を完全一致検索する。

### Search and Filters

| Control | Behavior |
| --- | --- |
| World search | 部分一致候補を最大5件表示し、Enter で前後空白を除いた文字列を確定する |
| Date | 今日、直近7日、今月、先月、半年、1年、カスタム範囲 |
| Orientation | すべて、横長、縦長 |
| World | 複数ワールドの完全一致 |
| Tags | 複数タグ。指定したタグをすべて持つ写真を対象にする |
| Favorite | お気に入りだけを表示 |
| Sort | 撮影日時の降順、ワールド名の昇順 |
| Grouping | なし、ワールド。Standard のみ |

検索文字列の編集だけでは DB を再読込しない。Enter 確定後に `WorldQuery` を更新する。コマンド検索構文は持たない。

### Photo Operations

- カードのお気に入りを切り替える。
- 写真へ25文字以内のタグを1件または複数追加し、個別に削除する。
- 複数選択では、お気に入り設定、複数タグ追加、反対側の写真フォルダへのコピーを行う。
- 一括操作は写真ごとの成功・失敗を返す。成功分だけ画面状態へ反映し、失敗分だけを再試行用に選択状態へ残す。
- コピー先に同名ファイルがある場合は上書きしない。

## Photo Details

写真詳細は Shell の modal layer に表示する。

- 選択写真、ワールド名、撮影日時、タグを表示する。
- 左右キーまたは画像端のナビゲーションで、現在の表示リスト内を前後移動する。
- お気に入りを切り替える。
- タグマスタから未付与タグを複数選択して追加し、付与済みタグを削除する。
- Explorer の `/select,<path>` で原本を選択表示する。
- 有効な `wrld_` ID がある場合は VRChat ワールドページを開く。
- アクティブな投稿テンプレートがある場合は投稿準備を実行する。

投稿準備では画像を Windows clipboard に置き、本文だけを `https://twitter.com/intent/tweet?text=...` で開く。画像は Web Intent に直接添付されないため、利用者が投稿画面へ貼り付ける。設定が有効で `world_id` がある場合は、投稿画面の後に VRChat ワールドページも開く。

## Settings

設定は最大1120×820 pxの modal 内で、左ナビゲーションから1セクションずつ表示する。

### General

- 1st / 2nd 写真フォルダの選択とリセット
- Windows ログイン時の自動起動
- 投稿画面を開くときの VRChat ワールドページ表示
- Light / Dark テーマ
- ワールド名補完の起動と PDQ 解析進捗

ワールド名補完ボタンは PDQ 未解析件数が0になるまで無効である。StellaRecord の登録 UI は現行画面では非表示である。

フォルダ変更またはリセットは確認後に実行する。進行中の scan と post-scan、共有サムネイル生成を停止し、対象 slot の DB 行とキャッシュを削除してから設定を再読込し、新しい scan を開始する。整理要求は設定と同時に保存し、途中で終了した場合は次回起動時に再実行する。

### Tag Master

- 前後空白を除いた1〜25文字のタグを登録する。
- 大文字小文字を無視した重複は登録しない。
- タグ削除時は `photo_tags` の関連付けも削除する。

### Post Templates

- テンプレートの追加、編集、削除、アクティブ選択を行う。
- 入力欄の実文字数と X 互換の加重文字数が、どちらも280以下である必要がある。
- NFC 正規化後、一般的な文字は1、日本語等と emoji sequence は2、URLは23として数える。
- 置換後の超過に備え、プレースホルダーを含み残り20文字以下の場合は注意を表示する。
- 投稿準備時に写真情報とタグを置換した最終本文を再計算し、280を超える場合は画像コピーと投稿画面の起動を行わない。

対応プレースホルダー:

| Token | Value |
| --- | --- |
| `{world}`, `{world-name}`, `{world_name}` | ワールド名。不明な場合は利用者向けの不明表示 |
| `{world_id}` | VRChat world ID |
| `{date}` | `yyyy-MM-dd HH:mm` 相当 |
| `{timestamp}` | 保存済み timestamp |
| `{file}` | ファイル名 |
| `{memo}` | 現行では空文字 |
| `{tags}` | 空白を除いた `#tag` の列 |

## World Resolution

### Archive Resolution

Polaris の `HKCU\Software\CosmoArtsStore\Polaris\InstallLocation` から `archive` を探す。`output_log_*.txt` をファイル名順に読み、`Entering Room` と `OnLeftRoom` から訪問区間を保存する。未知写真の撮影時刻が訪問区間に含まれる場合、`world_name` を保存して `match_source = polaris_archive` とする。

### PDQ-assisted Confirmation

対象は、未知ワールドかつ有効な PDQ を持つ写真である。候補は同じ `source_slot` の既知ワールド写真だけから選ぶ。

- 既知候補の4回転ハッシュは先に packed 形式へ変換し、未知写真ごとに再パースしない。
- 同一 `source_slot + phash` の未知写真は同じ解析結果を共有する。
- 候補探索は UI 用に1論理 CPUを残し、最大12並列で実行する。
- 進捗通知は125 ms間隔を下限とする。
- 距離75以下だけを自動候補として表示する。表示上は71%以上に相当し、距離76は70%となる。
- 候補一覧はワールド名を大文字小文字無視で重複排除し、距離、ワールド名の順に並べる。
- 閾値内候補がない場合も、ワールド名を手入力できる。
- 対象写真と候補写真を拡大プレビューできる。
- すべて適用、すべて適用しない、個別切替の後、選択分だけを確定保存する。

自動候補の確定は `match_source = phash_confirmed`、手入力は `match_source = manual` とする。UI は利用者の確定なしに PDQ 候補を DB へ書き込まない。`WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync` という自動更新可能なサービスメソッドは存在するが、現行 UI と起動後フローからは呼び出さない。

画面を閉じるときは候補探索、候補サムネイル、UI 更新の停止完了を待ち、閉じた画面へ遅延結果を反映しない。

## Persistence and Events

永続データの場所は [Database Schema](database.md) と [README](../README.md#data-and-privacy) を参照する。

主要イベント:

| Event | Payload | Meaning |
| --- | --- | --- |
| `scan:progress` | `ScanProgressDto` | 走査件数と現在表示用情報 |
| `scan:completed` | null | 写真メタデータの取込完了 |
| `scan:warning` | string | 続行可能な警告 |
| `scan:cancelled` | string | 利用者操作または画面遷移による正常な中止 |
| `scan:error` | string | 走査エラー |
| `orientation_progress` | `OrientationProgressEvent` | 向き・寸法補完進捗 |
| `orientation_complete` | null | 向き・寸法補完完了 |
| `phash_progress` | `PhashProgressEvent` | PDQ 解析進捗 |
| `phash_complete` | null | PDQ 解析完了 |
| `scan:enrich_completed` | null | post-scan とフィルタ情報更新の完了 |

UI 文言は `app/Messages/messages.ja.properties` を正本とする。`{{` と `}}` は properties 内でリテラルの波括弧を表す。未定義キー、不正なキー、重複キー、必要な置換値の不足はエラーとする。

## Concurrency and Failure Handling

- SQLite 接続は操作ごとに開き、connection pooling を利用する。書込み use case はサービス側の gate またはワークフロー順序で直列化する。
- 写真 scan は500件単位、PDQ 更新は30件単位で transaction にまとめる。
- PDQ は30件を1 chunk とし、CPUの半分を基準に1〜8並列、1 batchあたり2 waveを処理する。
- サムネイル生成は共有 worker 2本で処理し、写真ごとの無制限な `Task.Run` は作らない。
- 読み込みは UI スレッドを占有しない。UI collection と property の変更は Dispatcher へ戻す。
- キャンセル、世代番号、完了待ちにより、画面遷移後の stale update と削除済み cache の再生成を防ぐ。
- 通常ログは fatal のみ。詳細ログは `ALPHERATZ_VERBOSE_LOGS=1` で明示的に有効化する。

## External Effects and Validation

- DB SQL は値を parameter bind する。
- VRChat URL は `wrld_` に続く英数字、`-`、`_` だけを許可する。
- 投稿 Intent は HTTPS、`twitter.com` または `x.com`、`/intent/tweet`、`text` queryだけを許可する。
- Explorer は Windows directory 配下の `explorer.exe` を直接起動し、対象パスを `ArgumentList` の1引数として渡す。
- 利用者が選んだ写真フォルダ、写真ファイル、Polaris archive は、その Windows ユーザーのアクセス権で読む。
- 自動削除は、確認済みの source slot に属する DB 行・thumbnail cache、または installer 管理下のファイルに限定する。写真原本は削除しない。
- Installerは同梱したMicrosoft Visual C++ Redistributable（x64）の必要versionを確認する。不足または旧versionの場合はUAC承認後にmachine-wide runtimeを導入してからappを置き換える。

## Current Constraints

- Windows 10 Build 19041以降、x64のみ。
- UI と利用者向け文言は日本語のみ。
- code signing は行っていないため、配布物で SmartScreen 警告が出る場合がある。
- Web Intent は画像を添付できない。画像は clipboard から手動で貼り付ける。
- Gallery view ではワールドグループ化を利用できない。
