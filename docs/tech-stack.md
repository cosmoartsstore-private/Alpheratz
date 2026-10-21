# Tech Stack and Architecture Decisions

現行sourceとbuild scriptsの依存、採用理由、開発command、CI gate、成果物と配布契約を記録する。module責務と実行flowは[Architecture](architecture.md)を正本とする。

## Document Control

| Field | Value |
| --- | --- |
| Application | Alpheratz |
| Scope | 依存とversion、採用理由、開発・検証command、CI、packaging、導入・更新・削除。機能条件・内部構造・保存形式は各専門文書が所有する |
| Audience | 開発環境や配布を管理する人 |
| Authority | [Application project](../app/Alpheratz.Frontend.csproj)、[SDK](../app/global.json)、[Release build](../BuildWorks/scripts/build-release.ps1)、[Installer](../BuildWorks/nsis/Installer.nsi)、[CI](../.github/workflows/ci.yml) |
| Related documents | [README](../README.md)、[Specification](spec.md)、[Architecture](architecture.md)、[Database Schema](database.md)、[Basic Design](basic-design.html) |
| Source review date | 2026-10-22。sourceの静的照合日であり、実行試験日ではない |

## Stack Reference

### Application

| Layer | Dependency | Declared version | Resolved version | Authority | Purpose |
| --- | --- | --- | --- | --- | --- |
| Language | C# | `LangVersion=latest`、nullable | compiler依存 | [csproj](../app/Alpheratz.Frontend.csproj) | application実装 |
| Runtime / target | .NET / Windows | `net8.0-windows10.0.19041.0`、x64 | 配布buildごと | 同上 | build19041以降のWindows |
| UI | Windows App SDK / WinUI 3 | `1.6.250205002` | lockなし。本書ではrestore結果を固定しない | 同上 | native Window / XAML |
| State | CommunityToolkit.Mvvm | `8.4.0` | 同上 | 同上 | observable state / command |
| DI | Microsoft.Extensions.DependencyInjection | `8.0.1` | 同上 | 同上 | 起動時composition |
| Database | Microsoft.Data.Sqlite | `8.0.11` | 同上 | 同上 | local metadata / user data |
| Imaging | Windows APIs / repository PDQ | OS API、独自256 bit / 4回転 | package lock対象外 | [Imaging](../app/Core/Imaging) | decode、thumbnail、類似候補 |
| Messages | embedded UTF-8 properties | `messages.ja.properties` | sourceで管理 | [MessageCatalog](../app/Messages/MessageCatalog.cs) | 日本語UI文言 |

`WindowsPackageType=None`のunpackaged appで、MSIX / app PRI generation toolingを無効にする。現行ReleaseはNativeAOTではない。field-based `[ObservableProperty]`があり、`MVVMTK0045`をprojectで抑止する。

### Build and Distribution

<a id="distribution"></a>

| Layer | Dependency | Declared version | Resolved version | Authority | Purpose |
| --- | --- | --- | --- | --- | --- |
| SDK | .NET SDK | `8.0.420`、`latestPatch` | 実行環境依存 | [app/global.json](../app/global.json) | app配下のSDK選択 |
| CI SDK | .NET SDK | `8.0.x` | runner実行ごと | [CI](../.github/workflows/ci.yml) | rootからrestore / test / package |
| Launcher | .NET Windows Forms | net8.0-windows、win-x64 | buildごと | [Launcher project](../BuildWorks/launcher/Alpheratz.Launcher.csproj) | self-contained single-file起動入口 |
| Installer | NSIS | version固定なし | 導入環境依存 | [build-release](../BuildWorks/scripts/build-release.ps1) | Unicode、PerMonitorV2、current user |
| Native runtime | Microsoft Visual C++ Redistributable x64 | Microsoft公式配布物 | 取得fileのProductVersionをNSISへ渡す | [prepare-vc-redist](../BuildWorks/scripts/prepare-vc-redist.ps1) | self-contained WinUI依存を同梱 |

