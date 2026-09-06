# XAML Packaging Contract

この文書は、self-contained unpackaged WinUI 3 frontendをNSISで配布するための現行XBF/PRI契約を保持する。障害対応の日誌ではない。

## Confirmed Runtime Model

- `app/Alpheratz.Frontend.csproj`は`WindowsPackageType=None`、x64、Windows App SDK `1.6.250205002`。
- Debugは`Program.Main`からsystem-installed Windows App Runtime 1.6を明示bootstrapする。
- Releaseは`WindowsAppSDKSelfContained=true`と`WindowsAppSdkUndockedRegFreeWinRTInitialize=true`を使い、system MSIXをbootstrapしない。
- Release installerはMicrosoft Visual C++ Redistributable（x64）を内包し、必要versionをmachine-wide registryで確認してからappを置き換える。
- custom entry pointは`Application.Start`より前に`WinRT.ComWrappersSupport.InitializeComWrappers()`を呼ぶ。
- `XamlControlsResources`は`App.xaml`の初期load時ではなく、`App.OnLaunched`で最初のWindow/Page XAMLより前にresource dictionaryへ挿入する。

Debug bootstrapとRelease reg-free initializationを同じprocessで同時に使わない。system MSIXとAppLocal DLLの二重loadは`Microsoft.UI.Xaml.dll`のfail-fastにつながる。

## Required Publish Layout

generated `LoadComponent` URIはroot-relative `ms-appx:///...` を使う。通常の`dotnet publish`だけでは必要なXBFがpublish rootへ揃わないため、`BuildWorks/scripts/publish-app-release.ps1`が次を行う。

1. `app/artifacts/obj/x64/Release/net8.0-windows10.0.19041.0/win-x64`から全`*.xbf`を相対pathを保ってpublish rootへcopyする。
2. older assembly-name URIとの互換用に、同じXBFを`Alpheratz.Frontend/`以下へmirrorする。
3. `Microsoft.UI.Xaml.Controls.pri`をpublish rootの`resources.pri`へcopyする。
4. rootの`App.xbf`、`MainWindow.xbf`、`resources.pri`がなければpublishを失敗させる。

`Microsoft.UI.Xaml.Controls.pri`にはstock WinUI theme XBFのresource mapがあり、このunpackaged layoutではapp-root `resources.pri`という名前で解決される。

## Installer Runtime Contract

- `BuildWorks/scripts/prepare-vc-redist.ps1`はMicrosoft公式URLからx64 Redistributableを取得し、有効なAuthenticode署名とMicrosoft Corporationのsignerを確認する。
- 検証済みpackageはignoredの`BuildWorks/runtime/vc_redist.x64.exe`へ置く。通常buildは最新版を取得し直し、`-UseCachedVcRedist`指定時だけ有効なcacheを優先する。
- `build-release.ps1`はfile versionをNSISへ渡す。Installerは導入済みversionが同一以上なら実行せず、不足時だけ`/install /passive /norestart`で導入する。
- Runtime導入は既存appの削除より前に行う。失敗時はinstallを中止して現在のappを保持し、終了code `3010`ではreboot flagを設定する。
- Visual C++ Runtimeはmachine-wideの共有componentであり、Alpheratzのuninstallでは削除しない。導入または更新にはUAC承認が必要になる。

## Files That Form One Contract

- `app/App.xaml`
- `app/App.xaml.cs`
- `app/Program.cs`
- `app/Alpheratz.Frontend.csproj`
- `app/Directory.Build.props`
- `BuildWorks/scripts/publish-app-release.ps1`
- `BuildWorks/scripts/prepare-vc-redist.ps1`
- `BuildWorks/scripts/build-release.ps1`
- `BuildWorks/nsis/Installer.nsi`
- `BuildWorks/launcher/Program.cs`
- `.github/workflows/ci.yml`

このうち1ファイルだけのoutput path、resource initialization、bootstrap policyを変更しない。

## Do Not Regress

- `XamlControlsResources`を検証なしに`App.xaml`へ戻さない。
- publish rootのXBF copyを削除しない。
- 代替resource resolutionをinstalled appで確認せず`resources.pri`を削除しない。
- app PRI generationを安易に再有効化しない。resource map名が異なるPRIはtheme resourceを解決しない。
- direct frontend exeの起動だけで配布成功と判断しない。launcherのregistry pathとinstalled directory構成も検証する。
- `CopyXbfToSubfolder`のexcludeと事前`RemoveDir`を外さない。assembly-name directoryを入力へ再帰的に含めるとnested copyが増殖する。
- Visual C++ Redistributableを未検証のbinaryへ置き換えない。署名検証、version比較、app置換前の実行順を一体で維持する。

## Verification Checklist

1. 実行中の`Alpheratz`／`Alpheratz.Frontend` processを終了する。
2. clean Release buildが必要な場合は、対象がrepository内のgenerated outputであることを確認してからcleanする。
3. `BuildWorks\scripts\build-release.ps1`を実行する。
4. `BuildWorks\runtime\vc_redist.x64.exe`のAuthenticode署名とsignerを確認する。
5. `BuildWorks\artifacts\publish\Alpheratz`のrootに`App.xbf`、`MainWindow.xbf`、`resources.pri`があることを確認する。
6. `BuildWorks\Alpheratz-Installer.exe`からcurrent-user installする。
7. `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz\Alpheratz.exe`を起動する。
8. logo splashからShellへ遷移し、frontend processの`MainWindowHandle`が0でないことを確認する。
9. 起動時刻以後のWindows Application event logに新しいAlpheratz crashがないことを確認する。
10. `dotnet test tests\Alpheratz.Tests\Alpheratz.Tests.csproj`を実行する。

Release artifactを変更していない通常のdomain/test/document変更では、installed app検証までを毎回要求しない。
