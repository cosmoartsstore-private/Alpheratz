# Audit Disposition Memo (最終更新: 2026-05-21)

3 段階監査 (検出 30 → 検証 30 → 三段精査 30) + 修正実施 + 新規調査 5 件 + 2026-05-21 第 2 ラウンド (15 領域 → 60 指摘 → 8 体検証) の結論を記録。
**このファイルは Claude の自己参照用メモ。再監査時に同じ指摘で時間を浪費しないために残す。**

---

## 凡例

- **対応必要**: 実コードに影響あり、修正推奨
- **対応不要**: false positive、または影響が運用上ゼロ
- **要観察**: 現状問題なし、規模拡大時に再評価

---

## ✅ 修正実施 (10 件) / 設計通り (2 件)

| ID | 場所 | 状態 |
|---|---|---|
| FIX-01 | `app/App.xaml.cs:259-273` | **DONE** — 失敗ブランチでも `lifecycle.advanceTo(dataReady)` を呼ぶように修正、スプラッシュ永久停止を回避 |
| FIX-02 | `app/Core/Scanner/PhotoScanner.cs:475-482` | **DONE** — foreach 全体を try で囲み、当該ディレクトリのみスキップする防御を追加 |
| FIX-03 | `app/Features/Shell/ShellViewModel.cs:178-211` | **DONE** — シグネチャを `void startScan()` に変更、3 箇所 (initialize/applyFolderChange/executeResetFolder) の `await` を撤去、XMLDoc で fire-and-forget を明示 |
| FIX-04 | `app/Shared/Controls/PhotoGridItemsView.xaml.cs:135-136` | **DONE** — `if (internalScrollViewer is not null) return;` で再 Loaded 時の二重登録を防止 |
| FIX-05 | `ThumbnailSourceConverter.cs:22` + `GalleryMasonryView.xaml.cs:449` | **DONE** — 両方の `CreateOptions = IgnoreImageCache` を除去、WinUI URI キャッシュ有効化 |
| FIX-06 | `app/Core/AppLogger.cs:76-81` | **DONE** — `StreamWriter { AutoFlush = true }` でクラッシュ直前のログ欠落を抑制 |
| FIX-07 | `app/Features/Template/TemplatePageViewModel.cs:142-162` | **DONE** — `deleteTemplate` の状態退避＋失敗時ロールバック (元位置への Insert) で in-memory と DB の整合を維持 |
| FIX-08 | `app/Services/StellaRecordRegistration.cs:75` | **DONE** — `catch (Exception ex) { AppLogger.Warn(...) }` に置換、`using System;` `using Alpheratz.Core;` 追加 |
| FIX-09 | `app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs:103,108` | **DONE** — `OnViewSizeChanged` `OnViewUnloaded` 名前付きハンドラ化、Unloaded で 3 件すべて `-=` |
| FIX-10 | `BuildWorks/launcher/Program.cs:30` | **DONE** — `UseShellExecute = false` に変更、コメントで仕様根拠明示 |
| FIX-11 | `BuildWorks/nsis/Installer.nsi:167-170` | **対応不要 → FP-47** — ユーザ判断: アンインストール時の `$INSTDIR\Data` 削除は意図通り (お気に入り等の永続保持は不要、ゴミ残し回避が目的) |
| FIX-12 | `app/Features/Shell/ShellViewModel.cs:359-367` | **対応不要 → FP-48** — try/catch がラムダ内の両 await を正しく包んでおり、pass 3 verifier の懸念は架空 |

---

## 🟡 要観察 (1 件)

| ID | 場所 | 内容 | 観察ポイント |
|---|---|---|---|
| WATCH-01 | `app/Features/Gallery/PhotoThumbnailItem.cs:146-167` | `EffectiveSourcePath/EffectiveDisplayPath` が getter 毎に文字列計算 + 依存フィールド変更時 `OnPropertyChanged` | 現状の使用頻度では問題なし。1 万件超ライブラリのスクロールプロファイル時にホットスポットになるなら計算結果を backing field にキャッシュ |

---

