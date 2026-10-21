# Specification

Alpheratzの利用者操作と自動処理について、前提、判断、成功・部分成功・取消し・失敗後の結果を記録する。技術構造は[Architecture](architecture.md)、保存形式は[Database Schema](database.md)を参照する。

## Document Control

| Field | Value |
| --- | --- |
| Application | Alpheratz |
| Scope | 現行機能の条件、判断順、表示・保存・外部効果。内部構造、schema、開発・配布commandは各専門文書が所有する |
| Audience | 利用者、仕様を判断する人、機能を変更する人 |
| Authority | [ShellViewModel](../app/Features/Shell/ShellViewModel.cs)、[GalleryViewModel](../app/Features/Gallery/GalleryViewModel.cs)、[PhotoService](../app/Services/PhotoService.cs)、各featureのsource |
| Related documents | [README](../README.md)、[Architecture](architecture.md)、[Database Schema](database.md)、[Tech Stack](tech-stack.md)、[Basic Design](basic-design.html) |
| Source review date | 2026-10-22。sourceの静的照合日であり、実行試験日ではない |

## Scope

Alpheratzは利用者が指定した2つまでの写真フォルダをローカルで管理するWindows x64アプリケーションである。

- 写真の再帰走査、メタデータとPDQ保存、標準グリッド／メイソンリー閲覧
- 検索、filter、sort、world grouping、お気に入り、タグ、複数写真操作
- 投稿templateとX Web Intent、Polaris archiveとPDQ候補によるワールド名補完
- 完全一致写真の検出と確認後の原本削除、同じDB内へのお気に入り・タグ保存と復元
- 一般設定、サムネイル、logのローカル保存

クラウド同期、account、外部API投稿、online storage、写真原本編集、StellaRecord連携UIは対象外である。登録helperの存在を利用者向け連携機能とは扱わない。

## Terms

| Term | Meaning |
| --- | --- |
| 1st / primary | `source_slot = 1` の写真フォルダ |
| 2nd / secondary | `source_slot = 2` の写真フォルダ |
| Unknown world | `world_name`がnull・空・空白だけ、かつ`world_id IS NULL`の写真。補完対象の定義 |
| Known world | 空でない`world_name`を持つ写真 |
| Standard view | 固定6列を基準とする標準グリッド |
| Gallery view | 画像比率に応じたメイソンリー |
| PDQ | 256 bitの知覚hash。4回転variantを保存する。距離はハミング距離 |
| Display match rate | `round((1 - distance / 256) * 100)`の表示値 |
| Photo user data backup | 同じDB内にファイル名単位で保存するお気に入り・タグ。外部backupとは異なる |

## Startup and Shutdown

<a id="startup"></a>

1. 設定theme、WinUI resources、Window、DBを初期化し、Bootstrapにロゴと著作権を表示する。初期化の疑似percentageは表示しない。
2. 起動直後に1秒待って500 msでfade-inする。listener・設定を初期化し、未完了folder cleanupを写真一覧より先に再開する。
3. cleanup成功後、写真・tag・filter候補を読む。設定済みfolderのscanはbackgroundで始まり、Shell表示時に完了済みとは限らない。
4. Shell初期化とfade-in後の最低1.5秒を待ち、350 msでsplashをfade-outしてShellへ切り替える。
5. DB初期化失敗は起動失敗表示へ進む。pending cleanup失敗では旧写真を表示せず新scanを止め、次回起動やfolder操作で再試行する。