SDK宣言は`app/global.json`にあり、rootのcommandやCIの`8.0.x`と同じpatch値を保証するものではない。NuGet dependency lock fileは管理していないため、宣言versionと特定実行環境の解決値を区別する。

### Tests

| Layer | Dependency | Declared version | Resolved version | Authority | Purpose |
| --- | --- | --- | --- | --- | --- |
| Test framework | xUnit / runner.visualstudio | `2.5.3` | lockなし。本書ではrestore結果を固定しない | [Test project](../tests/Alpheratz.Tests/Alpheratz.Tests.csproj) | logic / service / data / native境界 |
| Test host | Microsoft.NET.Test.Sdk | `17.8.0` | 同上 | 同上 | test実行 |
| Coverage | coverlet.collector / coverlet.msbuild | `6.0.0` | 同上 | 同上 | 任意coverage取得 |

frontendを直接ProjectReferenceし、launcher sourceもtestへlinkする。state / DB / scanner / imagingの検証、source宣言のpackaging検査、専用processでWinUI / OS境界を使う検証を含む。coverageの除外範囲と実画面検証の範囲は別に管理する。

## Runtime Decisions

### ADR-001: WinUI 3 Single-process Desktop App

.NET 8 + WinUI 3を採用し、Window、Dispatcher、Composition、picker、clipboardを直接使う。UI、scan、画像解析、SQLiteをC# process内で接続し、IPC schema / sidecarを持たない。Windows x64とunpackaged XAML配置が制約になる。

### ADR-002: Feature-local MVVM