## ❌ 対応不要 (以下は今後の監査で再指摘されても無視してよい)

### FP-01: `app/Themes/Typography.xaml:7,13` — `{ThemeResource AFontSans}` クラッシュ
- **誤認理由**: `ThemeResource` はマージ済み辞書全体から解決するため `ThemeDictionaries` 内になくても OK。`Tokens.xaml` (App.xaml:13 で先にマージ) の StaticResource を正しく拾える
- **再検証ポイント**: もし `AFontSans` が真に未定義ならまず IDE が警告を出す

### FP-02: `app/Shared/Controls/ScanningOverlay.xaml` / `ToastHost.xaml` DataContext 未設定
- **誤認理由**: WinUI/WPF の DataContext は親要素から自動継承される。`ShellPage.DataContext = viewModel` を起点に `ShellStage` 経由で全子コントロールへ伝播
- **再検証ポイント**: XAML 子コントロールの DataContext を疑う場合、明示的に `DataContext={x:Null}` が設定されていないか確認

### FP-03: `app/Shared/Controls/PhotoGridItemsView.xaml:55` — `AnimatedFavoriteStar.Liked` TwoWay バインド
- **誤認理由**: `{Binding ...}` の既定 Mode は OneWay。`AnimatedFavoriteStar.cs` の DP 登録も Mode 指定なし。`Photo.IsFavorite` も setter を持つ
- **再検証ポイント**: もし `Liked` を将来 TwoWay にするなら `IsFavorite` の双方向書込検証が必要

### FP-04: `app/Core/Imaging/Pdq/PdqImageReader.cs:70-79` — `bgra` 配列の境界チェック欠如
- **誤認理由**: WinRT `BitmapDecoder.GetPixelDataAsync` の契約で `width*height*4` バイト保証

### FP-05: `BitmapDecoder` / `PixelDataProvider` の Dispose 漏れ (各所)
- **誤認理由**: これらは WinRT RuntimeClass で IDisposable を実装していない。WinAppSDK 仕様。GC + COM ref count で解放される
- **対象ファイル**: `ThumbnailService.cs:141,158-161`, `PdqImageReader.cs:32`

### FP-06: `ThumbnailService.cs:163-168` — `fileStream` 例外時 orphan
- **誤認理由**: `finally` で `fileStream` を Dispose する経路あり

### FP-07: `app/Core/Imaging/Pdq/PdqHasher.cs:179-183` — decimation 範囲外
- **誤認理由**: 数式上 `(2*outRows-1)*inRows/(2*outRows) ≤ inRows-1` が常に成立。証明済み

### FP-08: `app/Core/AppConfig.cs:11` — `JsonOptions` null 処理
- **誤認理由**: `AlpheratzSetting` の各プロパティが初期値 (デフォルトリスト等) を持ち、null 化しない

### FP-09: `app/Features/WorldResolve/WorldResolveViewModel.cs:203-209` — トランザクション欠如
- **誤認理由**: 単一 UPDATE 文は SQLite 既定で原子的。ループでまとめる必要なし

### FP-10: `app/Features/Gallery/Controls/GalleryFilterPanel.xaml.cs:571,853` — 動的ボタンの Click ハンドラ leak
- **誤認理由**: `parent.Children.Clear()` でボタンが visual tree から外れる → ボタンへの唯一の root が切断 → ボタンと共にラムダも GC

### FP-11: `app/Features/Gallery/GalleryPage.xaml.cs:219-234` — `WireMasonryCallbacks` 重複登録
- **誤認理由**: `OnPhotoTapped` 等は **Action プロパティ** (`+=` ではなく `=` で代入)。再 wire しても上書きされ重複しない

### FP-12: `app/App.xaml.cs:209` — `PhaseAdvanced` ハンドラ未解除
- **誤認理由**: App はシングルトンで生涯一個。ハンドラの GC ルート切断が不要

### FP-13: Bootstrap → Shell 遷移レース / `SwapToShell` async void crash
- **誤認理由**: `TryEnqueue` + `Activate` 順序 + 内部 try/catch で多層防御済み

