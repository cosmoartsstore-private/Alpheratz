using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Features.Template;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;

namespace Alpheratz.Tests;

public sealed partial class NativeStartupTests
{
    [Fact]
    public Task NativeStartup_TemplateInputValidationAndEditDeletePersistFromActualControls() => RunIsolatedAsync("template-controls", "dark", false);

    [Fact]
    public Task NativeStartup_TemplateFailedSaveKeepsCommittedJsonAndActualUiCanSaveAgain() => RunIsolatedAsync("template-save-retry", "dark", false);

    [Fact]
    public Task NativeStartup_TemplateTokenInsertionAndPlaceholderWarningBoundariesPreserveStorage() => RunIsolatedAsync("template-token-boundaries", "dark", false);

    [Fact]
    public Task NativeStartup_TemplateFailedEditKeepsSavedCardAndEditingInputForRetry() => RunIsolatedAsync("template-edit-retry", "dark", false);

    [Fact]
    public Task NativeStartup_TemplateFailedDeleteKeepsActiveCardAndEditingInputForRetry() => RunIsolatedAsync("template-delete-retry", "dark", false);

    [Fact]
    public Task NativeStartup_TemplateFailedSelectionKeepsSavedActiveCardAndCanRetry() => RunIsolatedAsync("template-selection-retry", "dark", false);

    private static bool IsTemplateSettingsScenario(string kind) => kind is "template-controls" or "template-save-retry" or "template-token-boundaries"
        or "template-edit-retry" or "template-delete-retry" or "template-selection-retry";

