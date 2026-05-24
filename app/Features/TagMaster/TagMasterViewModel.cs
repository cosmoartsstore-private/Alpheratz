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

/// <summary>
/// タグマスタ画面の ViewModel。タグの追加・削除と一覧表示を担当する。
/// masterTags は他画面 (PhotoModal のタグコンボボックス、フィルタパネルのタグサジェスト) からも
/// 共有参照されるため、本 VM の変更は自動的にそれらの UI にも反映される。
/// </summary>
public partial class TagMasterViewModel : UiThreadSafeObservableObject
{
    /// <summary>1 タグの最大文字数。UI の入力上限ではなくバリデーション側で弾く。</summary>
    private const int MAX_TAG_LENGTH = 40;
    private readonly AlpheratzDb db;
    private readonly ToastService toastService;

    /// <summary>タグマスタの全タグ。フィルタ UI 等が直接バインドする共有コレクション。</summary>
    public UiObservableCollection<string> masterTags { get; } = [];
    /// <summary>追加フォームの入力中文字列。</summary>
    [ObservableProperty] private string tagDraft = string.Empty;

    public TagMasterViewModel(AlpheratzDb db, ToastService toastService)
    {
        AppLogger.Trace("TagMasterViewModel.ctor: enter");
        this.db = db;
        this.toastService = toastService;
        AppLogger.Trace("TagMasterViewModel.ctor: exit");
    }

    /// <summary>
    /// 全タグを DB からロードし masterTags を置換する。失敗時は rethrow して起動を中断させる
    /// （タグリストが空の状態で UI を立ち上げると、ユーザがタグ追加と既存タグの区別を失う）。
    /// </summary>
    public async Task loadTags()
    {
        AppLogger.Trace("TagMasterViewModel.loadTags: enter");
        try
        {
            // N-50: 旧実装は `masterTags.Clear() → foreach Add` だったため、Clear と
            // Add の途中で例外 (OOM/UI スレッドエラー) が出ると masterTags が空のまま残り、
            // DB と UI が乖離した。先にローカルへ全件受けて確定してから ReplaceAll で原子化する。
            var tags = await db.GetAllTagsAsync();
            var sorted = tags.OrderBy(tag => tag).ToList();
            masterTags.ReplaceAll(sorted);
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

    /// <summary>
    /// TagDraft の内容で新規タグを作成する。trim 後空文字なら no-op、長さ超過は toast でエラー表示。
    /// DB 追加成功時は loadTags() で一覧再取得し、ドラフトをクリアする。
    /// </summary>
    public async Task createTag()
    {
        AppLogger.Trace($"TagMasterViewModel.createTag: enter draft={TagDraft}");
        var normalized = TagDraft.Trim();
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

    /// <summary>
    /// タグマスタからタグを削除する。photo_tags の中間行も DB 側でカスケード削除されるため、
    /// 既に写真に付与されていたタグも一括で外れる。失敗時は toast でエラー通知。
    /// </summary>
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