### FP-14: `app/Services/PhotoService.cs:182-193,211-223` — セマフォ over-release
- **誤認理由**: `WaitAsync` は try の **外**。canonical safe pattern。`OperationCanceledException` で finally 未到達

### FP-15: `app/Core/UiThreadSafeObservableObject.cs:15-23` — 再入で StackOverflow
- **誤認理由**: `UiThread.Run` が `HasThreadAccess` を見て同期インライン実行 → 再帰マーシャルではなく通常の関数呼出

### FP-16: `app/Core/UiObservableCollection.cs:32-41` — `ReplaceAll` のロック欠如
- **誤認理由**: 全操作が `UiThread.Run` ラムダ内で UI スレッド直列化済み

### FP-17: `app/Core/LocalEventBus.cs` snapshot 脆弱
- **誤認理由**: snapshot 中ロック保持、handler 実行はロック外。正しい設計

### FP-18: `app/Services/ThumbnailWorker.cs:70` セマフォ race
- **誤認理由**: `using` 内で `Task.WhenAll(tasks)` を await してから抜けるので安全

### FP-19: `app/Services/PhashService.cs:138` — Phash 進捗の順序破壊
- **誤認理由**: `LocalEventBus.PublishAsync` が `foreach + await` で逐次実行 → FIFO 保証

### FP-20: `app/Features/Gallery/GalleryPhotosState.cs:263,421` — CTS race
- **誤認理由**: `try { ct = cts.Token; } catch (ObjectDisposedException)` で明示処理

### FP-21: `app/Shared/Animations/AnimationHelper.cs` — `GetElementVisual` NRE
- **誤認理由**: WinUI 仕様で `ElementCompositionPreview.GetElementVisual` は常に Visual を返す。null 不可

### FP-22: `app/Shared/Services/ToastService.cs:26` — static 初期化クラッシュ
- **誤認理由**: `DateTimeOffset.UtcNow` は実質スローしない

### FP-23: `app/Features/Shell/ShellPage.xaml.cs:66-67` — `viewModel.PropertyChanged` 未解除
- **誤認理由**: `RuntimeState.cs:108` の Unloaded で実際に外している。pass 1 audit の誤読

### FP-24: `app/Features/Shell/ShellPage.xaml.cs:42-64, 79` — HeaderBar / drillDown / OnCancelScan コールバック未解除
- **誤認理由**: ShellPage はシングルトンで子コントロールも一体所有 → leak 不可能

### FP-25: `app/Features/Shell/ShellPage.xaml.cs:183-191, 294-337` — 子ページ(`galleryPage`/`settingsPage`) コールバック未解除
- **誤認理由**: 子ページもキャッシュされたシングルトン。ShellPage と同じ生涯なので未クリアでも leak ゼロ

### FP-26: `app/Features/Shell/ShellPage.xaml.cs:365-397` — Modal `OnAddTag/OnTweet` 等未解除
- **誤認理由**: `PhotoModalPage` は毎回 `new` され閉じれば GC される

### FP-27: `app/Features/Settings/SettingsPage.xaml.cs:153-395` — 11 個の async void
- **誤認理由**: 全 11 個が try/catch を持つ。pass 2 verifier が確認済み

### FP-28: `app/Features/Settings/SettingsPage.xaml.cs:83-84,106-108` — Loaded 多重購読
- **誤認理由**: Loaded/Unloaded が対称ペア、Reload 時は Unloaded → Loaded 順なので二重購読発生せず

### FP-29: `app/Features/Settings/SettingsPage.xaml.cs:438-447` — `ThemeHelper.Brush()` null + indexing
- **誤認理由**: `Brushes.xaml` の Light/Default ThemeDictionaries 両方で全 6 キー (`ABorderStrong`, `ABorder`, `AAccentSoft`, `ASurfaceSoft`, `APrimary`, `ATextDim`) 定義済み。`stack` は固定 XAML 構造で必ず TextBlock を 2 個持つ