Pageはframework interaction、VM / stateは表示状態とuse case、service / coreはI/Oやdomain処理を担当する。実際に再利用する純粋判断だけ`*Logic.cs`へ分ける。実依存とsingleton / 短寿命Pageの所有関係は[Composition and State Ownership](architecture.md#composition-and-state-ownership)を参照する。

### ADR-003: SQLite as Local Metadata Index

単一Windows userのlocal状態をSQLiteへ保存する。WALはgallery readとbackground writeの共存、transactionはtag / scan batch / slot整理の整合性に用いる。DBには再生成可能indexだけでなく、再scanから再生成できないfavorite / tagとその復元用backupもある。原本backupとは異なる。

### ADR-004: Custom PDQ Pipeline

repositoryのPDQで4回転hashを計算し、DCT matrix cache、buffer pool、packed hash / popcountを用いる。保存経路は品質値を使わずhashを保存する。用途はworld候補と完全重複の候補絞込みであり、削除の確定にはSHA-256を使う。

### ADR-005: XAML Resource Dictionaries

Tokens→Brushes→Typography→Buttons→Containersの順でmergeし、feature固有layout / stateはfeature側へ置く。Light / Darkはtheme resourceで切り替える。`XamlControlsResources`は初期App.xaml load時ではなく、`App.OnLaunched`で最初のWindow / Page XAMLより前に挿入する。

### ADR-006: Enter-submit Search

world検索はplain textと候補を持ち、Enter等で確定する。入力中queryと確定queryを分け、入力だけを写真DB検索へ流さない。command palette / 特別prefix構文は持たない。

### ADR-007: Confirmed World Resolution

Polaris `InstallLocation/Data/archive`は時刻区間による補完に用いる。PDQは同slot候補を利用者が確認した場合だけUIから適用し、距離75以下 / 表示71%以上を候補にする。候補外は手入力を許可し、由来を保存する。

### ADR-008: Bounded Background Work

scan、PDQ、candidate ranking、thumbnailは処理ごとの並列上限を持つ。実行task、gate、世代、停止の範囲は[Concurrency and Lifecycle](architecture.md#concurrency-and-lifecycle)が正本であり、全UI / writeに共通する取消し保証へ広げない。

<a id="source-layout"></a>

source treeとmodule責務は[Source Responsibility Map](architecture.md#source-responsibility-map)へ集約する。

## Build and Packaging Contract

### Prerequisites and Debug

<a id="debug"></a>

Windows x64、.NET 8 SDK、Windows 10 SDKを利用できる開発環境、Debug用Windows App Runtime 1.6が必要である。installer生成時はNSISを導入する。scriptは`C:\Program Files (x86)\NSIS\makensis.exe`とその`Bin`配下を候補にする。

以下はrepository rootからのcommandである。

```powershell
dotnet build app/Alpheratz.Frontend.csproj
dotnet run --project app/Alpheratz.Frontend.csproj
```

Debugは`Program.Main`からCOM wrappersとWindows App SDK bootstrapを明示初期化する。system-installed runtimeを使い、Release reg-free初期化と同processで二重に有効化しない。

### Release Frontend

Releaseは`SelfContained=true`、`WindowsAppSDKSelfContained=true`、`WindowsAppSdkUndockedRegFreeWinRTInitialize=true`、`InvariantGlobalization=false`、`RuntimeIdentifier=win-x64`。cultureを日本語tag候補の比較・並び順などに使用する。

通常の`dotnet publish`だけでXBF layoutは完成しない。正式入口は次である。

```powershell
.\BuildWorks\scripts\build-release.ps1
# 有効なVC++ cacheを優先する場合
.\BuildWorks\scripts\build-release.ps1 -UseCachedVcRedist
```

1. frontendをRelease / x64 / win-x64 / self-containedでpublishする。
2. `app/artifacts/obj/x64/Release/net8.0-windows10.0.19041.0/win-x64`の全XBFを相対path保持でpublish rootへcopyし、旧assembly-name URI用に`Alpheratz.Frontend/`へmirrorする。
3. `Microsoft.UI.Xaml.Controls.pri`をrootの`resources.pri`としてcopyする。独自app PRIは生成しない。
4. rootの`App.xbf`、`MainWindow.xbf`、`resources.pri`とcopy件数を確認する。
5. launcherをself-contained single-file publishし、VC++を準備、file versionをNSISへ渡してinstallerを生成する。

frontend projectのoutput pathとscriptのXBF source pathは一体で変更する。互換XBF mirrorの入力に同mirrorを再帰的に含めない。

### Assets, Runtime and Outputs

<a id="launcher-and-installer"></a>

| Component | Output / contract |
| --- | --- |
| Frontend | `BuildWorks/artifacts/publish/Alpheratz` |
| Launcher | `artifacts/publish/Launcher/Alpheratz.exe` |
| Installer | `BuildWorks/Alpheratz-Installer.exe` |
| VC++ cache | `BuildWorks/runtime/vc_redist.x64.exe`。Microsoft公式URLから取得しAuthenticode status / signer / file identityを検証 |
| Bitmap / icon | `LogoText.png`、`avatar.jpg`、`icon.ico`、`icon.png`をcopyし、`Logo.png`は除外 |
| Messages | UTF-8 propertiesを`Alpheratz.Messages.messages.ja.properties`としてembed |

通常buildはVC++をdownloadし、失敗時も既存valid cacheがあれば再利用する。`-UseCachedVcRedist`はvalid cacheをdownloadより優先する指定であり、署名検証を省略しない。製品署名とVC++配布物の署名検証は別である。

## Verification Commands and CI

<a id="verification"></a>
<a id="source-and-tests"></a>

| Purpose | Command | Configuration | Scope | CI gate |
| --- | --- | --- | --- | --- |
| Restore | `dotnet restore tests/Alpheratz.Tests/Alpheratz.Tests.csproj` | runnerの.NET 8 | app / test依存 | 実行する |
| Source and tests | `dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj` | 既定Debug | project内test。native境界も含む | CIはDebug / no-restore / minimalで実行 |
| Release tests | `dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj --configuration Release` | self-contained、専用XBF / PRI配置 | Release構成で同projectを実行 | CIにはない |
| Coverage | `dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj --collect:"XPlat Code Coverage" --settings tests/coverlet.generated-only.runsettings` | 指定runsettings | 生成obj以外をpatternで除外せず、属性allowlistは適用 | CIには収集・閾値判定なし |
| Release artifact | `.\BuildWorks\scripts\build-release.ps1` | Release | XBF / PRI、launcher、VC++、NSIS生成 | 実行する |
| Installed app | installer導入後、外側launcherからsplash→Shellを確認 | 実配置 | current-user導入、registry、起動、Application event log | 自動CI stepなし |

CIはmain push / main宛PR / workflow_dispatchをwindows-2025で処理し、NSIS導入→restore→Debug test→最終installer生成の順に進む。soft-failはなく、同branchの古いrunは新runでcancelする。installer uploadやrelease公開は行わない。

`tests/coverlet.runsettings`はXAML code-behindなどをfile patternでも除外する別設定である。`CoveragePolicyTests`は`ExcludeFromCodeCoverage`のfile / 理由allowlistを固定するが、現行CIにcoverage 80% gateはない。実行条件・結果・件数・receiptはrunごとの内部証拠で管理し、通常suite成功を全画面・全Release・実installer検証完了とは扱わない。

<a id="release-artifact"></a>

Release / XAML変更の配布確認では、build成功、VC++署名、required XBF / PRI、installed launcherからの起動、splash→Shell、起動時刻後のApplication event logを確認する。これは確認手順であり、この文書の静的照合日に実行したという意味ではない。

## Installation Lifecycle

| Stage | Behavior | Retention, failure and retry |
| --- | --- | --- |
| 通常導入 | `Alpheratz-Installer.exe`、既定LocalAppData、current user。Program Files配下は対話 / silentとも拒否 | appはuser scope。不足VC++だけmachine-wide導入 / 更新時にUACが必要 |
| Runtime確認 | HKLM Installed / Versionと同梱VC++ versionを比較し、必要時に`/install /passive /norestart` | app置換前に実行。失敗は既存appを変更せず中止。3010はreboot flag |
| 更新 / 再導入 | 同じinstall先のlauncherとappを置換、Dataを作成 / 保持、registryとshortcutを更新 | 使用中fileは配置失敗となり得る。installerはprocessを自動終了しないため、実行中アプリを閉じて再実行する。binary全体のrollback保証はない |
| 登録 | HKCU InstallLocation / RuntimeLocation、uninstall情報、Start Menu shortcut、任意Desktop shortcut | 自動起動Runはアプリ設定で管理 |
| 通常uninstall | app、launcher、shortcut、標準Data/db・logs・cache、Run、製品 / uninstall registry、uninstallerを削除 | 写真原本・Polaris archive・共有VC++は保持。Data / install rootは空時だけ削除 |
| 削除失敗 | 非0結果と再試行案内、未完了fileとuninstallerを残す | 使用中fileや権限を解消して再実行。独自追加した他のData子fileまで再帰削除しない |

データ別の消失・保持と手動backupは[Deletion and Retention](database.md#deletion-and-retention)を参照する。launcherはHKCU RuntimeLocationを優先し、なければLocalAppData既定pathを使用する。起動例外は`Data/logs/launcher_error.log`への記録を試みる。

## Current Distribution Constraints

- Windows x64のみ、MSIX packageなし。
- 製品code signingなし。SmartScreen警告が出る場合がある。
- frontend / launcherはself-containedのため配布sizeが増える。
- 配布buildはVC++取得またはvalid cacheと署名検証、NSIS、固定XBF配置に依存する。

## References

- [Architecture](architecture.md): source責務、composition、state、event、lifecycle。
- [Specification](spec.md): 機能と利用者に見える制約。
- [Database Schema](database.md): 保存形式・transaction・保持・削除。
- [README](../README.md): 利用・導入と開発入口。