    /// <summary>実入力欄とbuttonからテンプレートを編集し、保存値と対象外のDB・写真・設定を照合する。</summary>
    private static async Task VerifyTemplateSettings(ShellPage shell, ShellViewModel viewModel, StartupObservation observed, string kind)
    {
        var stage = Assert.Single(Descendants(shell).OfType<ShellStage>()); var gallery = Assert.Single(Descendants(shell).OfType<GalleryPage>());
        var photos = viewModel.galleryViewModel.photosState.photos.ToArray(); Assert.Equal(2, photos.Length); observed.SavedPhotoCount = 2;
        var images = photos.ToDictionary(photo => photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar), photo => ReadSharedBytes(photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar)));
        var install = AppPaths.InstallLocation!; var settingPath = Path.Combine(install, "Data", "db", "setting.json");
        var dbPath = Path.Combine(install, "Data", "db", "Alpheratz.db"); var beforeRows = DatabaseSnapshot(dbPath);
        var beforeFields = TemplateOtherSettingFields(); var beforeRegistry = GeneralRegistryOtherValues();
        var page = await OpenTemplateSettings(shell, stage); var template = viewModel.templatePageViewModel;
        var toasts = App.Services!.GetRequiredService<ToastService>(); toasts.toasts.Clear();
        var input = TagElement<TextBox>(page, "TemplateInputBox"); var save = TagElement<Button>(page, "SaveButton");
        var initialBytes = ReadSharedBytes(settingPath); var snapshots = new List<object>();
        void Record(string step, bool verify = true)
        {
            observed.Step = kind + ": " + step;
            var rowsPreserved = beforeRows == DatabaseSnapshot(dbPath); var fieldsPreserved = beforeFields == TemplateOtherSettingFields();
            var registryPreserved = beforeRegistry == GeneralRegistryOtherValues(); var imagesPreserved = images.All(item => item.Value.SequenceEqual(ReadSharedBytes(item.Key)));
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); var startupPreserved = run?.GetValue("Alpheratz") is null;
            snapshots.Add(new { step, settings_loaded = page.IsLoaded, template_section_visible = TagElement<StackPanel>(page, "TemplateSection").Visibility == Visibility.Visible,
                input = input.Text, draft = template.TweetTemplateDraft, selection_start = input.SelectionStart, selection_length = input.SelectionLength,
                editing = template.EditingTweetTemplate, save_enabled = save.IsEnabled, save_button = save.Content as string,
                templates = template.tweetTemplates.ToArray(), active = template.ActiveTweetTemplate,
                items = TagElement<ItemsControl>(page, "TemplateList").Items.Cast<string>().ToArray(),
                validation = TagElement<TextBlock>(page, "TemplateValidationText").Text, character_count = TagElement<TextBlock>(page, "TemplateCharacterCountText").Text,
                setting = JsonSerializer.SerializeToElement(new AppConfig().LoadSetting()), toasts = toasts.toasts.Select(item => new { item.Msg, type = item.Type.ToString() }).ToArray(),
                rendered_toast_messages = Descendants(Assert.Single(Descendants(shell).OfType<ToastHost>())).OfType<TextBlock>().Select(item => item.Text).ToArray(),
                database_rows_preserved = rowsPreserved, other_setting_fields_preserved = fieldsPreserved, other_registry_values_preserved = registryPreserved,
                startup_registration_preserved = startupPreserved, image_bytes_preserved = imagesPreserved });
            if (verify) { Assert.True(rowsPreserved); Assert.True(fieldsPreserved); Assert.True(registryPreserved); Assert.True(startupPreserved); Assert.True(imagesPreserved); }
        }
        try
        {
            await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用");
            Assert.Equal("テンプレートを追加", TagElement<TextBlock>(page, "EditorModeLabel").Text);
            Assert.Equal(Visibility.Collapsed, TagElement<Button>(page, "CancelEditButton").Visibility); Assert.False(save.IsEnabled); Record("opened");
            observed.PhotoModalChecks.Add("actual-header-and-navigation-open-template-xbf-with-shared-di-and-existing-saved-template");
            if (kind == "template-controls")
            {
                await SetTemplateInput(page, template, "   ", false, 3);
                await SetTemplateInput(page, template, " 起動検証専用 ", false, 14, "同じテンプレートが登録されています。");
                await SetTemplateInput(page, template, new string('界', 140), true, 280);
                input.Text = new string('界', 141);
                await WaitPhotoUi(() => input.Text == new string('界', 140) && template.TweetTemplateDraft == input.Text
                    && TagElement<TextBlock>(page, "TemplateValidationText").Text == "Xの標準上限は280文字です。これ以上は入力できません。", "加重上限を超えた実入力が拒否されません。");
                Assert.Equal("X換算：280/280", TagElement<TextBlock>(page, "TemplateCharacterCountText").Text);
                await SetTemplateInput(page, template, new string('A', 280), true, 280);
                input.Text = new string('A', 281);
                await DrainGeneralSettingsQueue(page); Assert.Equal(new string('A', 280), input.Text); Assert.Equal(input.Text, template.TweetTemplateDraft);
                Assert.Equal(initialBytes, ReadSharedBytes(settingPath)); Record("blank-duplicate-weighted-and-raw-length-boundaries");

                await SetTemplateInput(page, template, "ABCD", true, 4); input.SelectionStart = 1; input.SelectionLength = 2;
                var quickTag = Assert.Single(Descendants(TagElement<StackPanel>(page, "TemplateSection")).OfType<Button>(), button => Equals(button.Content, "ワールド名"));
                await ClickPhotoButton(quickTag); await WaitPhotoUi(() => input.Text == "A{world-name}D" && template.TweetTemplateDraft == input.Text
                    && TagElement<TextBlock>(page, "TemplateCharacterCountText").Text == "X換算：14/280", "実buttonのtoken挿入と加重文字数の表示が同期しません。");
                Assert.Equal(13, input.SelectionStart); Assert.Equal(0, input.SelectionLength); Assert.Equal("X換算：14/280", TagElement<TextBlock>(page, "TemplateCharacterCountText").Text);
                Assert.Equal(initialBytes, ReadSharedBytes(settingPath)); Record("quick-token-replaces-selection-without-saving");

                const string created = "作成 {world-name}";
                await SetTemplateInput(page, template, "  作成 {world-name}  ", true, 21);
                toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用", created], "起動検証専用"); AssertTemplateSavedToast(toasts); Record("new-template-saved-and-draft-cleared");
                var beforeCancel = ReadSharedBytes(settingPath);
                await StartTemplateEdit(page, template, "起動検証専用");
                await SetTemplateInput(page, template, created, false, 17, "同じテンプレートが登録されています。");
                await ClickPhotoButton(TagElement<Button>(page, "CancelEditButton"));
                await WaitPhotoUi(() => template.EditingTweetTemplate is null && input.Text.Length == 0 && !save.IsEnabled, "実編集Cancelが入力と編集状態を解除しません。");
                Assert.Equal(beforeCancel, ReadSharedBytes(settingPath)); Record("duplicate-edit-disabled-and-cancel-preserves-json");

                const string edited = "編集済み {date}";
                await StartTemplateEdit(page, template, "起動検証専用"); await SetTemplateInput(page, template, edited, true, 15);
                toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, [edited, created], edited); AssertTemplateSavedToast(toasts); Record("active-template-edit-updates-value-and-selection");
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenTemplateSettings(shell, stage));
                await AssertTemplateStored(page, template, [edited, created], edited); Record("cached-page-reopened-with-saved-edit");
                toasts.toasts.Clear(); await DeleteTemplateThroughActualButton(page, edited);
                await AssertTemplateStored(page, template, [created], created); AssertTemplateSavedToast(toasts); Record("active-delete-selects-remaining-template");
                toasts.toasts.Clear(); await DeleteTemplateThroughActualButton(page, created);
                await AssertTemplateStored(page, template, [], ""); AssertTemplateSavedToast(toasts); Record("last-delete-persists-empty-selection");
                await SetTemplateInput(page, template, "起動検証専用", true, 12); toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用"); AssertTemplateSavedToast(toasts); Record("empty-list-recreates-initial-template");
                observed.PhotoModalChecks.Add("actual-template-input-duplicate-and-length-guards-token-selection-save-edit-cancel-active-and-last-delete-recreate");
            }
            else if (kind == "template-token-boundaries")
            {
                var quickTag = Assert.Single(Descendants(TagElement<StackPanel>(page, "TemplateSection")).OfType<Button>(), button => Equals(button.Content, "ワールド名"));
                await SetTemplateInput(page, template, "ABC", true, 3); input.SelectionStart = 0; input.SelectionLength = 0;
                await ClickPhotoButton(quickTag);
                await WaitPhotoUi(() => input.Text == "{world-name}ABC" && template.TweetTemplateDraft == input.Text
                    && TagElement<TextBlock>(page, "TemplateCharacterCountText").Text == "X換算：15/280", "先頭の空選択範囲へtokenが挿入されません。");
                Assert.Equal(12, input.SelectionStart); Assert.Equal(0, input.SelectionLength); Record("empty-selection-inserts-token-at-start");

                var overLimitBase = new string('A', 269);
                await SetTemplateInput(page, template, overLimitBase, true, 269); input.SelectionStart = 269; input.SelectionLength = 0;
                await ClickPhotoButton(quickTag);
                await WaitPhotoUi(() => TagElement<TextBlock>(page, "TemplateValidationText").Text == "Xの標準上限は280文字です。これ以上は入力できません。", "token追加による上限超過が拒否されません。");
                Assert.Equal(overLimitBase, input.Text); Assert.Equal(overLimitBase, template.TweetTemplateDraft); Assert.True(save.IsEnabled);
                Assert.Equal(269, input.SelectionStart); Assert.Equal(0, input.SelectionLength); Assert.Equal("X換算：269/280", TagElement<TextBlock>(page, "TemplateCharacterCountText").Text);
                Assert.Equal(Visibility.Visible, TagElement<TextBlock>(page, "TemplateValidationText").Visibility); Record("over-limit-token-preserves-draft-and-selection");

                var exactLimitBase = new string('A', 268);
                await SetTemplateInput(page, template, exactLimitBase, true, 268); input.SelectionStart = 268; input.SelectionLength = 0;
                await ClickPhotoButton(quickTag);
                await WaitPhotoUi(() => input.Text == exactLimitBase + "{world-name}" && template.TweetTemplateDraft == input.Text
                    && TagElement<TextBlock>(page, "TemplateCharacterCountText").Text == "X換算：280/280"
                    && TagElement<TextBlock>(page, "TemplateValidationText").Text == "残り0文字です。写真の情報を入れた後に上限を超えると、投稿準備を中止します。", "入力を短くした後のtoken挿入が上限ちょうどで受理されません。");
                Assert.True(save.IsEnabled); Assert.Equal(280, input.SelectionStart); Assert.Equal(0, input.SelectionLength); Record("exact-limit-token-and-zero-remaining-warning");

                input.SelectionStart = 0; input.SelectionLength = 280; await ClickPhotoButton(quickTag);
                await WaitPhotoUi(() => input.Text == "{world-name}" && template.TweetTemplateDraft == input.Text && save.IsEnabled
                    && TagElement<TextBlock>(page, "TemplateCharacterCountText").Text == "X換算：12/280"
                    && TagElement<TextBlock>(page, "TemplateValidationText").Text == "", "全選択範囲がtokenへ置換され、警告が解除されません。");
                Assert.Equal(Visibility.Collapsed, TagElement<TextBlock>(page, "TemplateValidationText").Visibility);
                Assert.Equal(12, input.SelectionStart); Assert.Equal(0, input.SelectionLength); Record("full-selection-replaced-with-token-and-warning-cleared");

                await SetTemplateInput(page, template, new string('A', 247) + "{world-name}", true, 259); Record("placeholder-remaining-21-has-no-warning");
                await SetTemplateInput(page, template, new string('A', 248) + "{world-name}", true, 260,
                    "残り20文字です。写真の情報を入れた後に上限を超えると、投稿準備を中止します。"); Record("placeholder-remaining-20-shows-warning");
                await SetTemplateInput(page, template, new string('A', 249) + "{world-name}", true, 261,
                    "残り19文字です。写真の情報を入れた後に上限を超えると、投稿準備を中止します。"); Record("placeholder-remaining-19-keeps-warning");
                await SetTemplateInput(page, template, new string('A', 248) + "{other-name}", true, 260); Record("unknown-placeholder-remaining-20-has-no-warning");
                Assert.Equal(initialBytes, ReadSharedBytes(settingPath)); Assert.Empty(toasts.toasts);
                await SetTemplateInput(page, template, "", false, 0); await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用");
                observed.PhotoModalChecks.Add("actual-token-empty-and-full-selection-exact-limit-rejection-retry-and-placeholder-warning-21-20-19-with-storage-preserved");
            }
            else if (kind == "template-edit-retry")
            {
                const string edited = "編集後 {date}";
                await StartTemplateEdit(page, template, "起動検証専用"); await SetTemplateInput(page, template, edited, true, 13);
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await SaveTemplateThroughActualButton(page);
                    await WaitTemplateFailure(shell, toasts, "テンプレートを保存できませんでした。変更内容は画面に残っています。もう一度保存してください。");
                    Assert.Equal(initialBytes, ReadSharedBytes(settingPath));
                    await AssertTemplateEditingState(page, template, ["起動検証専用"], "起動検証専用", "起動検証専用", edited);
                    Record("failed-edit-keeps-committed-card-active-and-editing-input");
                }
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenTemplateSettings(shell, stage));
                await AssertTemplateEditingState(page, template, ["起動検証専用"], "起動検証専用", "起動検証専用", edited);
                Record("failed-edit-reopen-keeps-input-and-update-button");
                toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, [edited], edited); AssertTemplateSavedToast(toasts); Record("same-update-button-retry-persists-edit");
                await StartTemplateEdit(page, template, edited); await SetTemplateInput(page, template, "起動検証専用", true, 12);
                toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用"); Record("saved-edit-restored-through-update-button");
                observed.PhotoModalChecks.Add("actual-template-edit-failure-keeps-saved-card-selection-and-input-with-same-update-retry");
            }
            else if (kind == "template-delete-retry")
            {
                const string remaining = "残すテンプレート";
                await SetTemplateInput(page, template, remaining, true, 16); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用", remaining], "起動検証専用");
                await StartTemplateEdit(page, template, "起動検証専用"); await SetTemplateInput(page, template, "削除前の編集中", true, 14);
                var beforeDelete = ReadSharedBytes(settingPath); toasts.toasts.Clear();
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await DeleteTemplateThroughActualButton(page, "起動検証専用");
                    await WaitTemplateFailure(shell, toasts, "テンプレートを削除できませんでした。保存済みの内容と編集中の入力は変更されていません。もう一度削除してください。");
                    Assert.Equal(beforeDelete, ReadSharedBytes(settingPath));
                    await AssertTemplateEditingState(page, template, ["起動検証専用", remaining], "起動検証専用", "起動検証専用", "削除前の編集中");
                    Record("failed-delete-keeps-card-active-and-unsaved-editing-input");
                }
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenTemplateSettings(shell, stage));
                await AssertTemplateEditingState(page, template, ["起動検証専用", remaining], "起動検証専用", "起動検証専用", "削除前の編集中");
                Record("failed-delete-reopen-keeps-original-delete-target");
                toasts.toasts.Clear(); await DeleteTemplateThroughActualButton(page, "起動検証専用");
                await AssertTemplateStored(page, template, [remaining], remaining); AssertTemplateSavedToast(toasts); Record("same-delete-button-retry-keeps-remaining-card");
                await StartTemplateEdit(page, template, remaining); await SetTemplateInput(page, template, "起動検証専用", true, 12);
                toasts.toasts.Clear(); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用"); Record("remaining-template-restored-through-update-button");
                observed.PhotoModalChecks.Add("actual-template-delete-failure-keeps-card-active-and-editing-input-with-same-delete-retry");
            }
            else if (kind == "template-selection-retry")
            {
                const string candidate = "変更するテンプレート";
                await SetTemplateInput(page, template, candidate, true, 20); await SaveTemplateThroughActualButton(page);
                await AssertTemplateStored(page, template, ["起動検証専用", candidate], "起動検証専用");
                var beforeSelection = ReadSharedBytes(settingPath); toasts.toasts.Clear();
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await SelectTemplateThroughActualCardHandler(page, candidate);
                    await WaitTemplateFailure(shell, toasts, "使用するテンプレートを変更できませんでした。使用中のテンプレートは変更されていません。もう一度選択してください。");
                    Assert.Equal(beforeSelection, ReadSharedBytes(settingPath));
                    await AssertTemplateStored(page, template, ["起動検証専用", candidate], "起動検証専用");
                    Record("failed-selection-keeps-committed-active-card");
                }
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenTemplateSettings(shell, stage));
                await AssertTemplateStored(page, template, ["起動検証専用", candidate], "起動検証専用"); Record("failed-selection-reopen-keeps-active-card");
                toasts.toasts.Clear(); await SelectTemplateThroughActualCardHandler(page, candidate);
                await AssertTemplateStored(page, template, ["起動検証専用", candidate], candidate); AssertTemplateSavedToast(toasts); Record("same-card-selection-retry-persists-active");
                toasts.toasts.Clear(); await DeleteTemplateThroughActualButton(page, candidate);
                await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用"); Record("candidate-removed-through-delete-button");
                observed.PhotoModalChecks.Add("actual-template-card-handler-selection-failure-keeps-committed-active-and-retries-physical-pointer-unverified");
            }
            else
            {
                const string retry = "Retry {world-name}";
                await SetTemplateInput(page, template, retry, true, 18);
                using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await SaveTemplateThroughActualButton(page);
                    await WaitTemplateFailure(shell, toasts, "テンプレートを保存できませんでした。変更内容は画面に残っています。もう一度保存してください。");
                    Assert.Equal(initialBytes, ReadSharedBytes(settingPath));
                    await WaitPhotoUi(() => input.Text == retry && save.IsEnabled && !TemplateCardButtons(page, retry, "編集").Any(), "保存失敗後の入力保持・同じ保存ボタンでの再試行が成立しません。");
                    Assert.Equal(["起動検証専用"], template.tweetTemplates); Assert.Equal("起動検証専用", template.ActiveTweetTemplate);
                    Assert.Equal(retry, template.TweetTemplateDraft); Assert.Null(template.EditingTweetTemplate); Record("failed-save-keeps-json-draft-and-same-save-enabled");
                }
                await CloseTagSettings(page, stage); Assert.Same(page, await OpenTemplateSettings(shell, stage));
                Assert.Equal(initialBytes, ReadSharedBytes(settingPath)); Assert.Equal(["起動検証専用"], template.tweetTemplates);
                Assert.Equal(["起動検証専用"], TagElement<ItemsControl>(page, "TemplateList").Items.Cast<string>()); Assert.Equal(retry, input.Text); Assert.True(save.IsEnabled); Record("cache-reopen-keeps-draft-and-committed-list");
                toasts.toasts.Clear();
                await SaveTemplateThroughActualButton(page); await AssertTemplateStored(page, template, ["起動検証専用", retry], "起動検証専用");
                AssertTemplateSavedToast(toasts); Record("same-save-button-retry-persists-submitted-draft");
                toasts.toasts.Clear(); await DeleteTemplateThroughActualButton(page, retry);
                await AssertTemplateStored(page, template, ["起動検証専用"], "起動検証専用"); AssertTemplateSavedToast(toasts); Record("retry-template-removed-and-initial-values-restored");
                observed.PhotoModalChecks.Add("actual-template-save-failure-preserves-json-committed-list-and-draft-with-same-save-button-retry");
            }
            await CloseTagSettings(page, stage); Assert.Same(gallery, Assert.Single(Descendants(shell).OfType<GalleryPage>()));
            Record("closed-gallery-preserved"); observed.PhotoModalChecks.Add("actual-template-settings-close-unloads-with-gallery-and-protected-storage-preserved");
        }
        finally
        {
            Record("final", verify: false); AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant);
            var root = FixtureDirectory(Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N"));
            File.WriteAllText(Path.Combine(root, "template-preferences-observation.json"), JsonSerializer.Serialize(new
            {
                kind, actual_main = true, actual_xaml = true, invariant_globalization = invariant,
                build_configuration = typeof(NativeStartupTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                    .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration,
                snapshots, text_changed_through_real_textbox = true, physical_key_input = false, source_template_callbacks_modified_by_test = false,
                callback_completion_observed_by_forwarding = kind != "template-token-boundaries",
                pointer_event_handler_invoked_by_reflection = kind == "template-selection-retry", physical_pointer_input = false,
                real_user_run_registry_modified = false, browser_or_clipboard_used = false, verification_complete = false,
            }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
    }

    private static async Task<SettingsPage> OpenTemplateSettings(ShellPage shell, ShellStage stage)
    {
        var header = Assert.Single(Descendants(shell).OfType<ShellHeaderBar>()); await ClickPhotoButton(Assert.IsType<Button>(header.FindName("SettingsGearBtn")));
        await WaitPhotoUi(() => Descendants(shell).OfType<SettingsPage>().Any(item => item.IsLoaded), "実headerからSettingsを表示できません。");
        var page = Assert.Single(Descendants(shell).OfType<SettingsPage>()); var composite = Assert.IsType<SettingsCompositeViewModel>(page.DataContext);
        Assert.Same(App.Services!.GetRequiredService<ShellViewModel>().templatePageViewModel, composite.Template);
        await ClickPhotoButton(TagElement<Button>(page, "TemplateNavButton"));
        await WaitPhotoUi(() => TagElement<StackPanel>(page, "TemplateSection").Visibility == Visibility.Visible, "実navigationからTemplateを表示できません。");
        foreach (var name in new[] { "GeneralSection", "SimilarPhotosSection", "TagMasterSection", "CreditsSection" }) Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, name).Visibility);
        Assert.Equal(Visibility.Visible, stage.ModalVisibility); Assert.Equal(Visibility.Collapsed, stage.TopModalVisibility); return page;
    }

    private static async Task SetTemplateInput(SettingsPage page, TemplatePageViewModel template, string text, bool canSave, int weightedCount, string validation = "")
    {
        // 実TextBoxのproperty変更からBeforeTextChangingとTwoWay bindingを通す。物理key入力は行わない。
        var input = TagElement<TextBox>(page, "TemplateInputBox"); Assert.False(input.IsReadOnly); input.Text = text;
        await WaitPhotoUi(() => input.Text == text && template.TweetTemplateDraft == text && TagElement<Button>(page, "SaveButton").IsEnabled == canSave
            && TagElement<TextBlock>(page, "TemplateValidationText").Text == validation
            && TagElement<TextBlock>(page, "TemplateCharacterCountText").Text == $"X換算：{weightedCount}/280", "実template入力・加重文字数・保存guardが同期しません。");
        Assert.Equal(validation.Length == 0 ? Visibility.Collapsed : Visibility.Visible, TagElement<TextBlock>(page, "TemplateValidationText").Visibility);
    }

    private static IEnumerable<Button> TemplateCardButtons(SettingsPage page, string template, string content) =>
        Descendants(TagElement<ItemsControl>(page, "TemplateList")).OfType<Button>().Where(button => Equals(button.DataContext, template) && Equals(button.Content, content));

    private static async Task StartTemplateEdit(SettingsPage page, TemplatePageViewModel template, string value)
    {
        await ClickPhotoButton(Assert.Single(TemplateCardButtons(page, value, "編集")));
        await WaitPhotoUi(() => template.EditingTweetTemplate == value && TagElement<TextBox>(page, "TemplateInputBox").Text == value
            && Equals(TagElement<Button>(page, "SaveButton").Content, "更新"), "実編集buttonが入力欄と編集modeを更新しません。");
        Assert.Equal("テンプレートを編集", TagElement<TextBlock>(page, "EditorModeLabel").Text);
        Assert.Equal(Visibility.Visible, TagElement<Button>(page, "CancelEditButton").Visibility);
    }

    private static async Task SaveTemplateThroughActualButton(SettingsPage page)
    {
        var original = Assert.IsType<Func<Task>>(page.OnSaveTemplate); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.OnSaveTemplate = async () =>
        {
            try { await original(); done.TrySetResult(); }
            catch (Exception error) { done.TrySetException(error); throw; }
        };
        try { InvokePhotoButton(TagElement<Button>(page, "SaveButton")); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); await DrainGeneralSettingsQueue(page); }
        finally { page.OnSaveTemplate = original; }
    }

    private static async Task SelectTemplateThroughActualCardHandler(SettingsPage page, string value)
    {
        var card = Assert.Single(Descendants(TagElement<ItemsControl>(page, "TemplateList")).OfType<Border>(), border =>
            Equals(border.DataContext, value) && border.Child is Grid { Children.Count: > 0 } grid
            && grid.Children[0] is StackPanel { Children.Count: > 0 } labels && labels.Children[0] is TextBlock { Name: "TemplateLabel" });
        var original = Assert.IsType<Func<string, Task>>(page.OnSelectTemplate); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.OnSelectTemplate = async selected =>
        {
            Assert.Equal(value, selected);
            try { await original(selected); done.TrySetResult(); }
            catch (Exception error) { done.TrySetException(error); throw; }
        };
        try
        {
            // 実cardと本体のPointerPressed handler・callbackを使用する。物理pointerの到達性は別caseで確認する。
            var handler = typeof(SettingsPage).GetMethod("TemplateCard_PointerPressed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            handler.Invoke(page, [card, null]); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); await DrainGeneralSettingsQueue(page);
        }
        finally { page.OnSelectTemplate = original; }
    }

    private static async Task AssertTemplateEditingState(SettingsPage page, TemplatePageViewModel template, string[] expected, string active, string editing, string draft)
    {
        await WaitPhotoUi(() => TagElement<ItemsControl>(page, "TemplateList").Items.Cast<string>().SequenceEqual(expected)
            && TagElement<TextBox>(page, "TemplateInputBox").Text == draft && TagElement<Button>(page, "SaveButton").IsEnabled,
            "保存失敗後に保存済みcard・編集中の入力・再試行操作が保持されません。");
        Assert.Equal(expected, template.tweetTemplates); Assert.Equal(active, template.ActiveTweetTemplate);
        Assert.Equal(editing, template.EditingTweetTemplate); Assert.Equal(draft, template.TweetTemplateDraft);
        Assert.Equal("更新", TagElement<Button>(page, "SaveButton").Content); Assert.Equal(Visibility.Visible, TagElement<Button>(page, "CancelEditButton").Visibility);
        var saved = new AppConfig().LoadSetting(); Assert.Equal(expected, saved.TweetTemplates); Assert.Equal(active, saved.ActiveTweetTemplate);
        foreach (var value in expected) Assert.Single(TemplateCardButtons(page, value, "削除"));
        var label = Assert.Single(Descendants(TagElement<ItemsControl>(page, "TemplateList")).OfType<TextBlock>(), item => item.Name == "TemplateLabel" && Equals(item.DataContext, active));
        Assert.Equal("使用中", label.Text);
    }

    private static async Task WaitTemplateFailure(ShellPage shell, ToastService toasts, string expected)
    {
        await WaitPhotoFailure(toasts, expected);
        var host = Assert.Single(Descendants(shell).OfType<ToastHost>()); Assert.True(host.IsLoaded); Assert.Equal(Visibility.Visible, host.Visibility);
        Assert.Same(toasts.toasts, Assert.Single(Descendants(host).OfType<ItemsControl>()).ItemsSource);
        await WaitPhotoUi(() => Descendants(host).OfType<TextBlock>().Any(item => item.Text == expected), "template操作の失敗通知が実ToastHostに表示されません。");
        var message = Assert.Single(Descendants(host).OfType<TextBlock>(), item => item.Text == expected);
        await WaitPhotoUi(() => message.ActualWidth > 0 && message.ActualHeight > 0, "通知本文のlayoutが確定しません。");
        DependencyObject? parent = VisualTreeHelper.GetParent(message);
        while (parent is not null && parent is not Border) parent = VisualTreeHelper.GetParent(parent);
        var card = Assert.IsType<Border>(parent);
        var bounds = message.TransformToVisual(card).TransformBounds(new Windows.Foundation.Rect(0, 0, message.ActualWidth, message.ActualHeight));
        // Textが存在しても、横方向の無制限measureで復旧案内がcard外へ切れる場合は不合格にする。
        Assert.True(bounds.X >= 0 && bounds.Right <= card.ActualWidth + 0.5,
            $"通知本文が右端で切れています: textRight={bounds.Right}, cardWidth={card.ActualWidth}");
        Assert.True(bounds.Y >= 0 && bounds.Bottom <= card.ActualHeight + 0.5,
            $"通知本文が上下で切れています: textBottom={bounds.Bottom}, cardHeight={card.ActualHeight}");
    }

    private static async Task DeleteTemplateThroughActualButton(SettingsPage page, string value)
    {
        var original = Assert.IsType<Func<string, Task>>(page.OnDeleteTemplate); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var requests = new List<string>();
        page.OnDeleteTemplate = async template =>
        {
            requests.Add(template);
            try { await original(template); done.TrySetResult(); }
            catch (Exception error) { done.TrySetException(error); throw; }
        };
        try { InvokePhotoButton(Assert.Single(TemplateCardButtons(page, value, "削除"))); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); await DrainGeneralSettingsQueue(page); Assert.Equal(value, Assert.Single(requests)); }
        finally { page.OnDeleteTemplate = original; }
    }

    private static async Task AssertTemplateStored(SettingsPage page, TemplatePageViewModel template, string[] expected, string active)
    {
        await WaitPhotoUi(() => TagElement<ItemsControl>(page, "TemplateList").Items.Cast<string>().SequenceEqual(expected)
            && TagElement<TextBox>(page, "TemplateInputBox").Text.Length == 0 && !TagElement<Button>(page, "SaveButton").IsEnabled, "template一覧・入力解除・保存buttonが同期しません。");
        Assert.Equal(expected, template.tweetTemplates); Assert.Equal(active, template.ActiveTweetTemplate); Assert.Null(template.EditingTweetTemplate); Assert.Equal("", template.TweetTemplateDraft);
        var saved = new AppConfig().LoadSetting(); Assert.Equal(expected, saved.TweetTemplates); Assert.Equal(active, saved.ActiveTweetTemplate);
        foreach (var value in expected)
        {
            await WaitPhotoUi(() => TemplateCardButtons(page, value, "編集").Count() == 1 && TemplateCardButtons(page, value, "削除").Count() == 1, "保存済みtemplateの実cardとbuttonが表示されません。");
            var card = Assert.Single(Descendants(TagElement<ItemsControl>(page, "TemplateList")).OfType<Border>(), border =>
                Equals(border.DataContext, value) && border.Child is Grid { Children.Count: > 0 } grid
                && grid.Children[0] is StackPanel { Children.Count: > 0 } labels && labels.Children[0] is TextBlock { Name: "TemplateLabel" });
            Assert.Equal(value == active ? "使用中" : "テンプレート", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(Assert.IsType<Grid>(card.Child).Children[0]).Children[0]).Text);
        }
    }

    private static void AssertTemplateSavedToast(ToastService toasts)
    {
        var toast = Assert.Single(toasts.toasts); Assert.Equal("テンプレートを保存しました。", toast.Msg); Assert.Equal(ToastType.info, toast.Type);
    }

    private static string TemplateOtherSettingFields() => JsonSerializer.Serialize(new SortedDictionary<string, JsonElement>(
        JsonSerializer.SerializeToElement(new AppConfig().LoadSetting()).EnumerateObject().Where(item => item.Name is not "tweetTemplates" and not "activeTweetTemplate")
            .ToDictionary(item => item.Name, item => item.Value)));
}