### FP-30: `app/Features/Shell/ShellPage.RuntimeState.cs:69-70` — `App.MainWindowInstance` null 時の silent skip
- **誤認理由**: `mainWindow` は `OnLaunchedCore` で 1 度だけ代入され null 化されない。`ApplyTheme` は MainWindow 確立後にしか呼ばれない

### FP-31: `app/Features/PhotoModal/PhotoModalPage.xaml.cs:124-130` — `BitmapImage` リーク
- **誤認理由**: `Image.Source` への代入で旧 BitmapImage の参照は即解放。GC で回収

### FP-32: `app/Features/PhotoModal/PhotoModalPage.xaml.cs:291` — `DispatcherQueue.TryEnqueue` 戻り値無視
- **誤認理由**: シャットダウン時のみ false。クリップボードオーバーレイ表示の cosmetic 失敗

### FP-33: `app/Features/WorldResolve/WorldResolveViewModel.cs:140-148, 288` — Off-UI thread プロパティ書込
- **誤認理由**: 文字列参照代入は .NET で原子的、`UiThreadSafeObservableObject` が `PropertyChanged` を UI スレッドにマーシャル

### FP-34: `app/Features/WorldResolve/WorldResolvePage.xaml.cs:30` — PropertyChanged 未解除
- **誤認理由**: Page と VM が同時に作られ同時に GC される (両方とも modal 専用 transient)

### FP-35: `app/Features/WorldResolve/WorldResolvePage.xaml.cs:124` — CTS 未 Dispose
- **誤認理由**: セッション中 1〜2 個程度の allocation で実害なし

### FP-36: `app/Features/Gallery/GalleryViewModel.cs:111,115,132` — Fire-and-forget
- **誤認理由**: `onFiltersChanged/onFiltersCollectionChanged` の外側 try/catch 及び `debounceAndApplySearch` 内部 catch で全例外捕捉

### FP-37: `app/Features/Gallery/GalleryPhotosState.cs:137-138` — 二重 Subscribe
- **誤認理由**: `InitializeAsync` は app 起動時に 1 度だけ呼出 (`ShellViewModel.initialize()` → 1 箇所のみ)、singleton

### FP-38: `app/Features/TagMaster/TagMasterViewModel.cs:48-52` — `masterTags.Clear()` 連発でちらつき
- **誤認理由**: `masterTags` は `UiObservableCollection`、`UiThread.Run` で UI スレッド直列化。ユーザ操作 1 回につき 1 周のみで体感なし

### FP-39: `app/Services/OrientationService.cs:120` — `OrientationProgress` 購読者ゼロ
- **誤認理由**: ShellViewModel のコメント (line 45-50) に「orientation は post-scan のバックグラウンドエンリッチで UI 表示は不要」と明記。dead event ではなく **設計通りの silent background task**
- **再検証ポイント**: 将来 UI で進捗を出したくなったら subscribe を追加すればよい

### FP-40: `app/Features/Shell/ShellViewModel.cs:51` — `postScanGate` 未 Dispose
- **誤認理由**: ShellViewModel はシングルトン、SemaphoreSlim の OS handle はプロセス終了時に OS が自動回収

### FP-41: `app/Features/Shell/ShellViewModel.cs:42-43` — `unlistenFns` 再初期化で多重購読
- **誤認理由**: `initialize()` の呼出は App 起動時 1 回のみ (`App.xaml.cs:259` 単一箇所)、再呼出経路なし

### FP-42: `app/Features/Shell/ShellViewModel.cs:264-286, 299-309` — postScanGate / orphan scan task
- **誤認理由**: `runPostScanWorkflow` は三層 try/catch + finally で完全防御。`Task.Run` orphan でも例外は内部で log + 抑制

### FP-43: `app/Core/AppConfig.cs:56` — `File.WriteAllText` 非アトミック
- **誤認理由**: `LoadSetting` (lines 14-42) で破損時にデフォルトに fallback、ユーザ可視データ損失なし。BSOD 中の書込破損は理論上のみ

