# Tech Stack and Architecture Decisions

この文書は、現行 source と build scripts が採用している技術、version、配布契約、設計判断を記録する。過去実装の作業手順や未採用の将来案は含めない。

## Stack Reference

### Application

| Area | Technology | Current version / setting |
| --- | --- | --- |
| Language | C# | `LangVersion=latest`, nullable enabled |
| Runtime | .NET | `net8.0-windows10.0.19041.0` |
| UI | WinUI 3 | Windows App SDK `1.6.250205002` |
| Architecture | MVVM | CommunityToolkit.Mvvm `8.4.0` |
| Dependency injection | Microsoft.Extensions.DependencyInjection | `8.0.1` |
| Database | SQLite | Microsoft.Data.Sqlite `8.0.11` |
| Image metadata | Windows / local binary readers | PNG XMP/iTXt、画像 header、PDQ luma decode |
| Perceptual hash | In-repository PDQ implementation | 256 bit、4 rotations |
| Messages | Embedded UTF-8 properties | `MessageCatalog.getMsg` |
| Platform | Windows | Build 19041以降、x64のみ |

`WindowsPackageType=None` の unpackaged app である。MSIX tooling と PRI generation は project設定で無効にする。

### Tests

| Area | Technology | Version / role |
| --- | --- | --- |
| Test framework | xUnit | `2.5.3` |
| Test host | Microsoft.NET.Test.Sdk | `17.8.0` |
| Coverage | coverlet collector / MSBuild | `6.0.0` |
| UI boundary policy | `ExcludeFromCodeCoverage` allowlist | WinUI / OS glueだけを除外 |

テストは `tests/Alpheratz.Tests` から application project を直接参照する。ViewModel、state、database、scanner、PDQ、service、XAML/package契約を UI processなしで検証する。WinUI Page、UserControl、Composition、OS dialog、process entry pointは framework boundaryとして限定的に除外し、判断ロジックは通常の coverage対象に残す。

`.github/workflows/ci.yml`はmainへのpushとmain宛PRをWindows runnerで検証する。全testの後にXBF/PRI、launcher、Microsoft Visual C++ Redistributable、NSISを含む最終installerまで生成し、途中のsoft-failは設けない。

### Distribution

| Component | Technology | Output |
| --- | --- | --- |
| Frontend publish | `dotnet publish`, self-contained | `BuildWorks/artifacts/publish/Alpheratz` |
| Launcher | .NET 8 Windows Forms, single-file | `artifacts/publish/Launcher/Alpheratz.exe` |
| Installer | NSIS, Unicode, per-monitor V2 | `BuildWorks/Alpheratz-Installer.exe` |
| Native runtime | Microsoft Visual C++ Redistributable x64 | Microsoft署名を検証してinstallerへ内包 |
| Install scope | current user | `%LOCALAPPDATA%\CosmoArtsStore\Alpheratz` by default |

launcher は `HKCU\Software\CosmoArtsStore\Alpheratz\RuntimeLocation` を読み、`app\Alpheratz.Frontend.exe` を起動する。値がない場合は既定 LocalAppData pathへフォールバックする。起動失敗時は `Data\logs\launcher_error.log` への記録を試みる。

## Source Layout

```text
app/
├─ App.xaml(.cs), Program.cs        startup and DI composition
├─ Features/
│  └─ <Feature>/                   Page, ViewModel, state, local logic
├─ Services/                       use-case and OS integration
├─ Core/
│  ├─ Database/                    SQLite gateway and query models
│  ├─ Imaging/                     thumbnail and PDQ
│  └─ Scanner/                     file and Polaris log scan
├─ Models/                         cross-feature DTOs and events
├─ Messages/                       Japanese message catalog
├─ Shared/                         reusable controls, services, models
└─ Themes/                         tokens, brushes, typography, controls
```

feature固有の状態と操作は同じ `Features/<Feature>` に置く。複数featureから使うI/Oやuse caseだけを `Services`、DB・filesystem・image algorithm等の基盤を `Core`、実際に再利用するUI部品を `Shared` に置く。

## Runtime Decisions

### ADR-001: WinUI 3 Single-process Desktop App

**Decision**

.NET 8 + WinUI 3 の単一 frontend processを使う。

**Current reasons**

