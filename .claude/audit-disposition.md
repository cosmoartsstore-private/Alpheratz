# Audit Disposition Memo (作成: 2026-05-20)

3 段階監査 (検出 30 → 検証 30 → 三段精査 30) の結論を記録。
**このファイルは Claude の自己参照用メモ。再監査時に同じ指摘で時間を浪費しないために残す。**

---

## 凡例

- **対応必要**: 実コードに影響あり、修正推奨
- **対応不要**: false positive、または影響が運用上ゼロ
- **要観察**: 現状問題なし、規模拡大時に再評価

---

## ✅ 対応必要 (12 件)

| ID | 場所 | 内容 | 推奨修正 |
|---|---|---|---|
| FIX-01 | `app/App.xaml.cs:259-273` | `shellViewModel.initialize()` 失敗時 `dataReady` 不到達 → スプラッシュ永久停止 | `ContinueWith` の失敗ブランチで error 用 phase 追加 or 強制 `advanceTo(dataReady)` + 失敗状態表示 |
| FIX-02 | `app/Core/Scanner/PhotoScanner.cs:475-482` | `Directory.EnumerateFileSystemEntries` の遅延列挙 `foreach` を try で囲っておらず権限例外でスキャン全停止 | `foreach` を `try { ... } catch { log; return; }` で囲う |
| FIX-03 | `app/Features/Shell/ShellViewModel.cs:178-211` | `startScan()` が `Task.CompletedTask` を即返却するが内部で `Task.Run` 実行。呼出側 3 箇所 (172, 406, 433) が `await` しており API 偽装 | `async Task` に変更して内部の処理を `await` する、または明示的に `void` を返す |
| FIX-04 | `app/Shared/Controls/PhotoGridItemsView.xaml.cs:135-136` | `Loaded` が複数回発火で `ViewChanged/PointerWheelChanged` が二重登録 → スクロール毎にハンドラ多重実行 | `if (internalScrollViewer is not null) return;` を `PhotoItems_Loaded` 先頭に追加 |
| FIX-05 | `app/Shared/Converters/ThumbnailSourceConverter.cs:22` + `app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs:449` | `BitmapCreateOptions.IgnoreImageCache` 設定で WinUI 内蔵 URI キャッシュ無効化。スクロールごとに同一画像を再デコード | `IgnoreImageCache` を外す、または独自 `BitmapImage` キャッシュを `URI+DecodePixelWidth` で実装 |
| FIX-06 | `app/Core/AppLogger.cs:76-81` | `StreamWriter` の `AutoFlush=false` かつ明示 `Flush` なし。クラッシュ直前のログ消失 | `new StreamWriter(fs, ...) { AutoFlush = true }` のワンライナー |
| FIX-07 | `app/Features/Template/TemplatePageViewModel.cs:142-162` | `deleteTemplate` がコレクション削除後に save 失敗してもメモリ状態を巻き戻さない → 再起動でテンプレ復活 | save 失敗時 catch で `tweetTemplates.Add(template)` ロールバック |
| FIX-08 | `app/Services/StellaRecordRegistration.cs:75` | 空 catch、コメントなし、ログなし | `catch (Exception ex) { AppLogger.Warn($"... {ex.Message}"); }` |
| FIX-09 | `app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs:103,108` | `SizeChanged += (_,_)=>Rebuild()` 匿名ラムダで購読、Unloaded で `ActualThemeChanged` のみ外す非対称 | 名前付きハンドラ `OnSizeChanged` に変更し Unloaded で `SizeChanged -= OnSizeChanged` |
| FIX-10 | `BuildWorks/launcher/Program.cs:30` | `UseShellExecute=true` 下で `ArgumentList.Add` は無音破棄される。launcher への argv がフロントエンドに届かない | `UseShellExecute = false` に変更 |
| FIX-11 | `BuildWorks/nsis/Installer.nsi:167-170` | アプリは `$INSTDIR\Data` をユーザデータ保管場所として使う (`AppPaths.cs:33`)。アンインストール時 `RMDir /r` で DB / ログ / キャッシュ完全消去 | uninstall section から `Data` フォルダ削除を撤去、または「ユーザデータも削除？」プロンプト追加 |
| FIX-12 | `app/Features/Shell/ShellViewModel.cs:359-367` (要再判断) | `phash:complete` 中の `_ = Task.Run(async () => { ... })` の try/catch は内部にあるが orphan task 自体への保険なし。pass 3 verifier は対応必要としたが try は両ステートメントを包んでいる | 念のため `Task.Run(async () => { try {...} catch (Exception ex) {...} })` を確認、もし既に try が両命令を包むならスキップ可 |

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

これらは設計上 OK と判断済み。