### FP-44: `app/App.xaml.cs:88-94` — `OnLaunched` の bare catch
- **誤認理由**: コメントで「toast 機構を生かすために app は continue させる」と明示。意図的設計
- **要注意**: ただし DB init 失敗時ユーザに何も表示されない可能性あり。将来 toast の確実な接続が必要なら別途検討

### FP-45: `BuildWorks/launcher/Alpheratz.Launcher.csproj` — UAC マニフェスト無し
- **誤認理由**: launcher はプロセス起動のみで保護パスへの書込なし。エラーログも LocalAppData (ユーザ書込可) に書く。.NET 8 既定で `asInvoker`

### FP-46: `app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs:450` — `DecodePixelWidth` NaN/Inf → OOM
- **誤認理由**: `entry.Container.Width` は `GalleryMasonryLayout.Build()` の `columnWidth ≥ 220` 保証経由で必ず有限正の値。NaN/Inf 経路は到達不能

### FP-47: `BuildWorks/nsis/Installer.nsi:167-170` — アンインストール時の `$INSTDIR\Data` 削除
- **誤認理由**: ユーザ判断で「アンインストール = 完全クリーンアップ」が意図された設計。お気に入り・スキャン結果等は再インストール時に再構築可能なため永続保持は不要。むしろ `$INSTDIR` 内のゴミ残し回避を優先する UX 判断
- **再検証ポイント**: 将来「アンインストール時にユーザデータ保持オプション」要望が来た場合に再評価

### FP-48: `app/Features/Shell/ShellViewModel.cs:359-367` — `phash:complete` 内 orphan `Task.Run`
- **誤認理由**: pass 3 verifier の指摘を実コードで再確認したところ、try/catch がラムダ本体の全 await を完全に囲んでおり「surrounding context」で例外が漏れる経路は実在しない
- **再検証ポイント**: pass 2/pass 3 verdict が割れた場合は必ず実コードのインデント構造を直接確認する

---

## 🔍 新規調査 (2026-05-20、修正実施後) — triage

修正完了後に 5 エージェントで未検出問題を再探索。結果は下記の通り、REAL 4 件は別途 FIX として追跡、その他は本メモに FP として恒久的に登録する。

### REAL (要対応 = 別途 FIX-NEW として追跡)

| 新規 ID | 場所 | 概要 |
|---|---|---|
| FIX-NEW-01 | `app/Core/Imaging/Pdq/PdqImageReader.cs:68` | `ExifOrientationMode.IgnoreExifOrientation` で PDQ 計算 → EXIF 回転表示と物理回転済み画像が同一視されず重複検出されない。サムネルパイプラインと整合させ `RespectExifOrientation` へ |
| FIX-NEW-02 | `app/Core/Imaging/ThumbnailService.cs:80-83,109-110` | `_pathLocks` のキーが filename 由来 `thumbPath` のため別ディレクトリ同名で衝突。さらに `CurrentCount == 1` での `TryRemove` に再取得 race window あり。キーは正規化フルパス + `imgCacheDir` で安定化 |
| FIX-NEW-03 | `app/Services/PhashService.cs:95-98` | `OperationCanceledException` を一般例外として握り潰し進捗カウンタを進めてしまう。`when (ex is not OperationCanceledException)` を付与しキャンセルは素通しに |
| FIX-NEW-04 | `app/Core/Scanner/PhotoScanner.cs:345-350` | `File.GetLastWriteTime(path)` がローカル時刻を返す → DST 跨ぎや TZ 変更で同一ファイルでも timestamp が変化。`GetLastWriteTimeUtc` へ移行 (既存 DB 値とのマイグレーション要設計) |

### FALSE / 重複 / 低優先 — 終了済みとして記録