- Windows native window、Dispatcher、Composition、file picker、clipboard、Launcherを直接利用できる。
- UI state、scanner、image analysis、SQLiteをC#内で接続でき、IPC schemaやsidecar processを必要としない。
- standard gridとvirtualized masonryをWinUI controlsで実装できる。

**Constraints**

- UI objectの変更はDispatcher threadへ戻す。
- WinUI XAML compilerとunpackaged resource layoutがbuild契約になる。
- platformはWindows x64に限定される。

### ADR-002: Feature-local MVVM

**Decision**

Pageはevent wiringとframework interaction、ViewModel/stateは観察可能な状態とuse case呼出し、service/coreはI/Oとdomain logicを担当する。

**Rules embodied in current code**

- `UiThreadSafeObservableObject` と `UiObservableCollection` は notification をmarshalするが、mutation自体のthread ownershipは保証しない。
- background処理からUI-bound stateを変える場合は `DispatcherService.RunOnUiThread` を使う。
- reusableな純粋判断がある場合だけ `*Logic.cs` へ分ける。表示から消えた機能のhelperは残さない。
- shellはoverlay/modalの所有者で、feature pageを実行時に複数parent間で移動しない。

CommunityToolkit の field-based `[ObservableProperty]` を使う箇所があり、`MVVMTK0045` は project全体で抑止している。現行 ReleaseはNativeAOTではなく、この設定はruntime要件ではない。

### ADR-003: SQLite as Local Metadata Index

**Decision**

写真原本とは別に、検索用metadata、tag、favorite、PDQ、Polaris visitをSQLiteへ保存する。

**Current reasons**

- 全データが単一Windows userのローカル状態であり、server DBを必要としない。
- WALによりbackground scan/writeとgallery readを共存させられる。
- transactionでmulti-table tag操作、batch upsert、folder resetの整合性を守れる。

DBは写真原本のbackupではない。原本を削除せず、scanで再構築可能なindexとして扱う。詳細は [Database Schema](database.md) を参照する。

### ADR-004: Custom PDQ Pipeline

**Decision**

repository内のPDQ実装で、各写真の0°、90°、180°、270° hashを計算する。保存値と比較値は32 byte / 256 bitを4つまで持つ。

**Current implementation choices**

- DCT matrixをcacheする。
- 一時bufferはpoolして大量解析時のallocationを抑える。
- comparisonでは64 bit整数4個のpacked hashとpopcountを使う。
- 保存経路では品質値を必要としないため、4方向hashだけを計算する。
- PDQ workerは30写真を1 chunk、CPUの半分を基準に最大8並列、2 waveを1 batchとして処理する。
- World Resolveのcandidate rankingは候補を先にparseし、大規模集合では最大12並列を使う。

PDQの現行用途はワールド名補完候補である。一般的なduplicate cleanup UIは提供しない。

### ADR-005: XAML Resource Dictionaries

**Decision**

視覚tokenとstyleは次の順でapplication resourcesへmergeする。

1. `Themes/Tokens.xaml`
2. `Themes/Brushes.xaml`
3. `Themes/Typography.xaml`
4. `Themes/Buttons.xaml`
5. `Themes/Containers.xaml`

feature固有のlayoutとvisual stateはfeature XAML/code-behindに置き、複数画面で本当に共通のcontrolだけを `Shared/Controls` に置く。Light/Darkはtheme resourceで切り替え、利用者の選択を `setting.json` に保存する。

`XamlControlsResources` は `App.xaml` の初期load中ではなく、`App.OnLaunched` で最初のWindow/Page XAMLより前に挿入する。これはself-contained unpackaged hostのresource resolution契約である。

### ADR-006: Enter-submit Search

**Decision**

headerのworld searchはplain textと候補表示だけを持ち、Enterで確定する。

入力中のqueryを即時DB検索へ流さず、`SearchQuery` と確定済み `DebouncedQuery` を分ける。command paletteや特別なprefix parserは持たない。

### ADR-007: Confirmed World Resolution

**Decision**

Polaris archiveによる時間区間補完はpost-scanで自動適用する。PDQ候補はWorld Resolve画面で利用者が確認した項目だけを適用する。

- 同じ source slot の既知写真だけを候補にする。
- distance 75以下、表示一致率71%以上だけを自動候補として見せる。
- 候補外は手動world name入力を許可する。
- 確定済み候補は `phash_confirmed`、手入力は `manual` として由来を保持する。

### ADR-008: Bounded Background Work

**Decision**