通常終了はWindowを閉じる操作である。完了済みの保存と未完了のbackground処理を区別し、通常終了が全taskの完了待ちを行う保証はない。終了時の実配線は[Concurrency and Lifecycle](architecture.md#concurrency-and-lifecycle)を参照する。

## Feature Specifications

### Photo Scan

| Item | Specification |
| --- | --- |
| Purpose and Entry | 設定済み写真folderを起動後などに走査し、検索用indexを更新する |
| Preconditions and Input | 最大2folder。`.png` / `.jpg` / `.jpeg`を大文字小文字無視で再帰列挙。同一・親子folderは拒否。設定済みfolderが存在しない場合は片方だけ続けずerrorで終了 |
| Decision and Processing | 完全列挙slotだけ欠落indexを整理し、新規・再出現・slot変更・内容変更を解析する。filenameだけの変更は既存metadataを保持してpath情報更新。初回解析記録のないPNGはmetadataだけ取得 |
| Success and Output | 撮影時刻、world、寸法、向き、処理件数を更新。通常成功時は終端進捗とscan完了を通知し、後処理を開始 |
| Persistence | 最大500写真ずつ保存。DB pathは`/`区切り。batchの詳細は[Write Transactions and Invariants](database.md#write-transactions-and-invariants) |
| Cancellation and Failure | scanの取消しは正常中止として通知。commit済みbatchは残る。読めないdirectoryやreparse pointを含むslotは不完全として欠落行削除を延期し、警告する |
| Source and Constraints | [PhotoScanner](../app/Core/Scanner/PhotoScanner.cs)。mtime比較による差分であり、同mtimeの任意の内容差まで検出する保証はない |

#### Inputs

除外directory名は`node_modules`、`vendor`、`cache`、`$recycle.bin`、`system volume information`、`thumbnails`。reparse pointは走査せずslotを不完全とする。

#### Change Detection

path、filename、source slot、更新時刻、欠落状態を比較する。mtime記録なし・不正は内容変更、記録との差が1秒を超える場合も内容変更とする。内容変更時は旧world情報とPDQを破棄して再構築するが、お気に入りと写真tagは保持する。

変更なしの写真は`match_source = unresolved`を含め再解析・DB書込みしない。PNGの初回解析でworld情報がなければ`unresolved`として「解析済み・情報なし」を記録する。

#### Metadata

VRChat filenameの撮影日時を優先し、取得不能ならlocal更新時刻、時刻取得例外なら現在時刻を使う。PNG XMP/iTXtのworld情報を採用した場合は`metadata`とする。画像headerから幅・高さ・向きを得て、JPEGはEXIF回転後の見た目で判定する。正方形はlandscape、補完時に読めない画像は`unreadable`となり、通常は繰り返し再解析しない。

#### Post-scan Workflow

Polaris archive→向き・寸法→PDQ→filter候補→一覧再読込の順で後処理する。保存は段階ごとに独立し、取消しや段階失敗でscan済み結果を戻さない。後処理完了は全段階成功の証明ではない。

### Gallery

| Item | Specification |
| --- | --- |
| Purpose and Entry | Shellの写真一覧で閲覧、検索、分類対象選択、表示切替を行う |
| Preconditions and Input | 可視index写真。header world入力、filter、sort、view mode、group、複数選択 |
| Decision and Processing | 検索・各filter領域はAND、複数worldはOR、複数tagはAND。標準は6列・表示領域4行を基準に行高を算出する。条件一致の写真は全件取得し、UIで仮想化する。galleryはresponsive masonry |
| Success and Output | 条件に合う写真、件数、月件数を表示。group drill-downはcardの写真を優先し、なければ同じworld名をDBから取得 |
| Persistence | view modeだけ設定へ保存し、保存成功後に切り替える。filter / sort / group / 選択はsession状態 |
| Cancellation and Failure | 旧一覧loadの遅延反映を世代で抑止。fallback group query失敗は空一覧。galleryへの切替時はgroupingをnoneへ戻す |
| Source and Constraints | [GalleryViewModel](../app/Features/Gallery/GalleryViewModel.cs)、[GalleryPhotosState](../app/Features/Gallery/GalleryPhotosState.cs)。groupingはStandardのみ |

#### Display Modes

Standardでは`none` / `world`のgroupingを選べる。trim後world名でまとめ、null・空・空白だけは同じ「不明なワールド」groupになる。Gallery viewではgroupingを利用できない。月移動は撮影日時順で利用する。

#### Search and Filters

| Control | Behavior |
| --- | --- |
| World search | 入力中は既読world候補を最大5件表示し、Enterまたは候補選択でtrim後の文字列を確定。入力だけでは写真queryを再実行しない |
| Date | 今日、直近7日、今月、先月、半年、1年、custom。終端日は23:59:59まで |
| Orientation | すべて、横長、縦長 |
| World | 複数worldのtrim後完全一致。unknown filterはworld名だけで判断し、補完対象のworld_id条件とは異なる |
| Tags | 指定したすべてのtagを持つ写真 |
| Favorite / Source | お気に入り、1st / 2ndのsource条件 |
| Sort | 撮影日時降順、world名昇順 |
| Grouping / Reset | Standardだけworld grouping。resetはsort / group / sourceも既定へ戻す |

検索文字列の`%`、`_`、backslashは文字として扱い、LIKE命令やcommand構文にはしない。

#### Photo Operations

単体favoriteはDB成功後だけ表示へ反映し、失敗時は旧表示を保持する。tagはtrim後1〜25文字で、空・大文字小文字無視の重複を拒否する。追加はmasterと写真関連を保存し、写真からの削除は関連だけを外してmasterを残す。

| Operation | Result and failure |
| --- | --- |
| 複数favorite / tag | 写真ごとに保存し、成功分だけ反映。失敗分を再試行用に選択へ残す。ただし操作中に選択世代が変われば新しい選択を上書きしない。API取消しは写真間で確認し、実行中の1写真は完了させる |
| 別folderへcopy | folder pickerで利用者が選んだ任意folderへ同じfilenameでcopyする。反対source slotへの自動固定はしない。同名fileは上書きせず個別skipし、成功・skip件数を通知する。元の選択を維持し、失敗写真だけへの絞込みは行わない |

copyのpicker取消しは無操作。copy結果は原本の別fileであり、favorite / tagの複製やコピー先index登録を同じ操作で確定するものではない。入口は[BulkCopy_Click](../app/Features/Gallery/GalleryPage.xaml.cs)→SettingsViewModelのfolder picker→PhotoServiceである。

### Photo Details

| Item | Specification |
| --- | --- |
| Purpose and Entry | Gallery / drill-downから写真詳細を開き、拡大、前後移動、favorite / tag、外部操作を行う |
| Preconditions and Input | 選択写真と現在の表示list。有効world IDとactive templateは対応する外部操作の前提 |
| Decision and Processing | 左右keyまたは画像端で渡されたlist内を移動。tag masterから複数追加・付与済みtag削除。world IDがないworld操作は無操作 |
| Success and Output | 写真、world名、時刻、filename、tagを表示。Explorerは原本を選択表示し、worldはVRChat pageを開く |
| Persistence | favorite / tagはGalleryと同じ保存経路。詳細表示・前後履歴自体は保存しない |
| Cancellation and Failure | 写真切替・closeで補助tag取得を取消し、適用時pathも照合。closeで選択・履歴を消す。外部起動失敗は通知 |
| Source and Constraints | [PhotoModalState](../app/Features/PhotoModal/PhotoModalState.cs)、[WorldService](../app/Services/WorldService.cs)。画像解放の入口別範囲は[Architecture](architecture.md#concurrency-and-lifecycle) |

### Settings

設定は最大1120×820 pxのmodal内で左navigationから1sectionずつ表示する。General、Duplicate Photos、Tags、Templates、Creditsはそれぞれ別の機能目的を持つ。

#### General

| Item | Specification |
| --- | --- |
| Purpose and Entry | Generalで1st / 2nd folder、startup、投稿時world page、Light / Dark、補完起動を管理する |
| Preconditions and Input | folder pickerで存在するdirectoryを選択。1st / 2ndの同一・親子関係は禁止。World Resolve起動はPDQ未解析件数0が前提 |
| Decision and Processing | 未設定slotの初回folder設定は直接適用。既存pathの変更とresetは確認後に実行。同一folder選択とpicker取消しは無操作 |
| Success and Output | 対象slotのindex / cacheを整理して新設定を適用。resetはfolderを未設定へ戻す。原本は削除しない |
| Persistence | pathとpending cleanupを同時保存。theme / viewは保存成功後UI反映。startupはRun登録とJSONを調整 |
| Cancellation and Failure | 確認後にpathが変われば旧確認を適用しない。保存前失敗は旧設定を保持、保存後のcleanup失敗はpendingを残す。startup / 投稿world設定の保存失敗はSettings VMを旧値へ戻す |
| Source and Constraints | [ShellPage](../app/Features/Shell/ShellPage.xaml.cs)、[ShellViewModel](../app/Features/Shell/ShellViewModel.cs)。StellaRecord登録UIは非表示 |

#### Tag Master

| Item | Specification |
| --- | --- |
| Purpose and Entry | Tagsで候補tagの登録・削除を行う |
| Preconditions and Input | trim後1〜25文字。大文字小文字無視の重複を拒否 |
| Decision and Processing | 追加はmasterへ保存。削除はmasterと全写真の関連を1transactionで削除 |
| Success and Output | master一覧と写真tagを更新 |
| Persistence | `tags`と`photo_tags`。写真原本は変更しない |
| Cancellation and Failure | DB失敗はdraft / 一覧保持。保存後の再読込だけ失敗した場合は現在一覧へ結果を補完し、再試行可能な状態を保つ |
| Source and Constraints | [TagMasterViewModel](../app/Features/TagMaster/TagMasterViewModel.cs)。過去の付与情報backupにはtag名が残り、復元で再作成され得る |

#### Duplicate Photos

| Item | Specification |
| --- | --- |
| Purpose and Entry | Duplicate Photosで完全一致原本を検出し、選択・確認後に削除する |
| Preconditions and Input | DB登録済み可視写真。各groupに1枚以上を未選択で残す。初期選択はなし |
| Decision and Processing | file size→PDQ候補→SHA-256。同size群の全PDQが有効なら回転variant集合で絞り、不正・未算出があれば群全体を比較。SHA-256一致2枚以上を容量順に表示 |
| Success and Output | count / size確認後、登録・slot・size・hash・保持写真を再検証して原本削除。削除済みpathのcache / DBを整理 |
| Persistence | 原本変更が先、DB整理が後。原本とSQLiteをまたぐtransactionはない |
| Cancellation and Failure | 読めない・変化した写真はskipし再検出できる。保持確認不能groupは削除しない。DB整理失敗は原本削除失敗と区別。既削除原本は戻らない |
| Source and Constraints | [PhotoService](../app/Services/PhotoService.cs)、[DuplicatePhotosViewModel](../app/Features/Settings/DuplicatePhotosViewModel.cs)。現行画面は検出 / 削除用CancellationTokenを渡さず、serviceの取消し対応を画面cancel機能とは扱わない |

保持写真は書換え・削除を許可しないread handleを処理中保持する。`StorageDeleteOption.Default`は保存先によってごみ箱を使わず完全削除になる。似ているだけの写真は対象にしない。

#### Photo User Data Backup

| Item | Specification |
| --- | --- |
| Purpose and Entry | Generalの保存 / 復元を確認後に実行し、folder再読込後も付与情報を使えるようにする |
| Preconditions and Input | 現在の全可視写真。filename単位でfavorite / tagを扱い、path・slot・内容hashをkeyにしない |
| Decision and Processing | 保存は同名favoriteをOR、tagを和集合に統合。0写真なら既存backup保持、1件以上なら全置換。復元は同名の全可視写真へfavorite上書き・tag追加 |
| Success and Output | 現在tagは削除せず、不足masterを作成。一致写真なしは無操作 |
| Persistence | 同じDBの`photo_user_data_backup`に操作全体を1transactionで保存。外部fileは生成しない |
| Cancellation and Failure | DB操作失敗時はtransactionを戻す。処理中は両buttonをdisable。folder resetはbackupを消さない |
| Source and Constraints | [Database](../app/Core/Database/AlpheratzDb.cs)。異なる内容でも同filenameなら反映され、DB削除・uninstallでは失う。原本・world・PDQ・設定は対象外 |

#### Post Templates

| Item | Specification |
| --- | --- |
| Purpose and Entry | Templatesで追加・編集・削除・使用対象選択、PhotoModalで投稿準備 |
| Preconditions and Input | active template、入力実長と加重長がともに280以下。trim後重複を拒否 |
| Decision and Processing | NFC後、通常文字1、日本語等2、emoji sequence2、URL23で独自計算。placeholderがあり残り20以下で注意。展開した最終本文も280以下を確認 |
| Success and Output | 本文生成→画像clipboard→X Web Intent→設定有効かつ有効world IDならVRChat pageの順 |
| Persistence | template配列とactiveをJSONへ保存し、成功後だけ一覧確定・draft消去 |
| Cancellation and Failure | 保存失敗は旧保存値と入力を保持し同じ入力で再試行可。本文超過・生成失敗は画像copy / browser起動なし。clipboard失敗ならIntentなし。Intent失敗後もclipboardは残る。world pageだけ失敗してもIntentは既に開いている |
| Source and Constraints | [TemplatePageViewModel](../app/Features/Template/TemplatePageViewModel.cs)。独自加重計算はXサービスとの完全一致保証ではない。Web Intentへ画像は直接添付せず利用者が貼り付ける |

| Token | Value |
| --- | --- |
| `{world}`, `{world-name}`, `{world_name}` | world名。不明なら利用者向け不明表示 |
| `{world_id}` | VRChat world ID |
| `{date}` | `yyyy-MM-dd HH:mm`相当 |
| `{timestamp}` | 保存済みtimestamp |
| `{file}` | filename |
| `{memo}` | 現行では空文字 |
| `{tags}` | 空白を除去した`#tag`列 |

Creditsは明示clickで外部linkを既定browserへ開く。通常利用に外部API認証・telemetry・写真同期はない。

### World Resolution

#### Archive Resolution

| Item | Specification |
| --- | --- |
| Purpose and Entry | post-scanでPolaris訪問logから未知写真のworld名を自動補完する |
| Preconditions and Input | HKCUのPolaris `InstallLocation/Data/archive`にある非圧縮`output_log_*.txt`。存在しなければ0件 |
| Decision and Processing | filename順で`Entering Room` / `OnLeftRoom`を訪問区間化し、撮影時刻を含む最新joinを採用。次のEnteringだけでは前区間を閉じずleave=null |
| Success and Output | 未知写真のworld名と由来`polaris_archive`を更新。既知world、favorite、tag、元logを保持 |
| Persistence | 訪問作業tableを全置換。写真補完は別保存単位 |
| Cancellation and Failure | 個別log読取失敗は警告して続行し、読めた訪問情報を使う。Polaris起動や外部API呼出しはしない |
| Source and Constraints | [AppPaths](../app/Core/AppPaths.cs)、[PhotoScanner](../app/Core/Scanner/PhotoScanner.cs)、[WorldService](../app/Services/WorldService.cs) |

#### PDQ-assisted Confirmation

| Item | Specification |
| --- | --- |
| Purpose and Entry | GeneralからWorld Resolveを開き、未知写真の候補を比較して確定する |
| Preconditions and Input | PDQ未解析0。未知写真は有効PDQを持ち、候補は同slotの既知world写真 |
| Decision and Processing | 4回転の最短距離75以下、表示71%以上を候補提示。world名を大文字小文字無視で重複排除し、距離・world名順。候補なしでもtrim後空でない手入力を許可 |
| Success and Output | preview、別候補、全適用 / 全skip / 個別切替後、選択分だけ確認保存。候補由来は`phash_confirmed`、手入力は`manual` |
| Persistence | 写真ごとの独立更新。手入力でworld IDを新規入力せず、null ID指定は既存IDを保持 |
| Cancellation and Failure | 候補を見るだけではDBを変えない。途中失敗までの保存は残り、全件rollback・失敗分だけの再選択は保証しない。適用中はcloseを抑止し、探索中closeは取消し・完了待ちを行う |
| Source and Constraints | [WorldResolveViewModel](../app/Features/WorldResolve/WorldResolveViewModel.cs)。候補外は自動確定しない。自動更新service APIは現行UI / post-scanから呼ばない |

## Workflows and State Transitions

| Workflow | State transition and confirmed effects |
| --- | --- |
| folder change / reset | 既存pathの確認→scan停止待ち→新pathまたは空pathとpending保存→生成停止待ち→対象slot DB / cache整理→pending解除→再読込。途中終了後は次回起動でpendingから再実行 |
| scan / enrichment | 欠落整理→差分解析batch→scan完了→archive→向き→PDQ→候補・一覧更新。既に保存した段階は後段失敗で戻さない |
| duplicate delete | 保持を残す選択→確認→進行処理停止→原本再検証→原本削除→cache / DB整理。DB失敗で原本を復元しない。後の完全scanは欠落indexを整理できる |
| user data backup / restore | 確認→1transactionで保存全置換または復元→結果表示。外部backupや原本の複製にはならない |

folder操作はDB整理失敗時にpendingが残る一方、cache削除だけの失敗は警告としてDB成功を維持し、pendingが解除され得る。技術的なgate / task / eventは[Main Data Flows](architecture.md#main-data-flows)、保存単位は[Database Schema](database.md)が正本である。

## Concurrency and Failure Handling

- 操作の完了は対応する保存単位の結果であり、scan・enrichment・外部起動を一括でrollbackしない。
- 複数favorite / tagとWorld Resolveは写真単位、folder cleanupと重複削除は原本・index・設定の効果を別に扱う。
- 古い一覧load、候補探索、補助tag取得はそれぞれ世代やpathを確認する。全UI / 画像control / cache / DBへの共通のstale防止保証ではない。
- 操作結果・回復可能な失敗はtoast、入力validationは局所表示、破壊的操作は確認dialogを使う。
- 通常logはFatal、明示verbose時だけError / Warn / Info、Traceはverboseと`TRACE_LOGGING` buildの双方が必要。保存契約は[Data Storage](database.md#storage-and-ownership)を参照する。

## External Effects and Validation

- 写真folder、原本、Polaris archiveは実行Windows userの権限で読む。原本削除は重複写真で選択・確認した対象だけ、copyは選択folderへ行う。
- SQL値はparameter bind。VRChat URLは`wrld_`後の英数字・`-`・`_`だけを許可する。
- 投稿IntentはHTTPS、`twitter.com` / `x.com`、`/intent/tweet`、`text` queryに限定。ExplorerはWindows directoryの実行fileへ`/select,<path>`を1引数で渡す。
- installerは不足VC++ runtimeをapp置換前に導入する。通常uninstallは標準Data/db・logs・cacheを削除し、写真原本と共有VC++ runtimeは保持する。

## Current Constraints

- Windows build19041以降、x64、日本語UIのみ。製品code signingはなくSmartScreen警告が出る場合がある。
- Gallery viewのgrouping、画像のWeb Intent直接添付、StellaRecord登録UIは提供しない。
- 画像処理の取消しと解放は入口ごとに実装する。WorldResolve shutdown、PhotoModal通常close、thumbnail生成取消しを、あらゆるunload後のSource更新防止へ一般化しない。
- 通常終了時の全background task完了待ちは接続されていない。DB内付与情報backupはDB削除・uninstallへの保護ではない。

## References

<a id="runtime-architecture"></a>
<a id="persistence-and-events"></a>

内部構造・状態・eventの旧節は[Architecture](architecture.md)へ移した。保存形式・保持は[Database Schema](database.md)、versions・開発・CI・配布は[Tech Stack](tech-stack.md)、画面構造は[Basic Design](basic-design.html)を参照する。