- **FP-49**: `PdqHasher.cs:295` bit ordering "double reversal" → PDQ spec 準拠で正常 (pass 2 検証済)
- **FP-50**: `PdqHasher.cs:179,182` decimation drift → 数学的に範囲内であることを証明済み (FP-07 と重複)
- **FP-51**: `ThumbnailService` `DetachPixelData` の Dispose 漏れ → `DetachPixelData()` は plain `byte[]` を返すのみで IDisposable ではない
- **FP-52**: `AppIcons.GetGeometry` 並行 parse → Compositor 側 cache で十分、追加最適化不要
- **FP-53**: `ThemeHelper.SelectedTheme` enum race → enum 代入は CLR で原子的
- **FP-54**: `AppLifecycleService:60` `PhaseAdvanced` snapshot 化 → `lock(_gate)` 内発火のため snapshot 不要
- **FP-55**: `ToastService._idCounter` 初期化 race → 過去 pass で FALSE 判定済み
- **FP-56**: `DispatcherService` null/disposed パス → DI 解決順序で保証
- **FP-57**: `GalleryMasonryView` `DispatcherQueue` field-init 順 → ctor は UI スレッド上で実行が保証されており `GetForCurrentThread` が確実に返す
- **FP-58**: `AnimatedFavoriteStar` `Loaded`/`ActualThemeChanged` 未解除 → コントロール自体が parent と同一寿命のため leak 不可
- **FP-59**: `CompositionBatch.Completed` 後処理 → `batch.End()` 後 GC 対象となるため追加 unsubscribe 不要
- **FP-60**: `WorldService.cs:94` `Process.Start` タイムアウト → explorer.exe は Windows シェル、fire-and-forget 設計で OK
- **FP-61**: `AppLogger.cs:47` `Directory.CreateDirectory` 沈黙 → fallback パス計算用、失敗時は別 path で再試行
- **FP-62**: `AlpheratzDb.cs:211` `StringComparer.Ordinal` → scanner が forward-slash 正規化済みパスのみ書込、上流契約で吸収
- **FP-63**: `AlpheratzDb.cs:1277-1281` NULL vs empty `world_name` → 実運用で NULL のみ書込、空文字経路存在せず
- **FP-64**: `AlpheratzDb.cs:1329` vs `1404` validation 非対称 → write 側は `WorldService` 内で trim/validate 済み
- **FP-65**: `AlpheratzDb.cs:921` `DeleteMissingPhotos` casing → パスは scanner で正規化済み、casing 不一致は発生し得ない
- **FP-66**: `GalleryPhotosState.cs:317-333` fire-forget → pass 2/3 で try/catch 完備を確認済み

### 要観察 (新規)

| ID | 場所 | 内容 | 観察ポイント |
|---|---|---|---|
| WATCH-02 | `app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs` shimmer animations | shimmer 停止漏れの可能性 | Compositor が detached 要素の animation を自動破棄するため現状実害なし。Profiler で leak が観測されたら明示停止を追加 |

---

## 🛠️ 2026-05-20 修正実施 (FIX-NEW-01〜04 + 動線改善 1 件)

| ID | 場所 | 状態 | メモ |
|---|---|---|---|
| FIX-NEW-01 | `app/Core/Imaging/Pdq/PdqImageReader.cs:30-95` + `PdqHasher.cs:33-40` + `AlpheratzDb.cs:1428-1513` + `PhashService.cs:55,67,93` | **DONE** | `RespectExifOrientation` 化 (+ axis-swap 対応で 90°/270° 寸法も正しく) / `PdqHasher.CurrentVersion = 1` 導入 / `phash_version` カラムを実活用し旧 version (0) 行を再計算対象に / `UpdatePhotoPhashAsync` に version を書く |
| FIX-NEW-02 | `app/Core/Imaging/ThumbnailService.cs:32-44,102-110,133` | **DONE** | thumb filename に `ComputePathHash(photoPath)` (SHA1 先頭 64bit hex) を混ぜて別ディレクトリ同名衝突を回避。`_pathLocks.TryRemove` クリーンアップを撤去 (Release→GetOrAdd→TryRemove→GetOrAdd の race で同一ファイル並行書込が成立していた) |
| FIX-NEW-03 | `app/Services/PhashService.cs:95` | **DONE** | `catch (Exception ex) when (ex is not OperationCanceledException)` で cancellation を素通しに |
| FIX-NEW-04 | `app/Core/Scanner/PhotoScanner.cs:425-444` | **DONE** | `IsFileModifiedSinceTimestamp` を local 時刻に揃え `ToUniversalTime()` 経由の DST/TZ skew を排除。DST 跨ぎ + clock skew 吸収用に fudge を 1 分 → 90 分に拡張 |
| FIX-NEW-05 | `app/Features/PhotoModal/PhotoModalPage.xaml.cs:124-130` | **DONE** | (動線監査由来) FIX-05 で gallery 側は外したが PhotoModal の `BitmapCreateOptions.IgnoreImageCache` が残存。modal 連続表示でフルデコード強制 → URI cache 有効化で解消 |