写真数に比例して無制限なtaskを作らず、normal useのUI応答性とmemoryを守る範囲で固定上限を使う。

| Work | Bound |
| --- | --- |
| Scan image analysis | 1〜4 parallel、500-row DB batch、100 ms progress interval |
| PDQ analysis | 1〜8 parallel chunks、30 rows/chunk、2 waves/batch |
| World candidate search | 最大12 parallel、125 ms progress interval |
| Thumbnail generation | shared workers 2本 |
| Logger queue | 512 lines、message 8000 chars、file 1 MiB + `.1` |

画面やfolderを切り替える処理は、cancellation tokenだけでなく、実行中taskの完了待ちとgeneration guardも使う。これによりstale UI update、削除後cacheの再作成、旧folderへの遅延DB writeを防ぐ。

## Build and Packaging Contract

### Debug

`Program.Main` が `WinRT.ComWrappersSupport.InitializeComWrappers()` を呼び、Windows App SDK bootstrap `1.6` を明示初期化する。Debugはsystem-installed Windows App Runtimeを使う。Release用reg-free初期化と同時に有効化しない。

```powershell
dotnet build app/Alpheratz.Frontend.csproj
dotnet run --project app/Alpheratz.Frontend.csproj
```

### Release Frontend

Release project設定は次を有効にする。

- `SelfContained=true`
- `WindowsAppSDKSelfContained=true`
- `WindowsAppSdkUndockedRegFreeWinRTInitialize=true`
- `InvariantGlobalization=true`
- `RuntimeIdentifier=win-x64`

通常の `dotnet publish` だけではこのunpackaged WinUI appに必要なXBF layoutが完成しない。`BuildWorks/scripts/publish-app-release.ps1` は次を追加する。

1. build objの全 `*.xbf` を相対pathを保ってpublish rootへcopyする。
2. older generated URIとの互換用に `Alpheratz.Frontend/` 以下にもmirrorする。
3. `Microsoft.UI.Xaml.Controls.pri` をroot `resources.pri` としてcopyする。
4. root `App.xbf`, `MainWindow.xbf`, `resources.pri` の存在を検証する。

XBF source pathは現在のRelease build layoutに依存するため、package scriptとproject output pathを別々に変更しない。

### Launcher and Installer

launcherはself-contained、single-fileのWindows Forms `WinExe`。installerは次を行う。

- `$INSTDIR\app` にfrontend publishを配置する。
- `$INSTDIR` にlauncherとuninstallerを配置する。
- `$INSTDIR\Data` を作成する。
- 同梱したMicrosoft Visual C++ Redistributableのversionをmachine-wide registryと比較し、不足時はappを置換する前に導入する。
- HKCUへ`InstallLocation`と`RuntimeLocation`を保存する。
- Start Menu shortcutを作成し、finish pageで任意のDesktop shortcutを作成する。
- update時はapp binariesだけを入れ替え、Dataを保持する。
- uninstall時はapp、shortcuts、Data、Run key、製品registry keyを削除する。

App本体はcurrent-user installで、Program Files配下はinstaller validationで拒否する。Microsoft Visual C++ Redistributableの導入または更新時だけUAC承認が必要である。Release build全体の入口は次だけである。

```powershell
.\BuildWorks\scripts\build-release.ps1
```

## Verification

### Source and Tests

```powershell
dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj
```

coverageを取得する場合は `tests/coverlet.runsettings` または生成物だけを除外する `tests/coverlet.generated-only.runsettings` を使う。coverage除外fileと理由は `CoveragePolicyTests` のallowlistで固定する。

### Release Artifact

Release/XAML変更では少なくとも次を確認する。

1. `build-release.ps1` が成功する。
2. `BuildWorks/runtime/vc_redist.x64.exe`にMicrosoftの有効なAuthenticode署名がある。
3. publish rootに `App.xbf`, `MainWindow.xbf`, `resources.pri` がある。
4. NSIS installerからcurrent-user installできる。
5. launcher経由でfrontendが起動し、splashからShellへ遷移する。
6. install後のApplication event logに新しいstartup crashがない。

詳細な障害原因と変更禁止点はagent専用の `.claude/xaml-packaging-notes.md` に置き、公開文書には現行契約だけを記載する。

## Current Distribution Constraints

- x64のみ。
- code signingなし。
- MSIX packageなし。
- frontendとlauncherはself-containedのため配布サイズが増える。
