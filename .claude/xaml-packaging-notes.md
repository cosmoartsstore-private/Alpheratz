# XAML Packaging Notes

This is an agent-private handoff for the 2026-06-13 NSIS/self-contained WinUI startup failures.

## Confirmed Cause

The repeated crashes were caused by incomplete XAML resource layout in the unpackaged self-contained publish output, not by gallery sync state interference.

Confirmed failure chain:

- Generated `*.g.i.cs` files use root-relative `ms-appx:///...` URIs for windows, pages, controls, and `App.xaml`.
- `dotnet publish` did not place compiled XBF files at the publish root, so `MainWindow.InitializeComponent()` could fail with generic XAML parse errors after a clean build.
- Adding root XBF exposed the next issue: `XamlControlsResources` in `App.xaml` could fail-fast while `App.InitializeComponent()` was running in this unpackaged desktop host.
- Moving `XamlControlsResources` to `OnLaunched` made the failure catchable and showed the concrete missing resource:
  `ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml`.
- `Microsoft.UI.Xaml.Controls.pri` contains the WinUI theme XBF resource map entries, but the unpackaged app lookup needed the same PRI available as app-root `resources.pri`.
- The custom generated-main path used `DISABLE_XAML_GENERATED_MAIN`, so it also had to perform `WinRT.ComWrappersSupport.InitializeComWrappers()` before `Application.Start`.

Observed evidence during the incident:

- Windows Error Reporting showed `Alpheratz.Frontend.exe` crashing in `Microsoft.UI.Xaml.dll` with `0xc000027b`.
- Dumps showed failures first in `MainWindow.InitializeComponent`, then in `XamlControlsResources..ctor()`, then in shell/control XAML once partial workarounds were tried.
- Verbose app log showed `Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'` before the `resources.pri` fix.
- After copying root XBF and app-root `resources.pri`, both direct publish output and the installed NSIS app stayed alive with a real `MainWindowHandle`.

## Implemented Countermeasures

- `BuildWorks/scripts/publish-app-release.ps1`
  - Copies all generated `*.xbf` files to the publish root, preserving relative paths.
  - Keeps the assembly-name XBF mirror for compatibility with older generated paths.
  - Copies `Microsoft.UI.Xaml.Controls.pri` to `resources.pri` so WinUI stock theme resources resolve in the unpackaged layout.
  - Fails publish if required runtime files such as `App.xbf`, `MainWindow.xbf`, or `resources.pri` are missing.
- `app/App.xaml`
  - No longer declares `XamlControlsResources` during `App.InitializeComponent`.
- `app/App.xaml.cs`
  - Installs `XamlControlsResources` from `OnLaunched` before any window/page/control XAML is loaded.
- `app/Program.cs`
  - Calls `WinRT.ComWrappersSupport.InitializeComWrappers()` in the custom entry point.

## Do Not Regress

- Do not move `XamlControlsResources` back into `App.xaml` unless the startup path is revalidated in the NSIS installed app.
- Do not remove app-root XBF copies. Current generated `LoadComponent` URIs are root-relative.
- Do not remove `resources.pri` without replacing it with a verified WinUI theme resource resolution path.
- Do not re-enable custom app PRI generation casually. Earlier attempts with the wrong package/resource map name caused startup failures.
- Do not assume a direct `dotnet publish` output is valid unless the NSIS-installed launcher path is also checked.

## Configuration Audit Result

No additional fatal architecture mismatch was confirmed beyond the XBF/PRI/custom-main issues fixed above.

Confirmed intentional constraints:

- `WindowsPackageType=None` plus NSIS current-user install is a documented local decision.
- `WindowsAppSDKSelfContained=true` and `WindowsAppSdkUndockedRegFreeWinRTInitialize=true` match the no-runtime-preflight installer policy.
- The installer placing frontend files under `$INSTDIR\app` matches the launcher/runtime-location registry model.

Possibilities intentionally not fixed because current evidence is below the threshold for implementation:

- Switching back to MSIX or packaged WinUI may reduce unpackaged resource edge cases, but that contradicts the current NSIS distribution decision and is not proven necessary.
- Upgrading or downgrading Windows App SDK may affect the `XamlControlsResources` behavior, but the fixed layout works with the pinned `1.6.250205002` package.
- Sync/state orchestration may still deserve future simplification, but it was not the primary cause of this startup crash chain.

## Verification Checklist

For future XAML startup fixes, verify all of these before handoff:

- Stop existing `Alpheratz*` processes.
- Clean `app/artifacts`, `BuildWorks/artifacts`, launcher `bin/obj`, and old installer output.
- Run `BuildWorks/scripts/build-release.ps1`.
- Confirm publish output contains root `App.xbf`, root `MainWindow.xbf`, and root `resources.pri`.
- Install `BuildWorks/Alpheratz-v2-Installer.exe` silently or interactively.
- Launch `$env:LOCALAPPDATA\CosmoArtsStore\Alpheratz\Alpheratz.exe`.
- Wait long enough for splash-to-shell transition and confirm `Alpheratz.Frontend` has a non-zero `MainWindowHandle`.
- Check the Application event log for `Alpheratz` WER/Application Error entries after launch time.
- Run `dotnet test tests\Alpheratz.Tests\Alpheratz.Tests.csproj`.