---

## 📋 動線・保守性レビュー結果 (2026-05-20)

### 着手済 (1 件)

| ID | 場所 | 状態 | メモ |
|---|---|---|---|
| MAINT-01 | `app/Features/PhotoModal/PhotoModalPage.xaml.cs:95-104, 215-226` | **DONE** | `OnStatePropertyChanged` と `Page_Loaded` が 4 つの sync を別々に列挙していた。`RefreshFromState()` private method に統合し、sync 追加時の片方忘れバグを構造的に防ぐ |

### 動線で気になる点 (低〜中、未対応 / 観察継続)

- **ShellPage:552-568** modal dismiss の 400ms dead-zone — 経過時間判定でなく fade-in 完了フラグ参照に置換できるが現状実害なし
- **ShellViewModel cancelScan fire-and-forget** — 「キャンセル中…」の visible feedback が欠ける (UX 改善案、致命的でない)
- **GalleryFilterPanel dropdowns** — 開閉で scroll offset が保持されない
- **空ギャラリー時のメッセージ不在** — フィルタ 0 件と loading が区別できない
- **GalleryMasonryView shimmer** — 高速ロード時のちらつき (軽微)

---

## 🚫 「分割しない」判断の恒久メモ (FP-MAINT-01〜05)

2026-05-20 の動線・保守性監査で「god class」「責務分散」と指摘された 5 件について、実コードを精査した結果 **分割は保守性をむしろ下げる** と判断。再監査エージェントが同じ提案をしてきても以下を参照。

### FP-MAINT-01: `GalleryFilterPanel.xaml.cs` (924 行) を子コントロールに分割
- **却下理由**: `OnActualThemeChanged` と `boundFiltersState.PropertyChanged` が calendar / tag list / world list / orientation / active states を**一括で再描画する共通アンカー**になっている。分割すると 3 子コントロールが各々 theme と filter state を購読し、親が子を listen して整合させる必要が生じる。924 行の内訳は ~10 のごく短い handler (例: `Orientation*_Click` が 3 行ずつ) で密結合 spaghetti ではなく、**整列した平坦リスト**
- **再検証ポイント**: もし calendar セクションだけが将来 200 行以上に膨らんだら `CalendarPicker` 単独抽出は再検討余地あり

### FP-MAINT-02: `GalleryMasonryView.xaml.cs` (743 行) から `MasonryCard` UserControl を抽出
- **却下理由**: クラス冒頭 doc に明記の通り「WinUI 標準 ItemsRepeater が 27,000 枚で OOM」を回避するための hand-rolled 仮想化。UserControl 化はテンプレート実体化コスト + DependencyProperty 経由 binding + 仮想化と card lifecycle の物理分離で **性能 regression**。本質的にこれは「単一責任を破ってでも性能を取った」設計
- **再検証ポイント**: 仮想化を捨てて良い小規模ユースケースが現れた場合のみ別途検討

### FP-MAINT-03: ShellPage の 3 つの bool (`isModalOpen` / `isMiddleModalOpen` / `isFilterOpen`) を `ModalStack` enum に
- **却下理由**: 3 次元は **独立した直交フラグ**。実コードで以下のすべての組合せが正当に処理されている:
  - filter 開きながら写真モーダル開く → `isFilterOpen && isModalOpen`
  - 設定モーダル → そこから写真モーダル → `isMiddleModalOpen && isModalOpen` (2 段スタック、`CloseMiddleModal` で `isModalOpen` を見て HeaderBar dim 維持)
  - キーボード判定 `!isModalOpen && !isMiddleModalOpen` でも `isFilterOpen` だけ独立
  - 2^3 = 8 通りすべて valid → enum で表現すると flags enum になり結局 bool と等価
