using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Shell;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Tests;

public sealed partial class NativeStartupTests
{
    [Fact]
    public Task NativeStartup_PhotoModalActualBindingsNavigationAndUnloading() => RunIsolatedAsync("photo-bindings", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalMutationFailureAndSameUiRetry() => RunIsolatedAsync("photo-mutations", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalFailureToastBindingAndMessage() => RunIsolatedAsync("photo-toast", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalTagPickerCandidatesSelectionAndCancelPreserveStorage() => RunIsolatedAsync("photo-tags-picker", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalImmediateReopenKeepsNewPhotoAndNavigationLight() => RunIsolatedAsync("photo-immediate-reopen-light", "light", false);

    [Fact]
    public Task NativeStartup_PhotoModalImmediateReopenKeepsNewPhotoAndNavigationDark() => RunIsolatedAsync("photo-immediate-reopen-dark", "dark", false);

    private static bool IsPhotoModalScenario(string kind) => kind is "photo-bindings" or "photo-mutations" or "photo-toast"
        or "photo-tags-input" or "photo-tags-create-retry" or "photo-tags-delete-retry"
        or "photo-tags-delete-filter-retry" or "photo-tags-empty-reuse" or "photo-tags-picker"
        or "photo-immediate-reopen-light" or "photo-immediate-reopen-dark";

    // 起動fixtureが所有するDataだけへ現行schemaと画像を作り、実ShellのDI・XBF・callbackを通す。
    private static async Task SeedPhotoModalDatabase(string install, string dbPath, bool tagBothPhotos)
    {
        var db = new AlpheratzDb(dbPath); db.Initialize();
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        foreach (var (name, slot) in new[] { ("写真①.png", 1L), ("写真②.png", 2L) })
        {
            var path = Path.Combine(install, name); await File.WriteAllBytesAsync(path, image);
            await db.UpsertPhotoAsync(new PhotoUpsertData { PhotoPath = AppPaths.NormalizePathForDb(path), PhotoFilename = name, SourceSlot = slot,
                Timestamp = slot == 1 ? "2026-10-09 00:01:00" : "2026-10-09 00:02:00", ImageWidth = 1, ImageHeight = 1, Orientation = "square",
                WorldId = slot == 1 ? "wrld_fixture" : null, WorldName = slot == 1 ? "保持ワールド" : null, MatchSource = slot == 1 ? "manual" : null });
        }
        await db.AddPhotoTagAsync(AppPaths.NormalizePathForDb(Path.Combine(install, "写真①.png")), "保持");
        await db.CreateTagMasterAsync("追加候補");
        if (tagBothPhotos)
        {
            var second = AppPaths.NormalizePathForDb(Path.Combine(install, "写真②.png"));
            await db.AddPhotoTagAsync(second, "保持"); await db.AddPhotoTagAsync(second, "追加候補");
        }
        PhotoSql(dbPath, """
            INSERT INTO photo_user_data_backup (photo_filename,is_favorite,tags) VALUES ('保持backup.png',1,'["保持"]');
            INSERT INTO archive_world_visits (source_log_name,world_name,join_time,leave_time)
                VALUES ('fixture.log','保持する履歴','2026-10-01 10:00:00','2026-10-01 11:00:00');
            """);
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); SqliteConnection.ClearPool(pool);
    }

    private static async Task VerifyActualPhotoModal(ShellPage shell, ShellViewModel viewModel, StartupObservation observed, string kind)
    {
        Assert.True(UiThread.Queue!.HasThreadAccess);
        var photos = viewModel.galleryViewModel.photosState.photos.ToArray(); Assert.Equal(2, photos.Length);
        var first = Assert.Single(photos, photo => photo.PhotoFilename == "写真①.png");
        var second = Assert.Single(photos, photo => photo.PhotoFilename == "写真②.png");
        Assert.Equal(1L, first.SourceSlot); Assert.Equal(2L, second.SourceSlot);
        var dbPath = Path.Combine(AppPaths.InstallLocation!, "Data", "db", "Alpheratz.db");
        var sourceBytes = new[] { first, second }.ToDictionary(photo => photo.PhotoPath, photo => ReadSharedBytes(photo.PhotoPath.Replace('/', Path.DirectorySeparatorChar)));
        observed.Step = kind + ": opening-photo-modal";
        var modal = Assert.IsType<PhotoModalViewModel>(viewModel.createPhotoModalViewModelFromList(first, [first, second]));
        shell.ShowPhotoModal(modal);
        await WaitPhotoUi(() => Descendants(shell).OfType<PhotoModalPage>().Any(page => page.IsLoaded), "写真詳細のLoadedが完了しません。");
        var page = Assert.Single(Descendants(shell).OfType<PhotoModalPage>()); Assert.Same(modal, page.DataContext);
        Assert.Equal(shell.ActualTheme, page.ActualTheme);
        await WaitPhotoUi(() => PhotoElement<Image>(page, "ModalImage").Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 }, "写真詳細の実画像decodeが完了しません。");
        Assert.Equal(new Uri(first.PhotoPath, UriKind.Absolute), Assert.IsType<BitmapImage>(PhotoElement<Image>(page, "ModalImage").Source).UriSource);
        Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == "写真①.png");
        Assert.Equal("保持ワールド", PhotoElement<TextBlock>(page, "WorldNameText").Text);
        Assert.True(PhotoElement<Button>(page, "WorldActionButton").IsEnabled);
        await WaitPhotoUi(() => AssignedTagButtons(page).Any(button => Equals(button.Tag, "保持")), "写真詳細のタグbindingが表示へ反映されません。");
        observed.PhotoModalChecks.Add("actual-xbf-data-context-theme-bitmap-and-tag-binding"); observed.SavedPhotoCount = 2;
        observed.Step = kind + ": loaded-and-decoded-photo-modal";

        if (kind == "photo-bindings") await VerifyPhotoBindingsAndUnloading(shell, viewModel, modal, page, first, second, dbPath, observed);
        else if (kind == "photo-mutations") await VerifyPhotoMutationFailures(page, first, dbPath, observed);
        else if (kind == "photo-toast") await VerifyPhotoFailureToast(shell, page, first, dbPath, observed);
        else if (kind == "photo-tags-picker") await VerifyPhotoTagPicker(modal, page, first, dbPath, observed);
        else if (kind is "photo-immediate-reopen-light" or "photo-immediate-reopen-dark") await VerifyPhotoImmediateReopen(shell, viewModel, page, first, second, dbPath, observed);
        else await VerifySettingsTagsFromPhoto(shell, viewModel, modal, page, first, second, dbPath, observed, kind);
        foreach (var (path, bytes) in sourceBytes) Assert.Equal(bytes, ReadSharedBytes(path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>閉じる操作直後に別写真を開き、旧Pageの遅いUnloaded後も新しい表示と操作対象を保つ。</summary>
    private static async Task VerifyPhotoImmediateReopen(ShellPage shell, ShellViewModel viewModel, PhotoModalPage page,
        PhotoThumbnailItem first, PhotoThumbnailItem second, string dbPath, StartupObservation observed)
    {
        var before = DatabaseSnapshot(dbPath);
        var oldUnloaded = 0;
        RoutedEventHandler onOldUnloaded = (_, _) => oldUnloaded++;
        page.Unloaded += onOldUnloaded;
        try
        {
            InvokePhotoButton(PhotoElement<Button>(page, "CloseButtonOverlay"));
            // 同じUI処理内で再表示し、実Unloaded完了を待つ既存caseとは異なる順序を成立させる。
            var reopenedModal = Assert.IsType<PhotoModalViewModel>(viewModel.createPhotoModalViewModelFromList(second, [first, second]));
            shell.ShowPhotoModal(reopenedModal);
            Assert.Equal(0, oldUnloaded);
            await WaitPhotoUi(() => oldUnloaded == 1 && Descendants(shell).OfType<PhotoModalPage>().Any(next => next.IsLoaded), "写真詳細を直後に開き直せません。");
            var reopened = Assert.Single(Descendants(shell).OfType<PhotoModalPage>());
            Assert.NotSame(page, reopened);
            Assert.Equal(shell.ActualTheme, reopened.ActualTheme);
            await WaitPhotoUi(() => PhotoElement<Image>(reopened, "ModalImage").Source is BitmapImage image
                && image.UriSource == new Uri(second.PhotoPath, UriKind.Absolute) && image.PixelWidth > 0 && image.PixelHeight > 0,
                "開き直した写真の画像とdecodeが表示へ反映されません。");
            Assert.Same(second, reopenedModal.state.SelectedPhoto);
            Assert.Contains(Descendants(reopened).OfType<TextBlock>(), text => text.Text == "写真②.png");
            Assert.Equal("ワールド不明", PhotoElement<TextBlock>(reopened, "WorldNameText").Text);
            Assert.False(PhotoElement<Button>(reopened, "WorldActionButton").IsEnabled);
            Assert.Empty(AssignedTagButtons(reopened));
            await ClickPhotoButton(PhotoElement<Button>(reopened, "PrevPhotoButton"));
            await WaitPhotoUi(() => ReferenceEquals(reopenedModal.state.SelectedPhoto, first)
                && PhotoElement<TextBlock>(reopened, "WorldNameText").Text == "保持ワールド"
                && AssignedTagButtons(reopened).Any(button => Equals(button.Tag, "保持")), "再表示後の前の写真とタグへ移動できません。");
            await ClickPhotoButton(PhotoElement<Button>(reopened, "NextPhotoButton"));
            await WaitPhotoUi(() => ReferenceEquals(reopenedModal.state.SelectedPhoto, second)
                && PhotoElement<TextBlock>(reopened, "WorldNameText").Text == "ワールド不明"
                && !AssignedTagButtons(reopened).Any(), "再表示後の次の写真に前の情報が残っています。");
            InvokePhotoButton(PhotoElement<Button>(reopened, "CloseButtonOverlay"));
            await WaitPhotoUi(() => !Descendants(shell).OfType<PhotoModalPage>().Any(), "開き直した写真詳細を閉じられません。");
            Assert.Equal(before, DatabaseSnapshot(dbPath));
            observed.PhotoModalChecks.Add("actual-immediate-reopen-before-old-unloaded-preserves-new-image-fields-tags-and-navigation");
        }
        finally { page.Unloaded -= onOldUnloaded; }
    }

    /// <summary>実buttonから候補表示・選択・取消しを通し、失敗時も保存内容とruntime条件を記録する。</summary>
    private static async Task VerifyPhotoTagPicker(PhotoModalViewModel modal, PhotoModalPage page, PhotoThumbnailItem first, string dbPath, StartupObservation observed)
    {
        var before = DatabaseSnapshot(dbPath); var originalTags = first.Tags.ToArray(); var sourcePath = first.PhotoPath.Replace('/', Path.DirectorySeparatorChar);
        var originalImage = ReadSharedBytes(sourcePath); var failures = new List<object>(); var snapshots = new List<object>();
        var uiThreadId = Environment.CurrentManagedThreadId;
        var opener = PhotoElement<Button>(page, "TagAddRow"); var confirm = PhotoElement<Button>(page, "TagAddConfirmButton");
        Assert.Equal(Visibility.Visible, opener.Visibility); Assert.True(opener.IsEnabled); Assert.False(page.HasBlockingInnerOverlayOpen);
        EventHandler<FirstChanceExceptionEventArgs> observeCulture = (_, args) =>
        {
            if (Environment.CurrentManagedThreadId == uiThreadId && args.Exception is CultureNotFoundException error && error.InvalidCultureName == "ja-JP")
                failures.Add(new { type = error.GetType().FullName, invalid_culture = error.InvalidCultureName, message = error.Message, stack = error.StackTrace });
        };
        void Record(string stage)
        {
            snapshots.Add(new { stage, overlay_open = page.HasBlockingInnerOverlayOpen, confirm_enabled = confirm.IsEnabled,
                candidates = PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray(),
                database_rows_preserved = before == DatabaseSnapshot(dbPath), selected_photo_preserved = ReferenceEquals(first, modal.state.SelectedPhoto),
                assigned_tags_preserved = originalTags.SequenceEqual(first.Tags), image_bytes_preserved = originalImage.SequenceEqual(ReadSharedBytes(sourcePath)) });
        }
        AppDomain.CurrentDomain.FirstChanceException += observeCulture;
        try
        {
            observed.Step = "photo-tags-picker: opening-candidates";
            await ClickPhotoButton(opener); Record("opened");
            Assert.True(page.HasBlockingInnerOverlayOpen, "写真詳細の実buttonからタグ追加画面を開けません。"); Assert.Empty(failures);
            var choice = Assert.Single(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>());
            Assert.Equal("タグ「追加候補」を選択", AutomationProperties.GetName(choice)); Assert.False(confirm.IsEnabled);
            Assert.DoesNotContain(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>(), button => AutomationProperties.GetName(button).Contains("保持", StringComparison.Ordinal));
            await ClickPhotoButton(choice); Record("selected"); Assert.True(confirm.IsEnabled);
            var selected = Assert.Single(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>());
            Assert.Equal("タグ「追加候補」の選択を解除", AutomationProperties.GetName(selected));
            await ClickPhotoButton(selected); Record("deselected"); Assert.False(confirm.IsEnabled);
            await ClickPhotoButton(Assert.Single(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>()));
            await ClickPhotoButton(Assert.Single(Descendants(PhotoElement<Grid>(page, "TagAddOverlay")).OfType<Button>(), button => Equals(button.Content, "キャンセル")));
            Record("cancelled"); Assert.False(page.HasBlockingInnerOverlayOpen); Assert.Empty(PhotoElement<StackPanel>(page, "ExistingTagList").Children); Assert.False(confirm.IsEnabled);
            await ClickPhotoButton(opener); Record("reopened"); Assert.True(page.HasBlockingInnerOverlayOpen); Assert.False(confirm.IsEnabled);
            Assert.Equal("タグ「追加候補」を選択", AutomationProperties.GetName(Assert.Single(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>())));
            await ClickPhotoButton(Assert.Single(Descendants(PhotoElement<Grid>(page, "TagAddOverlay")).OfType<Button>(), button => Equals(button.Content, "キャンセル")));
            Assert.False(page.HasBlockingInnerOverlayOpen); Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Equal(originalTags, first.Tags); Assert.Equal(originalImage, ReadSharedBytes(sourcePath));
            observed.PhotoModalChecks.Add("actual-tag-picker-excludes-assigned-tag-toggles-selection-and-cancels-with-storage-and-reopen-state-preserved");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= observeCulture;
            Record("final"); AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant);
            var root = FixtureDirectory(Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N"));
            File.WriteAllText(Path.Combine(root, "tag-picker-observation.json"), JsonSerializer.Serialize(new
            {
                actual_main = true, actual_xaml = page.IsLoaded, build_configuration = typeof(NativeStartupTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                    .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration,
                invariant_globalization = invariant, core_library = typeof(object).Assembly.Location, assembly = typeof(App).Assembly.Location,
                assembly_sha256 = HashFile(typeof(App).Assembly.Location), snapshots, culture_failures = failures,
                source_globalization_modified_by_test = false, source_tag_picker_modified_by_test = false, verification_complete = false,
            }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
    }

    private static async Task VerifyPhotoBindingsAndUnloading(ShellPage shell, ShellViewModel viewModel, PhotoModalViewModel modal,
        PhotoModalPage page, PhotoThumbnailItem first, PhotoThumbnailItem second, string dbPath, StartupObservation observed)
    {
        var before = DatabaseSnapshot(dbPath);
        // 端buttonは常時有効で、範囲外移動はstateが拒否する現行契約を実際のClickで確認する。
        Assert.False(modal.state.CanGoPrev); Assert.True(modal.state.CanGoNext);
        await ClickPhotoButton(PhotoElement<Button>(page, "PrevPhotoButton")); Assert.Same(first, modal.state.SelectedPhoto);
        await ClickPhotoButton(PhotoElement<Button>(page, "NextPhotoButton"));
        await WaitPhotoUi(() => ReferenceEquals(modal.state.SelectedPhoto, second) && PhotoElement<TextBlock>(page, "WorldNameText").Text == "ワールド不明", "次の写真へ移動しません。");
        await WaitPhotoUi(() => PhotoElement<Image>(page, "ModalImage").Source is BitmapImage image && image.UriSource == new Uri(second.PhotoPath, UriKind.Absolute)
            && image.PixelWidth > 0 && image.PixelHeight > 0, "次の写真の実画像sourceとdecodeが反映されません。");
        Assert.True(modal.state.CanGoPrev); Assert.False(modal.state.CanGoNext);
        await ClickPhotoButton(PhotoElement<Button>(page, "NextPhotoButton")); Assert.Same(second, modal.state.SelectedPhoto);
        var worldButton = PhotoElement<Button>(page, "WorldActionButton"); Assert.False(worldButton.IsEnabled);
        Assert.Equal("リンクなし", PhotoElement<TextBlock>(page, "WorldActionLabel").Text);
        Assert.Equal("ワールドページの情報がないため開けません", AutomationProperties.GetName(worldButton));
        Assert.Equal(Visibility.Visible, PhotoElement<TextBlock>(page, "AssignedTagEmptyNote").Visibility);
        await WaitPhotoUi(() => !AssignedTagButtons(page).Any(), "未付与の写真に前のタグが残っています。");
        Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == "写真②.png");
        observed.PhotoModalChecks.Add("actual-next-button-updates-photo-world-tags-and-navigation-boundaries");

        // 既存DTOへの通知がPageのqueue同期を通ることを確認する。ワールド解決の保存操作とは別の検証である。
        second.WorldName = "再解決したワールド"; second.WorldId = "wrld_secondary";
        await WaitPhotoUi(() => PhotoElement<TextBlock>(page, "WorldNameText").Text == "再解決したワールド" && worldButton.IsEnabled, "選択写真のfield変更が反映されません。");
        Assert.Equal("VRChatのワールドページを開く", AutomationProperties.GetName(worldButton));
        observed.PhotoModalChecks.Add("selected-photo-notifications-update-actual-world-controls");

        await ClickPhotoButton(PhotoElement<Button>(page, "PrevPhotoButton"));
        await WaitPhotoUi(() => ReferenceEquals(modal.state.SelectedPhoto, first), "前の写真へ移動しません。");
        await WaitPhotoUi(() => AssignedTagButtons(page).Any(button => Equals(button.Tag, "保持")), "前の写真のタグを再表示しません。");
        Assert.False(modal.state.CanGoPrev);
        InvokePhotoButton(PhotoElement<Button>(page, "TagAddRow")); await WaitPhotoUi(() => page.HasBlockingInnerOverlayOpen, "タグ追加画面を開きません。");
        InvokePhotoButton(Assert.Single(Descendants(PhotoElement<Grid>(page, "TagAddOverlay")).OfType<Button>(), button => Equals(button.Content, "キャンセル")));
        await WaitPhotoUi(() => !page.HasBlockingInnerOverlayOpen, "タグ追加を取り消せません。");
        Assert.False(PhotoElement<Button>(page, "TagAddConfirmButton").IsEnabled);
        Assert.Equal(before, DatabaseSnapshot(dbPath));
        observed.PhotoModalChecks.Add("actual-previous-button-and-tag-cancel-keep-durable-storage");

        var snapshots = new List<object>(); var timeline = Stopwatch.StartNew(); var unloadedEvents = 0; var loadedAgainEvents = 0; var reopenedUnloadedEvents = 0;
        PhotoModalPage? reopened = null; PhotoModalViewModel? reopenedModal = null; string? detachedWorldText = null; Exception? failure = null;
        void Record(string stage)
        {
            snapshots.Add(new { stage, elapsed_ms = timeline.ElapsedMilliseconds, old_loaded = page.IsLoaded, new_loaded = reopened?.IsLoaded,
                unloaded_events = unloadedEvents, loaded_again_events = loadedAgainEvents, new_unloaded_events = reopenedUnloadedEvents, state_shared = ReferenceEquals(modal.state, reopenedModal?.state),
                selected_filename = modal.state.SelectedPhoto?.PhotoFilename, selected_world = modal.state.SelectedPhoto?.WorldName,
                old_world = PhotoElement<TextBlock>(page, "WorldNameText").Text, new_world = reopened is null ? null : PhotoElement<TextBlock>(reopened, "WorldNameText").Text,
                old_image_type = PhotoElement<Image>(page, "ModalImage").Source?.GetType().FullName,
                old_image_uri = (PhotoElement<Image>(page, "ModalImage").Source as BitmapImage)?.UriSource?.ToString(),
                old_state_subscribers = PhotoSubscriberMethods(modal.state, page), old_first_photo_subscribers = PhotoSubscriberMethods(first, page), old_second_photo_subscribers = PhotoSubscriberMethods(second, page) });
        }
        // IsLoadedはUnloaded通知より先に変わるため、実eventで購読解除が終わってから再表示する。
        RoutedEventHandler unloaded = (_, _) => { unloadedEvents++; Record("old-unloaded-event"); };
        RoutedEventHandler loadedAgain = (_, _) => { loadedAgainEvents++; Record("old-loaded-again-event"); };
        RoutedEventHandler reopenedUnloaded = (_, _) => { reopenedUnloadedEvents++; Record("new-unloaded-event"); };
        page.Unloaded += unloaded; page.Loaded += loadedAgain;
        try
        {
            Record("before-close"); InvokePhotoButton(PhotoElement<Button>(page, "CloseButtonOverlay"));
            await WaitPhotoUi(() => unloadedEvents == 1 && !page.IsLoaded && !Descendants(shell).OfType<PhotoModalPage>().Any(), "写真詳細のUnloadedが完了しません。");
            Record("after-actual-unloaded");
            Assert.Null(modal.state.SelectedPhoto); Assert.Empty(modal.state.photoHistory); Assert.Null(PhotoElement<Image>(page, "ModalImage").Source);
            detachedWorldText = PhotoElement<TextBlock>(page, "WorldNameText").Text;
            reopenedModal = Assert.IsType<PhotoModalViewModel>(viewModel.createPhotoModalViewModelFromList(second, [first, second])); Record("after-new-vm-before-show");
            Assert.Same(modal.state, reopenedModal.state);
            shell.ShowPhotoModal(reopenedModal);
            await WaitPhotoUi(() => Descendants(shell).OfType<PhotoModalPage>().Any(next => next.IsLoaded), "写真詳細を開き直せません。");
            reopened = Assert.Single(Descendants(shell).OfType<PhotoModalPage>()); Assert.NotSame(page, reopened); Record("after-new-page-loaded");
            reopened.Unloaded += reopenedUnloaded;
            second.WorldName = "再表示後のワールド"; Record("after-new-field-before-wait");
            await WaitPhotoUi(() => PhotoElement<TextBlock>(reopened, "WorldNameText").Text == "再表示後のワールド", "再表示したPageの購読が有効になりません。");
            Record("after-new-field-applied");
            Assert.Equal(1, unloadedEvents); Assert.Equal(0, loadedAgainEvents);
            Assert.Equal(detachedWorldText, PhotoElement<TextBlock>(page, "WorldNameText").Text); Assert.Null(PhotoElement<Image>(page, "ModalImage").Source);
            Assert.Equal(before, DatabaseSnapshot(dbPath));
            InvokePhotoButton(PhotoElement<Button>(reopened, "CloseButtonOverlay")); await WaitPhotoUi(() => reopenedUnloadedEvents == 1 && !reopened.IsLoaded, "再表示したPageを解除できません。");
            observed.PhotoModalChecks.Add("actual-close-unloads-releases-image-and-detaches-old-page-before-reopen"); Record("after-new-page-close");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            page.Unloaded -= unloaded; page.Loaded -= loadedAgain;
            if (reopened is not null) reopened.Unloaded -= reopenedUnloaded;
            var root = FixtureDirectory(Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N"));
            File.WriteAllText(Path.Combine(root, "photo-unload-observation.json"), JsonSerializer.Serialize(new { actual_main = true, actual_xaml = true,
                original_expected_world_text = detachedWorldText, snapshots, failure = failure?.ToString(), database_rows_preserved = before == DatabaseSnapshot(dbPath),
                source_state_or_callback_modified_by_test = false, verification_complete = false }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }
    }

    /// <summary>通知の内部fieldが読める場合だけ、旧Pageへ残る購読を診断用に採取する。購読は変更しない。</summary>
    private static string[]? PhotoSubscriberMethods(object source, PhotoModalPage target)
    {
        for (Type? type = source.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (field is null) continue;
            return field.GetValue(source) is Delegate callbacks ? callbacks.GetInvocationList().Where(callback => ReferenceEquals(callback.Target, target)).Select(callback => callback.Method.Name).ToArray() : [];
        }
        return null;
    }

    private static async Task VerifyPhotoMutationFailures(PhotoModalPage page, PhotoThumbnailItem first, string dbPath, StartupObservation observed)
    {
        var toasts = App.Services!.GetRequiredService<ToastService>(); var before = DatabaseSnapshot(dbPath); var unrelated = PhotoUnrelatedSnapshot(dbPath, first.PhotoPath);
        observed.Step = "photo-mutations: favorite-fault";
        var favorite = Assert.Single(Descendants(page).OfType<Button>(), button => AutomationProperties.GetName(button) == "お気に入りを切り替え");
        var quoted = first.PhotoPath.Replace("'", "''");
        PhotoSql(dbPath, $"CREATE TRIGGER owned_photo_fault BEFORE UPDATE OF is_favorite ON photos WHEN NEW.photo_path = '{quoted}' BEGIN SELECT RAISE(ABORT, 'owned photo favorite fault'); END");
        try
        {
            toasts.toasts.Clear(); InvokePhotoButton(favorite);
            await WaitPhotoFailure(toasts, "お気に入りを更新できませんでした。お気に入りをもう一度変更してください。");
            Assert.False(first.IsFavorite); Assert.Equal(before, DatabaseSnapshot(dbPath)); AssertFavoriteBinding(favorite, false);
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_photo_fault"); }
        observed.Step = "photo-mutations: favorite-retry";
        toasts.toasts.Clear(); InvokePhotoButton(favorite); await WaitPhotoUi(() => first.IsFavorite, "同じお気に入りbuttonからの再試行が完了しません。");
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT is_favorite FROM photos WHERE photo_path = $path", first.PhotoPath)); AssertFavoriteBinding(favorite, true);
        Assert.Equal(2L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(unrelated, PhotoUnrelatedSnapshot(dbPath, first.PhotoPath)); observed.PhotoModalChecks.Add("actual-favorite-button-sql-failure-rollback-and-same-button-retry");

        before = DatabaseSnapshot(dbPath);
        observed.Step = "photo-mutations: tag-insert-fault";
        PhotoSql(dbPath, $"CREATE TRIGGER owned_photo_fault BEFORE INSERT ON photo_tags WHEN NEW.photo_path = '{quoted}' BEGIN SELECT RAISE(ABORT, 'owned photo tag insert fault'); END");
        try
        {
            toasts.toasts.Clear(); await ConfirmPhotoTag(page, "追加候補");
            await WaitPhotoFailure(toasts, "写真にタグを追加できませんでした。タグをもう一度追加してください。");
            Assert.Equal(["保持"], first.Tags); Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.False(page.HasBlockingInnerOverlayOpen);
            Assert.Equal(["保持"], AssignedTagButtons(page).Select(button => (string)button.Tag).ToArray());
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_photo_fault"); }
        observed.Step = "photo-mutations: tag-insert-retry";
        toasts.toasts.Clear(); await ConfirmPhotoTag(page, "追加候補");
        await WaitPhotoUi(() => AssignedTagButtons(page).Any(button => Equals(button.Tag, "追加候補")), "同じPageからのタグ再試行が表示へ反映されません。");
        // 現行UIはja-JPの読み順で追加候補→保持に並べる。DBの挿入順とは区別する。
        Assert.Equal(["追加候補", "保持"], first.Tags); Assert.Equal(2L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", first.PhotoPath));
        // 既存masterへのINSERT ... ON CONFLICT DO NOTHINGもAUTOINCREMENTを消費する。業務行の保持とは別に採番を照合する。
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(unrelated, PhotoUnrelatedSnapshot(dbPath, first.PhotoPath)); observed.PhotoModalChecks.Add("actual-tag-selection-and-confirmation-sql-rollback-and-same-page-retry");

        before = DatabaseSnapshot(dbPath); var remove = Assert.Single(AssignedTagButtons(page), button => Equals(button.Tag, "追加候補"));
        observed.Step = "photo-mutations: tag-delete-fault";
        PhotoSql(dbPath, $"CREATE TRIGGER owned_photo_fault BEFORE DELETE ON photo_tags WHEN OLD.photo_path = '{quoted}' BEGIN SELECT RAISE(ABORT, 'owned photo tag delete fault'); END");
        try
        {
            toasts.toasts.Clear(); InvokePhotoButton(remove);
            await WaitPhotoFailure(toasts, "写真からタグを削除できませんでした。タグをもう一度削除してください。");
            Assert.Equal(["追加候補", "保持"], first.Tags); Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Contains(remove, AssignedTagButtons(page));
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_photo_fault"); }
        observed.Step = "photo-mutations: tag-delete-retry";
        toasts.toasts.Clear(); InvokePhotoButton(remove); await WaitPhotoUi(() => !AssignedTagButtons(page).Any(button => Equals(button.Tag, "追加候補")), "同じタグbuttonからの削除再試行が完了しません。");
        Assert.Equal(["保持"], first.Tags); Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", first.PhotoPath));
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.True(first.IsFavorite); Assert.Equal(unrelated, PhotoUnrelatedSnapshot(dbPath, first.PhotoPath)); Assert.DoesNotContain(toasts.toasts, toast => toast.Type == ToastType.error);
        InvokePhotoButton(PhotoElement<Button>(page, "CloseButtonOverlay")); await WaitPhotoUi(() => !page.IsLoaded, "保存操作後の写真詳細を解除できません。");
        observed.PhotoModalChecks.Add("actual-assigned-tag-button-sql-rollback-and-same-button-delete-retry");
    }

    // 保存のrollback・再試行とは独立して、同じ本体Shellから実ToastHostのbindingと表示を確認する。
    private static async Task VerifyPhotoFailureToast(ShellPage shell, PhotoModalPage page, PhotoThumbnailItem first, string dbPath, StartupObservation observed)
    {
        var toasts = App.Services!.GetRequiredService<ToastService>(); var before = DatabaseSnapshot(dbPath);
        var expected = "お気に入りを更新できませんでした。お気に入りをもう一度変更してください。";
        var favorite = Assert.Single(Descendants(page).OfType<Button>(), button => AutomationProperties.GetName(button) == "お気に入りを切り替え");
        var host = Assert.Single(Descendants(shell).OfType<ToastHost>()); Assert.True(host.IsLoaded); Assert.Equal(Visibility.Visible, host.Visibility);
        observed.Step = "photo-toast: favorite-fault-and-toast-binding";
        PhotoSql(dbPath, $"CREATE TRIGGER owned_photo_fault BEFORE UPDATE OF is_favorite ON photos WHEN NEW.photo_path = '{first.PhotoPath.Replace("'", "''")}' BEGIN SELECT RAISE(ABORT, 'owned photo toast fault'); END");
        try
        {
            toasts.toasts.Clear(); InvokePhotoButton(favorite); await WaitPhotoFailure(toasts, expected);
            Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.False(first.IsFavorite); AssertFavoriteBinding(favorite, false);
            var list = Assert.Single(Descendants(host).OfType<ItemsControl>());
            var root = FixtureDirectory(Guid.ParseExact(Environment.GetEnvironmentVariable(FixtureIdVariable)!, "N"));
            File.WriteAllText(Path.Combine(root, "toast-binding-observation.json"), JsonSerializer.Serialize(new
            {
                actual_main = true, actual_xaml = true, actual_photo_modal_loaded = page.IsLoaded, toast_host_loaded = host.IsLoaded,
                toast_host_data_context_type = host.DataContext?.GetType().FullName, items_source_type = list.ItemsSource?.GetType().FullName,
                items_source_matches_service = ReferenceEquals(toasts.toasts, list.ItemsSource), items_count = list.Items.Count,
                service_messages = toasts.toasts.Select(toast => new { toast.Msg, type = toast.Type.ToString() }).ToArray(),
                rendered_messages = Descendants(host).OfType<TextBlock>().Select(text => text.Text).ToArray(),
                database_rows_preserved = true, favorite_preserved = !first.IsFavorite, source_binding_modified_by_test = false, verification_complete = false,
            }, new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
            Assert.Same(toasts.toasts, list.ItemsSource);
            await WaitPhotoUi(() => Descendants(host).OfType<TextBlock>().Any(text => text.Text == expected), "写真詳細の故障通知が実ToastHostに表示されません。");
            Assert.Equal(page.ActualTheme, host.ActualTheme); observed.PhotoModalChecks.Add("actual-sql-failure-toast-binding-and-message-rendered");
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_photo_fault"); }
        InvokePhotoButton(PhotoElement<Button>(page, "CloseButtonOverlay")); await WaitPhotoUi(() => !page.IsLoaded, "故障通知後の写真詳細を解除できません。");
    }

    private static T PhotoElement<T>(PhotoModalPage page, string name) where T : class => Assert.IsType<T>(page.FindName(name));
    private static IEnumerable<Button> AssignedTagButtons(PhotoModalPage page) => Descendants(page).OfType<Button>().Where(button => button.Tag is string);
    private static void InvokePhotoButton(Button button)
    {
        Assert.True(button.IsEnabled); var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static async Task ClickPhotoButton(Button button)
    {
        var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler observe = (_, _) => clicked.TrySetResult(); button.Click += observe;
        try { InvokePhotoButton(button); await clicked.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { button.Click -= observe; }
    }
    private static async Task ConfirmPhotoTag(PhotoModalPage page, string tag)
    {
        InvokePhotoButton(PhotoElement<Button>(page, "TagAddRow")); await WaitPhotoUi(() => page.HasBlockingInnerOverlayOpen, "タグ追加画面を開きません。");
        var choice = Assert.Single(PhotoElement<StackPanel>(page, "ExistingTagList").Children.OfType<Button>(), button => AutomationProperties.GetName(button) == $"タグ「{tag}」を選択");
        InvokePhotoButton(choice); await WaitPhotoUi(() => PhotoElement<Button>(page, "TagAddConfirmButton").IsEnabled, "既存タグの選択を確定できません。");
        InvokePhotoButton(PhotoElement<Button>(page, "TagAddConfirmButton")); await WaitPhotoUi(() => !page.HasBlockingInnerOverlayOpen, "タグ追加の処理が完了しません。");
    }
    private static async Task WaitPhotoFailure(ToastService toasts, string expected)
    {
        await WaitPhotoUi(() => toasts.toasts.Any(toast => toast.Type == ToastType.error && toast.Msg == expected), "写真詳細の故障通知がToastServiceへ届きません。");
        var failure = Assert.Single(toasts.toasts); Assert.Equal(ToastType.error, failure.Type); Assert.Equal(expected, failure.Msg);
    }
    private static void AssertFavoriteBinding(Button favorite, bool expected)
    {
        var marker = Assert.Single(Assert.IsType<Grid>(favorite.Content).Children.OfType<Border>());
        Assert.Equal(expected ? Visibility.Visible : Visibility.Collapsed, marker.Visibility);
        Assert.Equal(expected, Assert.Single(Descendants(favorite).OfType<AnimatedFavoriteStar>()).Liked);
    }
    private static async Task WaitPhotoUi(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew(); while (!condition()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), message); await Task.Delay(20); }
    }
    private static void PhotoSql(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static long PhotoScalar(string dbPath, string sql, string? path = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; if (path is not null) command.Parameters.AddWithValue("$path", path); return (long)command.ExecuteScalar()!;
    }
    private static string PhotoUnrelatedSnapshot(string dbPath, string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
        var queries = new[] { "SELECT * FROM photos WHERE photo_path <> $path ORDER BY photo_path", "SELECT * FROM photo_tags WHERE photo_path <> $path ORDER BY photo_path,tag_id",
            "SELECT * FROM tags ORDER BY id", "SELECT * FROM photo_user_data_backup ORDER BY photo_filename", "SELECT * FROM archive_world_visits ORDER BY id", "SELECT * FROM sqlite_sequence WHERE name <> 'tags' ORDER BY name" };
        var content = new List<List<object?[]>>();
        foreach (var sql in queries)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$path", path); using var reader = command.ExecuteReader(); var rows = new List<object?[]>();
            while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); content.Add(rows);
        }
        return JsonSerializer.Serialize(content);
    }
}
