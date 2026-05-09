using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.TagMaster;

public partial class TagMasterViewModel : UiThreadSafeObservableObject
{
    private const int MAX_TAG_LENGTH = 40;
    private readonly AlpheratzDb db;
    private readonly ToastService toastService;

    public UiObservableCollection<string> masterTags { get; } = [];
    [ObservableProperty] private string tagDraft = string.Empty;

    public TagMasterViewModel(AlpheratzDb db, ToastService toastService)
    {
        AppLogger.Trace("TagMasterViewModel.ctor: enter");
        this.db = db;
        this.toastService = toastService;
        AppLogger.Trace("TagMasterViewModel.ctor: exit");
    }

    public async Task loadTags()
    {
        AppLogger.Trace("TagMasterViewModel.loadTags: enter");
        try
        {
            var tags = await db.GetAllTagsAsync();
            masterTags.Clear();
            foreach (var tag in tags.OrderBy(tag => tag))
            {
                masterTags.Add(tag);
            }
        }
        catch (Exception ex)
        {
            // Rethrow: ShellViewModel.initialize awaits this and a missing
            // tag list breaks downstream filter UI; fail loud during startup.
            AppLogger.Error($"TagMasterViewModel.loadTags: threw: {ex}");
            throw;
        }
        AppLogger.Trace($"TagMasterViewModel.loadTags: exit count={masterTags.Count}");
    }

    public async Task createTag()
    {
        AppLogger.Trace($"TagMasterViewModel.createTag: enter draft={tagDraft}");
        var normalized = tagDraft.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            AppLogger.Trace("TagMasterViewModel.createTag: skip (empty)");
            return;
        }

        if (normalized.Length > MAX_TAG_LENGTH)
        {
            AppLogger.Trace("TagMasterViewModel.createTag: skip (too long)");
            toastService.addToast($"タグは{MAX_TAG_LENGTH}文字以内で入力してください。", ToastType.error);
            return;
        }

        try
        {
            await db.CreateTagMasterAsync(normalized);
            TagDraft = string.Empty;
            await loadTags();
            toastService.addToast("タグを追加しました。");
        }
        catch (Exception err)
        {
            // Continue: surface as toast (legacy alpheratz pattern).
            AppLogger.Error($"TagMasterViewModel.createTag: threw: {err}");
            toastService.addToast($"タグの追加に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("TagMasterViewModel.createTag: exit");
    }

    public async Task deleteTag(string tag)
    {
        AppLogger.Trace($"TagMasterViewModel.deleteTag: enter tag={tag}");
        try
        {
            await db.DeleteTagMasterAsync(tag);
            await loadTags();
            toastService.addToast("タグを削除しました。");
        }
        catch (Exception err)
        {
            AppLogger.Error($"TagMasterViewModel.deleteTag: threw: {err}");
            toastService.addToast($"タグの削除に失敗しました: {err}", ToastType.error);
        }
        AppLogger.Trace("TagMasterViewModel.deleteTag: exit");
    }
}