- **再検証ポイント**: もし modal layer 種別 (middle) が 4 種以上に増えたら別途 enum 化

### FP-MAINT-04: `GalleryFiltersState` ⇄ `GalleryDisplayState` を merge
- **却下理由**: 責務は**意味的に分離**されている:
  - FiltersState = 「何を見せたいか」(search/date/world/tag/sort/grouping)
  - DisplayState = 「どう描画するか」(ViewMode/PanelWidth/GridWrapperHeight/ViewPreparationLabel)
  - 「GroupingMode は FiltersState なのに `prepareGroupingModeChange` が DisplayState」という指摘は誤読。実装は **値の所有 (FiltersState) と視覚遷移の調整役 (DisplayState)** の綺麗な分担で、`prepareGroupingModeChange(current, next, setter)` が setter を callback として受け取り、`clearPendingViewPreparations` → `setGroupingMode` → label クリアの遷移責務だけを引き受けている
  - merge すると filter 変更が直接 view preparation 副作用を持つ god-state になる

### FP-MAINT-05: `SettingsPage` の Loaded/Unloaded subscription を `CompositeDisposable` で束ねる
- **却下理由**: 現状 3 subs の対称ペア (3 行 + 3 行)、視認できる距離。`CompositeDisposable` は Rx 系の依存追加 + indirection コストで「3 ペアの並びを見落とすミス」を防ぐが、現状サイズなら肉眼で十分検査可能
- **再検証ポイント**: subs が 10+ に膨らんだ時に再評価

---

## 監査メソッド統計

| Pass | 件数 | 結論 |
|---|---|---|
| Pass 1 (30 agent 検出) | 85 件指摘 | 過剰検出多数 |
| Pass 2 (30 agent 検証) | TRUE 32 / PARTIAL 12 / FALSE 41 | WinRT 型・XAML 継承の理解不足が多発 |
| Pass 3 (29 agent 三段精査) | **対応必要 12 / 要観察 1 / 対応不要 16+46** | 最終結論 |

---

## 再監査時のチェックリスト

新しいエージェントが下記を「致命的」と指摘してきても、**まずこのメモを参照**:

- WinRT 型 (`BitmapDecoder`, `PixelDataProvider`) の Dispose 漏れ → FP-05
- `UiObservableCollection` / `UiThreadSafeObservableObject` の並行性 → FP-15, FP-16
- ShellPage / 子ページ系のコールバック未クリア → FP-23〜FP-26
- WorldResolveVM の off-UI 書込 → FP-33
- `AppConfig.WriteAllText` 非アトミック → FP-43
- `OrientationProgress` 購読者なし → FP-39
- XAML DataContext 未設定 / ThemeResource 未定義 → FP-01〜FP-03
- DI シングルトンの「破棄漏れ」「再 init 多重購読」 → FP-37, FP-40, FP-41
- アンインストール時の `$INSTDIR\Data` 削除 → FP-47 (意図通り)
- `phash:complete` ラムダの try/catch 外漏れ → FP-48
- PDQ bit ordering / decimation 算式 → FP-49, FP-50 (PDQ spec 準拠)
- `DetachPixelData` の Dispose → FP-51 (`byte[]` 返却で非 IDisposable)
- `ThemeHelper.SelectedTheme` enum race → FP-53 (CLR 原子代入)
- `Compositor.Completed` 後処理・shimmer animation 停止 → FP-59, WATCH-02
- `Process.Start` (explorer.exe) タイムアウト → FP-60
- `AlpheratzDb` パス正規化 / NULL vs empty 議論 → FP-62〜FP-65 (上流契約で吸収)

これらは設計上 OK と判断済み。なお 2026-05-20 の新規調査で実コードに影響する 4 件 (FIX-NEW-01〜04) が見つかっており、別タスクで追跡中。
