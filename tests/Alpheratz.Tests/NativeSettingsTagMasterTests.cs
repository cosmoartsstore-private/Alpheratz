using System.Text.Json;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Features.TagMaster;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Tests;

public sealed partial class NativeStartupTests
{
    [Fact]
    public Task NativeStartup_PhotoModalTagMasterInputBindingsAndSavedTagReuse() => RunIsolatedAsync("photo-tags-input", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalTagMasterInsertFailureKeepsInputAndSameButtonRetries() => RunIsolatedAsync("photo-tags-create-retry", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalTagMasterDeleteFailureRollsBackPhotoLinksAndSameButtonRetries() => RunIsolatedAsync("photo-tags-delete-retry", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalTagMasterDeleteFailureKeepsActiveFilterAndBothPhotoLinks() => RunIsolatedAsync("photo-tags-delete-filter-retry", "dark", false);

    [Fact]
    public Task NativeStartup_PhotoModalTagMasterLastDeleteShowsEmptyStateAndDeletedDraftCanBeSavedAgain() => RunIsolatedAsync("photo-tags-empty-reuse", "dark", false);

    // 写真詳細の実callbackからSettingsへ遷移する。入力・保存は本体のDataContext、XBF、DIを維持する。
    private static async Task VerifySettingsTagsFromPhoto(ShellPage shell, ShellViewModel viewModel, PhotoModalViewModel modal,
        PhotoModalPage photoPage, PhotoThumbnailItem first, PhotoThumbnailItem second, string dbPath, StartupObservation observed, string kind)
    {
        var before = DatabaseSnapshot(dbPath);
        observed.Step = kind + ": opening-tag-master";
        InvokePhotoButton(PhotoElement<Button>(photoPage, "OpenTagMasterButton"));
        await WaitPhotoUi(() => !photoPage.IsLoaded && Descendants(shell).OfType<SettingsPage>().Any(page => page.IsLoaded), "写真詳細からタグマスタへ遷移しません。");
        var page = Assert.Single(Descendants(shell).OfType<SettingsPage>());
        var stage = Assert.Single(Descendants(shell).OfType<ShellStage>());
        var composite = Assert.IsType<SettingsCompositeViewModel>(page.DataContext);
        Assert.Same(viewModel.tagMasterViewModel, composite.TagMaster); Assert.Same(viewModel.settingsViewModel, composite.Settings);
        Assert.Same(viewModel.templatePageViewModel, composite.Template); Assert.Equal(shell.ActualTheme, page.ActualTheme);
        Assert.Equal(Visibility.Visible, TagElement<StackPanel>(page, "TagMasterSection").Visibility);
        Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, "GeneralSection").Visibility);
        Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, "SimilarPhotosSection").Visibility);
        Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, "TemplateSection").Visibility);
        Assert.Equal(Visibility.Collapsed, TagElement<StackPanel>(page, "CreditsSection").Visibility);
        Assert.Equal(Visibility.Collapsed, stage.TopModalVisibility); Assert.Equal(Visibility.Visible, stage.ModalVisibility);
        Assert.Null(modal.state.SelectedPhoto); Assert.Empty(modal.state.photoHistory); Assert.Null(PhotoElement<Image>(photoPage, "ModalImage").Source);
        Assert.Same(composite.TagMaster.masterTags, TagElement<ItemsControl>(page, "TagRepeater").ItemsSource);
        await WaitPhotoUi(() => TagDeleteButtons(page).Count() == 2, "実タグマスタの保存済みタグが表示されません。");
        Assert.Equal(Visibility.Collapsed, TagElement<Border>(page, "TagEmptyState").Visibility);
        Assert.Equal(before, DatabaseSnapshot(dbPath)); observed.PhotoModalChecks.Add("actual-photo-close-and-settings-tags-section-with-shared-di-and-xbf");

        if (kind == "photo-tags-input") await VerifyTagInputAndReuse(shell, viewModel, page, first, second, dbPath, observed);
        else if (kind == "photo-tags-create-retry") await VerifyTagInsertFailure(page, composite.TagMaster, dbPath, observed);
        else if (kind == "photo-tags-delete-filter-retry") await VerifyTagDeleteFilterFailure(viewModel, page, composite.TagMaster, dbPath, observed);
        else if (kind == "photo-tags-empty-reuse") await VerifyLastTagDeleteAndReuse(shell, viewModel, page, first, second, dbPath, observed);
        else { Assert.Equal("photo-tags-delete-retry", kind); await VerifyTagDeleteFailure(viewModel, page, composite.TagMaster, first, dbPath, observed); }
        await CloseTagSettings(page, stage);
        observed.PhotoModalChecks.Add("actual-settings-close-unloads-and-clears-middle-modal");
    }

    private static async Task VerifyTagInputAndReuse(ShellPage shell, ShellViewModel viewModel, SettingsPage page,
        PhotoThumbnailItem first, PhotoThumbnailItem second, string dbPath, StartupObservation observed)
    {
        var tags = viewModel.tagMasterViewModel; var before = DatabaseSnapshot(dbPath); var protectedRows = TagProtectedSnapshot(dbPath);
        var toasts = App.Services!.GetRequiredService<ToastService>(); var stage = Assert.Single(Descendants(shell).OfType<ShellStage>());
        // 起動時の案内を解除し、この入力操作が通知を発生させないことを照合する。
        toasts.toasts.Clear();
        Assert.Equal(25, TagElement<TextBox>(page, "TagInputBox").MaxLength);
        await SetNativeTagInput(page, tags, "", false, "");
        await SetNativeTagInput(page, tags, " \u3000 ", false, "");
        await SetNativeTagInput(page, tags, "  保持  ", false, "同じタグが登録されています。");
        await SetNativeTagInput(page, tags, new string('あ', 24), true, "");
        var maximum = new string('あ', 25);
        await SetNativeTagInput(page, tags, maximum, true, "タグ名は25文字が上限です。これ以上は入力できません。");
        Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Empty(toasts.toasts);
        observed.PhotoModalChecks.Add("actual-textbox-text-binding-empty-whitespace-duplicate-and-24-25-character-boundaries");

        toasts.toasts.Clear(); InvokePhotoButton(TagElement<Button>(page, "AddTagButton"));
        await WaitTagSaved(page, tags, maximum, toasts);
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = $path", maximum));
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(protectedRows, TagProtectedSnapshot(dbPath));
        await SetNativeTagInput(page, tags, "  Camera  ", true, "");
        toasts.toasts.Clear(); InvokePhotoButton(TagElement<Button>(page, "AddTagButton")); await WaitTagSaved(page, tags, "Camera", toasts);
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = 'Camera'"));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = '  Camera  '"));
        Assert.Equal(4L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        await SetNativeTagInput(page, tags, "camera", false, "同じタグが登録されています。");
        Assert.Equal(protectedRows, TagProtectedSnapshot(dbPath));
        observed.PhotoModalChecks.Add("actual-add-button-commits-normalized-and-maximum-tags-clears-bound-input-and-rejects-case-duplicate");

        await SetNativeTagInput(page, tags, "", false, ""); await CloseTagSettings(page, stage);
        // 解除前に予約済みの通知を処理し、解除後の新しいdraft変更への購読だけを確認する。
        var previouslyQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => previouslyQueued.SetResult()));
        await previouslyQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var detachedValidation = TagElement<TextBlock>(page, "TagValidationText").Text;
        tags.TagDraft = "保持";
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => drained.SetResult()));
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(detachedValidation, TagElement<TextBlock>(page, "TagValidationText").Text);
        shell.ShowSettings("tags"); await WaitPhotoUi(() => page.IsLoaded, "cacheしたSettingsPageを再表示できません。");
        Assert.Same(page, Assert.Single(Descendants(shell).OfType<SettingsPage>()));
        await WaitPhotoUi(() => TagElement<TextBlock>(page, "TagValidationText").Text == "同じタグが登録されています。", "再表示後の入力検証購読が有効になりません。");
        await SetNativeTagInput(page, tags, "再表示", true, ""); await SetNativeTagInput(page, tags, "", false, "");
        await CloseTagSettings(page, stage); observed.PhotoModalChecks.Add("actual-cached-settings-unloaded-detaches-editor-and-loaded-restores-input-validation");

        var reopened = Assert.IsType<PhotoModalViewModel>(viewModel.createPhotoModalViewModelFromList(first, [first, second]));
        shell.ShowPhotoModal(reopened); await WaitPhotoUi(() => Descendants(shell).OfType<PhotoModalPage>().Any(next => next.IsLoaded), "作成後の写真詳細を再表示できません。");
        var nextPhotoPage = Assert.Single(Descendants(shell).OfType<PhotoModalPage>()); Assert.Same(reopened, nextPhotoPage.DataContext);
        var beforeTagLink = PhotoUnrelatedSnapshot(dbPath, first.PhotoPath); var beforePhotoRows = TagProtectedSnapshot(dbPath, includePhotoLinks: false);
        await ConfirmPhotoTag(nextPhotoPage, maximum);
        await WaitPhotoUi(() => AssignedTagButtons(nextPhotoPage).Any(button => Equals(button.Tag, maximum)), "保存したタグを写真詳細で再利用できません。");
        Assert.Contains(maximum, first.Tags); Assert.Contains("保持", first.Tags); Assert.Equal(2, first.Tags.Count);
        Assert.Equal(2L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", first.PhotoPath));
        Assert.Equal(4L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags")); Assert.Equal(5L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path AND tag_id = (SELECT id FROM tags WHERE name = '保持')", first.PhotoPath));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", second.PhotoPath));
        Assert.Equal(beforeTagLink, PhotoUnrelatedSnapshot(dbPath, first.PhotoPath)); Assert.Equal(beforePhotoRows, TagProtectedSnapshot(dbPath, includePhotoLinks: false));
        InvokePhotoButton(PhotoElement<Button>(nextPhotoPage, "CloseButtonOverlay")); await WaitPhotoUi(() => !nextPhotoPage.IsLoaded, "タグ再利用後の写真詳細を解除できません。");
        observed.PhotoModalChecks.Add("actual-created-master-tag-reused-by-reopened-photo-modal-and-persisted-link");
        // 呼出元の最終解除確認も同じSettings instanceを対象にする。
        shell.ShowSettings("tags"); await WaitPhotoUi(() => page.IsLoaded, "最終解除前のSettingsPageを再表示できません。");
    }

    private static async Task VerifyTagInsertFailure(SettingsPage page, TagMasterViewModel tags, string dbPath, StartupObservation observed)
    {
        var toasts = App.Services!.GetRequiredService<ToastService>(); var before = DatabaseSnapshot(dbPath); var protectedRows = TagProtectedSnapshot(dbPath);
        var saved = tags.masterTags.ToArray(); const string draft = "  再試行タグ  ";
        await SetNativeTagInput(page, tags, draft, true, ""); var add = TagElement<Button>(page, "AddTagButton");
        observed.Step = "photo-tags-create-retry: insert-fault";
        PhotoSql(dbPath, "CREATE TRIGGER owned_tag_master_fault BEFORE INSERT ON tags BEGIN SELECT RAISE(ABORT, 'owned tag master insert fault'); END");
        try
        {
            toasts.toasts.Clear(); InvokePhotoButton(add);
            await WaitPhotoFailure(toasts, "タグを追加できませんでした。入力内容は保持されています。");
            Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Equal(saved, tags.masterTags); Assert.Equal(draft, tags.TagDraft);
            Assert.Equal(draft, TagElement<TextBox>(page, "TagInputBox").Text); Assert.True(add.IsEnabled); Assert.Equal(2, TagDeleteButtons(page).Count());
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_tag_master_fault"); }
        observed.Step = "photo-tags-create-retry: same-button-retry";
        toasts.toasts.Clear(); InvokePhotoButton(add); await WaitTagSaved(page, tags, "再試行タグ", toasts);
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = '再試行タグ'"));
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'")); Assert.Equal(protectedRows, TagProtectedSnapshot(dbPath));
        observed.PhotoModalChecks.Add("actual-master-insert-sql-rollback-keeps-draft-and-same-add-button-retry-commits-only-new-tag");
    }

    private static async Task VerifyTagDeleteFailure(ShellViewModel viewModel, SettingsPage page, TagMasterViewModel tags,
        PhotoThumbnailItem first, string dbPath, StartupObservation observed)
    {
        var toasts = App.Services!.GetRequiredService<ToastService>(); var before = DatabaseSnapshot(dbPath); var retainedPhotos = viewModel.galleryViewModel.photosState.photos.ToArray();
        var protectedRows = TagProtectedSnapshot(dbPath, includePhotoLinks: false); var saved = tags.masterTags.ToArray();
        var remove = Assert.Single(TagDeleteButtons(page), button => Equals(button.DataContext, "保持"));
        observed.Step = "photo-tags-delete-retry: parent-delete-fault-after-link-delete";
        // 本体SQLはphoto_tagsを先に削除する。後続のmaster削除を失敗させ、先行関連行のrollbackも確認する。
        PhotoSql(dbPath, "CREATE TRIGGER owned_tag_master_fault BEFORE DELETE ON tags WHEN OLD.name = '保持' BEGIN SELECT RAISE(ABORT, 'owned tag master delete fault'); END");
        try
        {
            toasts.toasts.Clear(); InvokePhotoButton(remove);
            await WaitPhotoFailure(toasts, "タグを削除できませんでした。写真への設定は変更されていません。");
            Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Equal(saved, tags.masterTags); Assert.Equal(["保持"], first.Tags);
            Assert.Equal(retainedPhotos, viewModel.galleryViewModel.photosState.photos); Assert.Contains(remove, TagDeleteButtons(page));
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_tag_master_fault"); }
        observed.Step = "photo-tags-delete-retry: same-button-retry";
        toasts.toasts.Clear(); InvokePhotoButton(remove);
        await WaitPhotoUi(() => !tags.masterTags.Contains("保持") && !TagDeleteButtons(page).Any(button => Equals(button.DataContext, "保持"))
            && viewModel.galleryViewModel.photosState.photos.Single(photo => photo.PhotoPath == first.PhotoPath).Tags.Count == 0, "同じタグ削除buttonの再試行が一覧と写真へ反映されません。");
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = '保持'"));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags")); Assert.Equal(["追加候補"], tags.masterTags);
        Assert.Equal(2L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'")); Assert.Equal(protectedRows, TagProtectedSnapshot(dbPath, includePhotoLinks: false));
        var success = Assert.Single(toasts.toasts); Assert.Equal("タグと写真への設定を削除しました。", success.Msg); Assert.Equal(ToastType.info, success.Type);
        observed.PhotoModalChecks.Add("actual-master-delete-sql-rollback-restores-earlier-photo-link-delete-and-same-button-retry-refreshes-gallery");
    }

    private static async Task VerifyTagDeleteFilterFailure(ShellViewModel viewModel, SettingsPage page, TagMasterViewModel tags,
        string dbPath, StartupObservation observed)
    {
        var gallery = viewModel.galleryViewModel; var filters = gallery.filtersState; var toasts = App.Services!.GetRequiredService<ToastService>();
        Assert.Empty(filters.tagFilters); Assert.Equal(0, filters.ActiveFilterCount);
        var beforeFilter = DatabaseSnapshot(dbPath);
        var reloaded = false; Action replaced = () => reloaded = true; gallery.photosState.OnPhotosReplaced += replaced;
        try
        {
            // filter controlの物理入力は別途残し、本体collection変更からの再読込を観測する。
            filters.tagFilters.Add("保持");
            await WaitPhotoUi(() => reloaded && !gallery.photosState.IsLoading, "タグfilter変更による本体gallery再読込が完了しません。");
        }
        finally { gallery.photosState.OnPhotosReplaced -= replaced; }
        await gallery.loadTagFilterCounts();
        Assert.Equal(beforeFilter, DatabaseSnapshot(dbPath));
        var retained = gallery.photosState.photos.ToArray(); Assert.Equal(2, retained.Length);
        var first = Assert.Single(retained, photo => photo.PhotoFilename == "写真①.png");
        var second = Assert.Single(retained, photo => photo.PhotoFilename == "写真②.png");
        Assert.Equal(["保持"], first.Tags); Assert.Equal(2, second.Tags.Count); Assert.Contains("保持", second.Tags); Assert.Contains("追加候補", second.Tags);
        var retainedTags = retained.ToDictionary(photo => photo.PhotoPath, photo => photo.Tags.ToArray()); Assert.Equal(["保持"], filters.tagFilters);
        Assert.Equal(1, filters.ActiveFilterCount); Assert.Equal(2L, filters.TagFilterCounts["保持"]);
        Assert.Equal(1L, filters.TagFilterCounts["追加候補"]); Assert.Equal(3L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags"));
        var before = DatabaseSnapshot(dbPath); var protectedRows = TagProtectedSnapshot(dbPath, includePhotoLinks: false);
        var counts = filters.TagFilterCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var remove = Assert.Single(TagDeleteButtons(page), button => Equals(button.DataContext, "保持"));
        observed.Step = "photo-tags-delete-filter-retry: parent-delete-fault";
        PhotoSql(dbPath, "CREATE TRIGGER owned_tag_master_fault BEFORE DELETE ON tags WHEN OLD.name = '保持' BEGIN SELECT RAISE(ABORT, 'owned filtered tag delete fault'); END");
        try
        {
            toasts.toasts.Clear(); await InvokeTagDeleteAndWait(page, remove, "保持");
            await WaitPhotoFailure(toasts, "タグを削除できませんでした。写真への設定は変更されていません。");
            Assert.Equal(before, DatabaseSnapshot(dbPath)); Assert.Equal(retained, gallery.photosState.photos);
            Assert.All(gallery.photosState.photos, photo => Assert.Equal(retainedTags[photo.PhotoPath], photo.Tags));
            Assert.Equal(["保持"], filters.tagFilters); Assert.Equal(1, filters.ActiveFilterCount);
            Assert.Equal(counts.OrderBy(pair => pair.Key), filters.TagFilterCounts.OrderBy(pair => pair.Key));
            Assert.Equal(2, tags.masterTags.Count); Assert.Contains(remove, TagDeleteButtons(page));
        }
        finally { PhotoSql(dbPath, "DROP TRIGGER owned_tag_master_fault"); }
        observed.Step = "photo-tags-delete-filter-retry: same-button-retry";
        toasts.toasts.Clear(); await InvokeTagDeleteAndWait(page, remove, "保持");
        await WaitPhotoUi(() => !gallery.photosState.IsLoading && filters.tagFilters.Count == 0 && gallery.photosState.photos.Count == 2
            && gallery.photosState.photos.All(photo => !photo.Tags.Contains("保持")) && !TagDeleteButtons(page).Any(button => Equals(button.DataContext, "保持")),
            "再試行後のfilter解除と複数写真・tag一覧の更新が完了しません。");
        Assert.Equal(0, filters.ActiveFilterCount); Assert.False(filters.TagFilterCounts.ContainsKey("保持"));
        Assert.Equal(1L, Assert.Single(filters.TagFilterCounts).Value); Assert.True(filters.TagFilterCounts.ContainsKey("追加候補"));
        Assert.Equal(retained.Select(photo => photo.PhotoPath).Order(), gallery.photosState.photos.Select(photo => photo.PhotoPath).Order());
        Assert.Empty(gallery.photosState.photos.Single(photo => photo.PhotoPath == first.PhotoPath).Tags);
        Assert.Equal(["追加候補"], gallery.photosState.photos.Single(photo => photo.PhotoPath == second.PhotoPath).Tags);
        Assert.Equal(["追加候補"], tags.masterTags); Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags"));
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path AND tag_id = 2", second.PhotoPath));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", first.PhotoPath));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags WHERE name = '保持'"));
        Assert.Equal(4L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(protectedRows, TagProtectedSnapshot(dbPath, includePhotoLinks: false));
        var success = Assert.Single(toasts.toasts); Assert.Equal("タグと写真への設定を削除しました。", success.Msg); Assert.Equal(ToastType.info, success.Type);
        observed.PhotoModalChecks.Add("actual-filter-collection-reload-and-master-delete-rollback-keep-both-photo-links-counts-and-active-filter");
        observed.PhotoModalChecks.Add("actual-same-delete-button-retry-clears-active-filter-and-refreshes-both-photos-with-unrelated-tag-link-and-storage-preserved");
    }

    private static async Task VerifyLastTagDeleteAndReuse(ShellPage shell, ShellViewModel viewModel, SettingsPage page,
        PhotoThumbnailItem first, PhotoThumbnailItem second, string dbPath, StartupObservation observed)
    {
        var tags = viewModel.tagMasterViewModel; var toasts = App.Services!.GetRequiredService<ToastService>();
        var stage = Assert.Single(Descendants(shell).OfType<ShellStage>());
        var untouchedRows = TagRowsWithoutMasters(dbPath);
        observed.Step = "photo-tags-empty-reuse: remove-linked-master";
        toasts.toasts.Clear(); await InvokeTagDeleteAndWait(page, Assert.Single(TagDeleteButtons(page), button => Equals(button.DataContext, "保持")), "保持");
        await WaitPhotoUi(() => tags.masterTags.SequenceEqual(["追加候補"]) && viewModel.galleryViewModel.photosState.photos.All(photo => photo.Tags.Count == 0), "先行tag削除が写真へ反映されません。");
        await SetNativeTagInput(page, tags, "追加候補", false, "同じタグが登録されています。");
        var removeLast = Assert.Single(TagDeleteButtons(page));
        observed.Step = "photo-tags-empty-reuse: remove-last-master";
        toasts.toasts.Clear(); await InvokeTagDeleteAndWait(page, removeLast, "追加候補");
        await WaitPhotoUi(() => tags.masterTags.Count == 0 && !TagDeleteButtons(page).Any()
            && TagElement<Border>(page, "TagEmptyState").Visibility == Visibility.Visible && TagElement<Button>(page, "AddTagButton").IsEnabled
            && TagElement<TextBlock>(page, "TagValidationText").Text.Length == 0, "最後のtag削除が空表示と同じdraftの追加可否へ反映されません。");
        Assert.Equal("追加候補", tags.TagDraft); Assert.Equal("追加候補", TagElement<TextBox>(page, "TagInputBox").Text);
        Assert.Equal(Visibility.Collapsed, TagElement<TextBlock>(page, "TagValidationText").Visibility);
        Assert.Empty(TagElement<ItemsControl>(page, "TagRepeater").Items); Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags"));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags")); Assert.Equal(2L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        var emptyStorage = DatabaseSnapshot(dbPath);
        // 最後のmasterだけを除いた非tag保存内容も、再表示と同じdraftの保存後に比較する。
        Assert.Equal(untouchedRows, TagRowsWithoutMasters(dbPath));
        await CloseTagSettings(page, stage); shell.ShowSettings("tags"); await WaitPhotoUi(() => page.IsLoaded, "空tag一覧のSettingsを再表示できません。");
        Assert.Equal(Visibility.Visible, TagElement<Border>(page, "TagEmptyState").Visibility); Assert.Empty(tags.masterTags);
        Assert.True(TagElement<Button>(page, "AddTagButton").IsEnabled); Assert.Equal("追加候補", TagElement<TextBox>(page, "TagInputBox").Text);
        Assert.Equal(emptyStorage, DatabaseSnapshot(dbPath));
        observed.PhotoModalChecks.Add("actual-last-master-delete-shows-empty-state-and-collection-change-enables-unchanged-duplicate-draft-after-reload");
        observed.Step = "photo-tags-empty-reuse: save-unchanged-draft";
        toasts.toasts.Clear(); InvokePhotoButton(TagElement<Button>(page, "AddTagButton")); await WaitTagSaved(page, tags, "追加候補", toasts);
        Assert.Equal(Visibility.Collapsed, TagElement<Border>(page, "TagEmptyState").Visibility); Assert.Equal(["追加候補"], tags.masterTags);
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(3L, PhotoScalar(dbPath, "SELECT id FROM tags WHERE name = '追加候補'"));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags")); Assert.Equal(untouchedRows, TagRowsWithoutMasters(dbPath));
        Assert.All(viewModel.galleryViewModel.photosState.photos, photo => Assert.Empty(photo.Tags));
        await CloseTagSettings(page, stage);
        var currentFirst = viewModel.galleryViewModel.photosState.photos.Single(photo => photo.PhotoPath == first.PhotoPath);
        var currentSecond = viewModel.galleryViewModel.photosState.photos.Single(photo => photo.PhotoPath == second.PhotoPath);
        var reopened = Assert.IsType<PhotoModalViewModel>(viewModel.createPhotoModalViewModelFromList(currentFirst, [currentFirst, currentSecond]));
        shell.ShowPhotoModal(reopened); await WaitPhotoUi(() => Descendants(shell).OfType<PhotoModalPage>().Any(next => next.IsLoaded), "空tagを持つ写真詳細を再表示できません。");
        var nextPhoto = Assert.Single(Descendants(shell).OfType<PhotoModalPage>());
        Assert.Equal(Visibility.Visible, PhotoElement<TextBlock>(nextPhoto, "AssignedTagEmptyNote").Visibility); Assert.Empty(AssignedTagButtons(nextPhoto));
        await ConfirmPhotoTag(nextPhoto, "追加候補");
        await WaitPhotoUi(() => AssignedTagButtons(nextPhoto).Any(button => Equals(button.Tag, "追加候補")), "作り直したmasterを写真へ付与できません。");
        Assert.Equal(["追加候補"], currentFirst.Tags); Assert.Empty(currentSecond.Tags);
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path AND tag_id = 3", first.PhotoPath));
        Assert.Equal(0L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM photo_tags WHERE photo_path = $path", second.PhotoPath));
        Assert.Equal(1L, PhotoScalar(dbPath, "SELECT COUNT(*) FROM tags")); Assert.Equal(4L, PhotoScalar(dbPath, "SELECT seq FROM sqlite_sequence WHERE name = 'tags'"));
        Assert.Equal(untouchedRows, TagRowsWithoutMasters(dbPath));
        InvokePhotoButton(PhotoElement<Button>(nextPhoto, "CloseButtonOverlay")); await WaitPhotoUi(() => !nextPhoto.IsLoaded, "再作成tagの写真詳細を解除できません。");
        shell.ShowSettings("tags"); await WaitPhotoUi(() => page.IsLoaded, "最終解除前のSettingsを再表示できません。");
        observed.PhotoModalChecks.Add("actual-empty-master-list-recreates-unchanged-draft-with-new-id-and-photo-link-without-changing-backup-or-archive");
    }

    // 実callbackの本体Taskをawaitして完了を観測する。委譲先と引数は維持し、終了後に元callbackへ戻す。
    private static async Task InvokeTagDeleteAndWait(SettingsPage page, Button button, string expected)
    {
        var actual = Assert.IsType<Func<string, Task>>(page.OnDeleteTag);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var received = new List<string>();
        page.OnDeleteTag = async tag =>
        {
            received.Add(tag);
            try { await actual(tag); completed.TrySetResult(); }
            catch (Exception error) { completed.TrySetException(error); throw; }
        };
        try { InvokePhotoButton(button); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(expected, Assert.Single(received)); }
        finally { page.OnDeleteTag = actual; }
    }

    private static string TagRowsWithoutMasters(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
        var rows = new List<List<object?[]>>();
        foreach (var query in new[] { "SELECT * FROM photos ORDER BY photo_path", "SELECT * FROM photo_user_data_backup ORDER BY photo_filename",
            "SELECT * FROM archive_world_visits ORDER BY id", "SELECT * FROM sqlite_sequence WHERE name <> 'tags' ORDER BY name" })
        {
            using var command = connection.CreateCommand(); command.CommandText = query; using var reader = command.ExecuteReader(); var content = new List<object?[]>();
            while (reader.Read()) content.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); rows.Add(content);
        }
        return JsonSerializer.Serialize(rows);
    }

    private static T TagElement<T>(SettingsPage page, string name) where T : class => Assert.IsType<T>(page.FindName(name));
    private static IEnumerable<Button> TagDeleteButtons(SettingsPage page) => Descendants(TagElement<ItemsControl>(page, "TagRepeater")).OfType<Button>();
    private static async Task SetNativeTagInput(SettingsPage page, TagMasterViewModel tags, string value, bool canAdd, string validation)
    {
        var input = TagElement<TextBox>(page, "TagInputBox");
        // 実TextBoxの変更からTwoWay bindingを検証する。物理key入力とnative UI Automation patternは対象外。
        Assert.False(input.IsReadOnly); input.Text = value;
        await WaitPhotoUi(() => input.Text == value && tags.TagDraft == value && TagElement<Button>(page, "AddTagButton").IsEnabled == canAdd
            && TagElement<TextBlock>(page, "TagValidationText").Text == validation, "実入力値・TagDraft・検証表示・追加buttonが同期しません。");
        Assert.Equal(validation.Length == 0 ? Visibility.Collapsed : Visibility.Visible, TagElement<TextBlock>(page, "TagValidationText").Visibility);
    }
    private static async Task WaitTagSaved(SettingsPage page, TagMasterViewModel tags, string normalized, ToastService toasts)
    {
        await WaitPhotoUi(() => tags.masterTags.Contains(normalized) && tags.TagDraft.Length == 0 && TagElement<TextBox>(page, "TagInputBox").Text.Length == 0
            && TagDeleteButtons(page).Any(button => Equals(button.DataContext, normalized)) && toasts.toasts.Any(toast => toast.Msg == "タグを保存しました。"), "実タグ追加の保存・再表示・入力解除が完了しません。");
        Assert.False(TagElement<Button>(page, "AddTagButton").IsEnabled);
        var saved = Assert.Single(toasts.toasts); Assert.Equal("タグを保存しました。", saved.Msg); Assert.Equal(ToastType.info, saved.Type);
        Assert.Equal(tags.masterTags.ToArray(), TagElement<ItemsControl>(page, "TagRepeater").Items.Cast<string>().ToArray());
    }
    private static async Task CloseTagSettings(SettingsPage page, ShellStage stage)
    {
        InvokePhotoButton(TagElement<Button>(page, "CloseButtonOverlay"));
        await WaitPhotoUi(() => !page.IsLoaded && stage.ModalContent is null && stage.ModalVisibility == Visibility.Collapsed, "SettingsPageのUnloadedと中位modal解除が完了しません。");
    }
    // タグmaster／対象の関連行以外の全保存内容と、既存master行・対象外の採番値を比較する。
    private static string TagProtectedSnapshot(string dbPath, bool includePhotoLinks = true)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()); connection.Open();
        var masterQuery = includePhotoLinks ? "SELECT * FROM tags WHERE name IN ('保持','追加候補') ORDER BY id" : "SELECT * FROM tags WHERE name = '追加候補' ORDER BY id";
        var queries = new List<string> { "SELECT * FROM photos ORDER BY photo_path", masterQuery,
            "SELECT * FROM photo_user_data_backup ORDER BY photo_filename", "SELECT * FROM archive_world_visits ORDER BY id", "SELECT * FROM sqlite_sequence WHERE name <> 'tags' ORDER BY name" };
        if (includePhotoLinks) queries.Add("SELECT * FROM photo_tags ORDER BY photo_path,tag_id");
        var rows = new List<List<object?[]>>();
        foreach (var query in queries)
        {
            using var command = connection.CreateCommand(); command.CommandText = query; using var reader = command.ExecuteReader(); var content = new List<object?[]>();
            while (reader.Read()) content.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); rows.Add(content);
        }
        return JsonSerializer.Serialize(rows);
    }
}
