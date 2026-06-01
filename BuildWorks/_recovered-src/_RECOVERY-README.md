# Alpheratz WIP 復元データ (recovery)

このフォルダは、セッション中の誤った `git checkout` で巻き戻された未コミット作業を
**インストール済みアプリの DLL（巻き戻し前のあなたの WIP からコンパイル）を逆コンパイル**して
復元したものです。逆コンパイル元: `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz\app\Alpheratz.Frontend.dll`
(1.14MB, 5/31 23:56, self-contained 版)。

## 復元の3層

1. **作業ツリーに直接戻した 13ファイル（最良・コメント付き）**
   セッションログの完全読み取り＋あなたの git stash から復元。`git diff` で確認可能。
   - csproj / build-release.ps1 / publish-app-release.ps1 / Program.cs / MainWindow.xaml.cs /
     ShellViewModel.cs / ShellPage.xaml.cs / GalleryMasonryLayout.cs /
     PhotoGridItemsView.xaml(.cs) / Themes/Brushes.xaml / Themes/Buttons.xaml /
     Features/Gallery/Controls/GalleryMasonryView.xaml.cs

2. **このフォルダ = 逆コンパイルした全 C#（約230ファイル、ロジック完全）**
   ↑1で戻せなかった約36ファイルの **C# ロジックはここに全部あります**:
   GalleryViewModel / GalleryPhotosState / GalleryDisplayState / GallerySelectionState /
   GalleryFiltersState / GalleryScrollState / AlpheratzDb / PhotoService / WorldService /
   OrientationService / PhotoScanner / SettingsViewModel / TagMasterViewModel /
   TemplatePageViewModel / PhotoModalState / PhotoModalViewModel / WorldResolveViewModel /
   StellaRecordRegistration ほか。各 .xaml.cs コードビハインドも含む。
   名前空間ごとのフォルダ構成（例: `Alpheratz.Features.Gallery/GalleryViewModel.cs`）。

   ⚠ 逆コンパイル出力の注意（そのままはビルド不可。「参考」として手で戻す）:
   - `[WinRTRuntimeClassName]` `[WinRTExposedType]` `using WinRT.*` 等は WinUI 生成物。削除する。
   - `[ObservableProperty]` 等のソースジェネレータは **展開済みプロパティ** になっている。
     元の `[ObservableProperty] private T field;` パターンに戻すと綺麗。
   - コメント（日本語コメント）は失われている。ロジックと変数名・メソッド構造は正確。

3. **残る隙間 = 純粋な .xaml マークアップ 約10個**（DLL には XBF として焼かれ C# 化されない）:
   ShellPage.xaml / GalleryPage.xaml / SettingsPage.xaml / PhotoModalPage.xaml /
   ShellHeaderBar.xaml / GalleryFilterPanel.xaml / GalleryGridStage.xaml /
   WorldResolvePage.xaml / EmptyState.xaml ほか。
   → **OS/クラウド/ファイル履歴が最善**。無ければ:
     - `*.recovered-0516`（古い stash、一部のみ） を参考に、
     - 逆コンパイルした各コードビハインドの `x:Name` フィールド・イベントハンドラ名から
       必要な要素を割り出して再構築。

## 推奨手順
1. まず OS/クラウド/ファイル履歴を確認（あれば全件正確に戻せる。これが最優先）。
2. 無ければ: 作業ツリーの13ファイル＋このフォルダの逆コンパイル C# を土台に各ファイルを復元。
3. 復元したら **すぐ commit**。`rescue-stash-today` / `rescue-stash-0516` タグも保持。

(ilspycmd をグローバルツールとして導入しました。不要なら `dotnet tool uninstall -g ilspycmd`)
