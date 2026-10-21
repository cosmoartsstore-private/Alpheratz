using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace Alpheratz.Tests;

public sealed partial class NativeStartupTests
{
    [Fact]
    public Task NativeStartup_SettingsThemeSaveFailureKeepsActualThemeAndSameButtonRetries() => RunIsolatedAsync("settings-theme-retry", "dark", false);

    [Fact]
    public Task NativeStartup_SettingsStartupSaveFailureRestoresToggleAndSameControlRetries() => RunIsolatedAsync("settings-startup-retry", "dark", false);

    [Fact]
    public Task NativeStartup_SettingsWorldLinkFailureKeepsCommittedValuesAndToggleRetry() => RunIsolatedAsync("settings-world-link-retry", "dark", false);

    private static bool IsGeneralSettingsScenario(string kind) => kind is "settings-theme-retry" or "settings-startup-retry" or "settings-world-link-retry";

    /// <summary>実headerから全般設定を開き、実control・本体callback・JSON／専用HKCUを同じ経路で確認する。</summary>
    private static async Task VerifyGeneralSettings(ShellPage shell, ShellViewModel viewModel, StartupObservation observed, string kind)
    {
        var photos = viewModel.galleryViewModel.photosState.photos.ToArray(); Assert.Equal(2, photos.Length); observed.SavedPhotoCount = 2;
        var imageBytes = photos.ToDictionary(photo => photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar), photo => ReadSharedBytes(photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar)));
        var install = AppPaths.InstallLocation!; var settingPath = Path.Combine(install, "Data", "db", "setting.json");
        var dbPath = Path.Combine(install, "Data", "db", "Alpheratz.db"); var beforeRows = DatabaseSnapshot(dbPath);
        var editedKey = kind == "settings-theme-retry" ? "themeMode" : kind == "settings-startup-retry" ? "enableStartup" : "openWorldLinkOnPost";
        var beforeFields = GeneralSettingFields(editedKey); var beforeRegistry = GeneralRegistryOtherValues();
        var stage = Assert.Single(Descendants(shell).OfType<ShellStage>()); var gallery = Assert.Single(Descendants(shell).OfType<GalleryPage>());
        var page = await OpenGeneralSettings(shell, stage); var snapshots = new List<object>();
        var toasts = App.Services!.GetRequiredService<ToastService>(); toasts.toasts.Clear();
        var themeButton = TagElement<Button>(page, "ThemeSwitchButton");
        var startup = Assert.Single(Descendants(page).OfType<ToggleSwitch>(), control => AutomationProperties.GetName(control) == "ログイン時に起動");
        var world = Assert.Single(Descendants(page).OfType<ToggleSwitch>(), control => AutomationProperties.GetName(control) == "投稿時にワールドページを開く");
        Assert.Equal(ThemeMode.dark, viewModel.ThemeMode); Assert.False(startup.IsOn); Assert.False(world.IsOn);
        void Record(string step, bool verify = true)
        {
            observed.Step = kind + ": " + step;
            var rowsPreserved = beforeRows == DatabaseSnapshot(dbPath);
            var fieldsPreserved = beforeFields == GeneralSettingFields(editedKey);
            var registryPreserved = beforeRegistry == GeneralRegistryOtherValues();
            var imagesPreserved = imageBytes.All(item => item.Value.SequenceEqual(ReadSharedBytes(item.Key)));
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            snapshots.Add(new { step, settings_loaded = page.IsLoaded, shell_theme = shell.ActualTheme.ToString(), page_theme = page.ActualTheme.ToString(),
                theme_button = themeButton.Content as string, theme_tooltip = ToolTipService.GetToolTip(themeButton) as string,
                shell_theme_mode = viewModel.ThemeMode.ToString(), settings_theme_mode = viewModel.settingsViewModel.ThemeMode.ToString(),
                startup_toggle = startup.IsOn, shell_startup = viewModel.StartupEnabled, settings_startup = viewModel.settingsViewModel.StartupEnabled,
                world_toggle = world.IsOn, shell_world_link = viewModel.OpenWorldLinkOnPost, settings_world_link = viewModel.settingsViewModel.OpenWorldLinkOnPost,
                template_world_link = viewModel.templatePageViewModel.OpenWorldLinkOnPost, run_registration = run?.GetValue("Alpheratz") as string,
                setting = JsonSerializer.SerializeToElement(new AppConfig().LoadSetting()), toasts = toasts.toasts.Select(item => new { item.Msg, type = item.Type.ToString() }).ToArray(),
                database_rows_preserved = rowsPreserved, other_setting_fields_preserved = fieldsPreserved, other_registry_values_preserved = registryPreserved, image_bytes_preserved = imagesPreserved });
            if (verify) { Assert.True(rowsPreserved); Assert.True(fieldsPreserved); Assert.True(registryPreserved); Assert.True(imagesPreserved); }
        }
        try
        {
            Record("opened"); observed.PhotoModalChecks.Add("actual-header-opens-general-settings-with-shared-di-xbf-and-two-saved-photos");
            if (kind == "settings-theme-retry")
            {
                await AssertGeneralTheme(shell, page, viewModel, ThemeMode.dark); var beforeBytes = ReadSharedBytes(settingPath);
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.False(await ClickGeneralTheme(page));
                    await WaitPhotoFailure(toasts, "テーマを変更できませんでした。テーマをもう一度切り替えてください。");
                    Assert.Equal(beforeBytes, ReadSharedBytes(settingPath)); await AssertGeneralTheme(shell, page, viewModel, ThemeMode.dark); Record("save-failed");
                }
                toasts.toasts.Clear(); Assert.True(await ClickGeneralTheme(page)); await AssertGeneralTheme(shell, page, viewModel, ThemeMode.light);
                Assert.Empty(toasts.toasts); Record("same-button-retry-saved-light");
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenGeneralSettings(shell, stage));
                await AssertGeneralTheme(shell, page, viewModel, ThemeMode.light); Record("cached-page-reopened-light");
                Assert.True(await ClickGeneralTheme(page)); await AssertGeneralTheme(shell, page, viewModel, ThemeMode.dark); Record("same-button-saved-dark");
                observed.PhotoModalChecks.Add("actual-theme-button-failed-save-holds-json-and-real-theme-retry-and-cache-reopen-round-trip");
            }
            else if (kind == "settings-startup-retry")
            {
                // AppConfigが登録先の存在を検証するため、専用installに登録対象fileを置く。実行は行わない。
                var launcher = Path.Combine(install, "Alpheratz.exe"); var launcherBytes = System.Text.Encoding.UTF8.GetBytes("起動登録検証専用file");
                Assert.False(File.Exists(launcher)); File.WriteAllBytes(launcher, launcherBytes); var beforeBytes = ReadSharedBytes(settingPath);
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var requests = await ToggleGeneralPreference(page, startup, startupPreference: true);
                    await WaitPhotoFailure(toasts, "ログイン時起動の設定を変更できませんでした。設定をもう一度切り替えてください。");
                    await WaitPhotoUi(() => !startup.IsOn, "保存失敗後にログイン時起動の実toggleが戻りません。");
                    Assert.Equal([true, false], requests); Assert.False(viewModel.StartupEnabled); Assert.False(viewModel.settingsViewModel.StartupEnabled);
                    Assert.Equal(beforeBytes, ReadSharedBytes(settingPath)); AssertGeneralStartupRegistration(false, launcher); Record("save-failed-toggle-restored");
                }
                toasts.toasts.Clear(); Assert.Equal([true], await ToggleGeneralPreference(page, startup, startupPreference: true));
                Assert.True(startup.IsOn); Assert.True(viewModel.StartupEnabled); Assert.True(viewModel.settingsViewModel.StartupEnabled); AssertGeneralStartupRegistration(true, launcher);
                Assert.Equal("Alpheratzをログイン時に起動するよう設定しました。", Assert.Single(toasts.toasts).Msg); Record("same-toggle-retry-registered");
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenGeneralSettings(shell, stage)); Assert.True(startup.IsOn); AssertGeneralStartupRegistration(true, launcher); Record("cached-page-reopened-enabled");
                toasts.toasts.Clear(); Assert.Equal([false], await ToggleGeneralPreference(page, startup, startupPreference: true));
                Assert.False(startup.IsOn); Assert.False(viewModel.StartupEnabled); Assert.False(viewModel.settingsViewModel.StartupEnabled); AssertGeneralStartupRegistration(false, launcher);
                Assert.Equal("Alpheratzのログイン時起動を無効にしました。", Assert.Single(toasts.toasts).Msg); Assert.Equal(launcherBytes, ReadSharedBytes(launcher)); Record("same-toggle-disabled");
                observed.PhotoModalChecks.Add("actual-startup-toggle-failed-json-save-restores-binding-and-private-run-registration-retry-enable-disable");
            }
            else
            {
                var beforeBytes = ReadSharedBytes(settingPath);
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.Contains(true, await ToggleGeneralPreference(page, world, startupPreference: false));
                    await WaitPhotoFailure(toasts, "投稿時のワールドリンク設定を変更できませんでした。設定をもう一度切り替えてください。");
                    Assert.Equal(beforeBytes, ReadSharedBytes(settingPath)); Assert.False(viewModel.OpenWorldLinkOnPost); Assert.False(viewModel.templatePageViewModel.OpenWorldLinkOnPost);
                    Assert.False(world.IsOn); Assert.False(viewModel.settingsViewModel.OpenWorldLinkOnPost); Record("save-failed-toggle-restored-to-committed-value");
                }
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenGeneralSettings(shell, stage));
                Assert.False(world.IsOn); Assert.False(new AppConfig().LoadSetting().OpenWorldLinkOnPost); Record("cached-page-reopened-with-committed-toggle");
                toasts.toasts.Clear();
                Assert.Equal([true], await ToggleGeneralPreference(page, world, startupPreference: false));
                Assert.True(world.IsOn); Assert.True(viewModel.OpenWorldLinkOnPost); Assert.True(viewModel.settingsViewModel.OpenWorldLinkOnPost); Assert.True(viewModel.templatePageViewModel.OpenWorldLinkOnPost);
                Assert.True(new AppConfig().LoadSetting().OpenWorldLinkOnPost); Assert.Empty(toasts.toasts); Record("toggle-on-retry-saved");
                Assert.Equal([false], await ToggleGeneralPreference(page, world, startupPreference: false));
                Assert.False(world.IsOn); Assert.False(viewModel.OpenWorldLinkOnPost); Assert.False(viewModel.settingsViewModel.OpenWorldLinkOnPost); Assert.False(viewModel.templatePageViewModel.OpenWorldLinkOnPost);
                Assert.False(new AppConfig().LoadSetting().OpenWorldLinkOnPost); Record("toggle-off-saved");
                observed.PhotoModalChecks.Add("actual-world-link-toggle-failure-restores-committed-ui-and-same-toggle-retry-saves-both-values");
            }
            await CloseTagSettings(page, stage); Assert.Same(gallery, Assert.Single(Descendants(shell).OfType<GalleryPage>()));
            Record("closed-gallery-preserved"); observed.PhotoModalChecks.Add("actual-general-settings-close-unloads-with-gallery-and-protected-storage-preserved");
        }
        finally
        {
            Record("final", verify: false); AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant);
            var root = FixtureDirectory(Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N"));
            File.WriteAllText(Path.Combine(root, "general-preferences-observation.json"), JsonSerializer.Serialize(new
            {
                kind, actual_main = true, actual_xaml = true, invariant_globalization = invariant, edited_setting_key = editedKey,
                build_configuration = typeof(NativeStartupTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                    .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration,
                snapshots, source_setting_callbacks_modified_by_test = false, callback_completion_observed_by_forwarding = true,
                real_user_run_registry_modified = false, installed_launcher_executed = false, verification_complete = false,
            }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
    }

    private static async Task<SettingsPage> OpenGeneralSettings(ShellPage shell, ShellStage stage)
    {
        var header = Assert.Single(Descendants(shell).OfType<ShellHeaderBar>());
        await ClickPhotoButton(Assert.IsType<Button>(header.FindName("SettingsGearBtn")));
        await WaitPhotoUi(() => Descendants(shell).OfType<SettingsPage>().Any(item => item.IsLoaded), "実headerから全般設定を表示できません。");
        var page = Assert.Single(Descendants(shell).OfType<SettingsPage>()); var composite = Assert.IsType<SettingsCompositeViewModel>(page.DataContext);
        var viewModel = App.Services!.GetRequiredService<ShellViewModel>();
        Assert.Same(viewModel.settingsViewModel, composite.Settings); Assert.Same(viewModel.tagMasterViewModel, composite.TagMaster); Assert.Same(viewModel.templatePageViewModel, composite.Template);
        Assert.Equal(Visibility.Visible, TagElement<StackPanel>(page, "GeneralSection").Visibility);
        foreach (var name in new[] { "SimilarPhotosSection", "TagMasterSection", "TemplateSection", "CreditsSection" }) Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, name).Visibility);
        Assert.Equal(Visibility.Visible, stage.ModalVisibility); Assert.Equal(Visibility.Collapsed, stage.TopModalVisibility); return page;
    }

    /// <summary>委譲先と引数を維持して本体Taskを待ち、async voidの末尾で更新する表示もqueueの完了後に照合する。</summary>
    private static async Task<bool> ClickGeneralTheme(SettingsPage page)
    {
        var original = Assert.IsType<Func<bool, Task<bool>>>(page.OnThemeChanged); var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.OnThemeChanged = async next =>
        {
            try { var accepted = await original(next); done.TrySetResult(accepted); return accepted; }
            catch (Exception error) { done.TrySetException(error); throw; }
        };
        try
        {
            InvokePhotoButton(TagElement<Button>(page, "ThemeSwitchButton")); var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await DrainGeneralSettingsQueue(page); return result;
        }
        finally { page.OnThemeChanged = original; }
    }

    private static async Task<bool[]> ToggleGeneralPreference(SettingsPage page, ToggleSwitch control, bool startupPreference)
    {
        Assert.True(control.IsEnabled);
        var original = Assert.IsType<Func<bool, Task>>(startupPreference ? page.OnStartupPreferenceChanged : page.OnOpenWorldOnPostChanged);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var requests = new List<bool>();
        Func<bool, Task> observe = async next =>
        {
            requests.Add(next); var first = requests.Count == 1;
            try { await original(next); if (first) done.TrySetResult(); }
            catch (Exception error) { if (first) done.TrySetException(error); throw; }
        };
        if (startupPreference) page.OnStartupPreferenceChanged = observe; else page.OnOpenWorldOnPostChanged = observe;
        try
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control) ?? new ToggleSwitchAutomationPeer(control);
            Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle)).Toggle();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); await DrainGeneralSettingsQueue(page); return requests.ToArray();
        }
        finally { if (startupPreference) page.OnStartupPreferenceChanged = original; else page.OnOpenWorldOnPostChanged = original; }
    }

    private static async Task DrainGeneralSettingsQueue(SettingsPage page)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => done.SetResult())); await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task AssertGeneralTheme(ShellPage shell, SettingsPage page, ShellViewModel viewModel, ThemeMode expected)
    {
        var theme = expected == ThemeMode.dark ? ElementTheme.Dark : ElementTheme.Light; var button = TagElement<Button>(page, "ThemeSwitchButton");
        await WaitPhotoUi(() => shell.ActualTheme == theme && page.ActualTheme == theme && button.Content is string text && text == (expected == ThemeMode.dark ? "ダーク" : "ライト"), "保存テーマが実Shell／Settingsとbuttonへ反映されません。");
        Assert.Equal(theme, Assert.IsType<Grid>(App.MainWindowInstance!.Content).ActualTheme);
        Assert.Equal(expected, viewModel.ThemeMode); Assert.Equal(expected, viewModel.settingsViewModel.ThemeMode); Assert.Equal(expected.ToString(), new AppConfig().LoadSetting().ThemeMode);
        Assert.Equal(expected == ThemeMode.dark ? "ライトテーマに切り替える" : "ダークテーマに切り替える", ToolTipService.GetToolTip(button));
    }

    private static void AssertGeneralStartupRegistration(bool enabled, string launcher)
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); Assert.NotNull(run);
        if (enabled) { Assert.Equal($"\"{launcher}\"", run.GetValue("Alpheratz")); Assert.Equal(RegistryValueKind.String, run.GetValueKind("Alpheratz")); }
        else Assert.Null(run.GetValue("Alpheratz"));
        var setting = new AppConfig().LoadSetting(); Assert.Equal(enabled, setting.EnableStartup); Assert.True(setting.StartupPreferenceSet);
    }

    private static string GeneralSettingFields(string editedKey) => JsonSerializer.Serialize(new SortedDictionary<string, JsonElement>(
        JsonSerializer.SerializeToElement(new AppConfig().LoadSetting()).EnumerateObject().Where(item => item.Name != editedKey).ToDictionary(item => item.Name, item => item.Value)));

    private static string GeneralRegistryOtherValues()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); Assert.NotNull(run);
        return JsonSerializer.Serialize(run.GetValueNames().Where(name => name != "Alpheratz").OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new { name, value = run.GetValue(name), kind = run.GetValueKind(name).ToString() }));
    }
}
