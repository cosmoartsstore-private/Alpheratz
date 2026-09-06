# Agent Notes

この文書は、現行実装に対して再検証済みのproject-local contractだけを保持する。日付付きの作業履歴、解決済み課題、生成reportの場所は記録しない。

## Authority and Scope

- 仕様が変化している間はproduction sourceを正本とする。
- Publicな現在仕様は `README.md` と `docs/` に置く。この文書へ利用者向け仕様を複製しない。
- Framework boundaryのcoverage除外理由は `CoveragePolicyTests.CoverageBoundaryJustification` と完全一致させ、allowlistを同時に更新する。
- WinUI startup、XAML resource、Release publishを変更する前に `xaml-packaging-notes.md` を読む。

## Current UI Contracts

- Shellは44 pxの`ShellHeaderBar`と`ShellStage`で構成する。Galleryはmain content、Settings／WorldResolve／GroupDrillDownはmiddle modal、PhotoModalはtop modalである。
- Shell overlayの判断は `ShellOverlayState` を正本とする。個別flagはframework viewの実状態を集める入力であり、header interactivity、Esc、dismissの分岐を別々に再実装しない。
- Filter panelは`ShellPage.FilterOverlay`の`FilterPanelContainer`に固定配置し、幅612 pxを維持する。表示時にparentを付け替えない。
- Header world searchはplain textのみ。入力中にDB検索せず、Enterで確定する。command prefix、quick command menu、`SearchCommandParser`を追加しない。
- Standard viewだけworld groupingを許可する。null、空文字、空白だけのworldは同じunknown groupにする。
- Group drill-downはclicked cardの`GroupPhotos`を優先し、存在しない場合だけDB fallbackを使う。
- PhotoModalの前後移動はkeyboard左右keyと画像端のhover affordanceを使う。円形button chrome、履歴back button、match source／source slot badgeは表示しない。
- Photo cardはvirtualizationで再利用されるため、favorite等のvisual stateを`Loaded`だけでなく`DataContextChanged`でも同期する。
- Settingsは最大1120×820 px、左navigationでGeneral／Tags／Templates／Creditsを切り替え、選択sectionだけを表示する。旧single-column 760 px構成へ戻さない。
- Tag上限はGallery、PhotoModal、TagMasterを通して25文字。投稿templateは実文字数とX加重文字数の双方で280以下とする。
- Bootstrapはlogoとcopyrightだけを表示する。phase percentage、progress bar、補間timerを戻さない。
- 視覚変更は現行resource tokenとlayout directionを維持し、long world/tag/file名と狭いwindowで確認する。

## Current Behavior Contracts

- Folder change/resetはscan、post-scan、thumbnail生成の停止完了を待つ。対象slotのDB/cache cleanup要求を`pendingFolderCleanup`として先に保存し、終了後も次回起動で再実行できる状態を保つ。
- 1stと2ndに同一folderまたは親子folderを設定しない。scan時も重複を再検証する。
- Scan完了後はarchive resolution、orientation、PDQ、filter metadata refreshを直列実行し、最後に`scan:enrich_completed`を発行する。
- PDQ候補のUI採用閾値はdistance 75以下、表示一致率71%以上。World Resolveは同じsource slotの候補だけを表示し、利用者の確定を必須とする。
- `WorldService.ResolveUnknownWorldsFromSimilarPhotosAsync`は自動DB更新可能なservice APIとして存在するが、現行UIとpost-scan workflowは呼ばない。UIを自動更新経路へ戻す変更と混同しない。
- World Resolveのcandidateが閾値外または存在しない場合もmanual world nameを入力できる。確定由来は自動候補が`phash_confirmed`、manualが`manual`である。
- `openWorldLinkOnPost`は投稿画面を開いた後、写真に有効な`world_id`があるときだけVRChat pageを開く永続設定である。
- AppLoggerは通常`Fatal`だけを保存する。`ALPHERATZ_VERBOSE_LOGS=1`でError／Warn／Infoを有効化し、Traceは`TRACE_LOGGING` buildに限る。queueは512行、messageは8000文字、`info.log`は1 MiBで1世代rotateする。
- `UiObservableCollection`と`UiThreadSafeObservableObject`はnotificationだけをmarshalする。UI-bound mutationは`DispatcherService.RunOnUiThread`へ明示的に戻す。

## Data Compatibility Kept by Current Source

- Gallery queryは常に`is_missing = 0`を条件にするが、現行scanは完全に列挙できたslotの欠落行を物理削除する。`is_missing`はcompatibility columnとして残っている。
- `phash_version`はschemaに存在するが現行PDQ保存で更新しない。画像内容変更時は0へ戻す。
- 新規`photo_tags`には`ON DELETE CASCADE`がある。既存tableへCASCADEを後付けしないため、tag deleteとslot resetは関連行を明示削除する。
- `Initialize()`は不足columnを追加し、旧`photo_embeddings` tableを削除する。`user_version`や独立migration frameworkは使わない。

## Confirmed Maintenance Scope

- field-based `[ObservableProperty]`が残っており、`MVVMTK0045`はprojectで抑止している。NativeAOTへ移行する場合はpartial property化を一括で扱う。warningだけを局所的に戻さない。
- Public method名にはlowerCamelが残る。外部API境界ではなくrepository内の広範なrenameになるため、機能変更へ混ぜない。
- Distributionはcode signingを持たない。実装済みと記述しない。

これらは現行runtimeの既知障害を示す一覧ではない。変更要求がない限り、architecture migrationや命名統一へscopeを広げない。

## Verification

- Source変更後は `dotnet test tests\Alpheratz.Tests\Alpheratz.Tests.csproj` を実行する。
- `.github\workflows\ci.yml`はmainへのpushとmain宛PRでtestと最終installer生成をhard gateにする。
- XAML／startup／packaging変更では、unit testに加えて `BuildWorks\scripts\build-release.ps1` とinstalled launcher経由の起動確認が必要である。
- UI確認ではlong tag、long world、unknown world、virtualized card再利用、640 px程度の狭いwindowを含める。
