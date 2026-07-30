using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using static Alpheratz.Messages.MessageCatalog;

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
    private readonly DispatcherService dispatcherService;

    /// <summary>タグマスタの全タグ。フィルタ UI 等が直接バインドする共有コレクション。</summary>
    public UiObservableCollection<string> masterTags { get; } = [];
    /// <summary>追加フォームの入力中文字列。</summary>
    [ObservableProperty] private string tagDraft = string.Empty;

    private static Task RunDbWriteOffUiThread(Func<Task> write)
        => Task.Run(async () => await write().ConfigureAwait(false));

    public TagMasterViewModel(AlpheratzDb db, ToastService toastService, DispatcherService? dispatcherService = null)
    {
        AppLogger.Trace("TagMasterViewModel.ctor: enter");
        this.db = db;
        this.toastService = toastService;
        this.dispatcherService = dispatcherService ?? new DispatcherService();
        AppLogger.Trace("TagMasterViewModel.ctor: exit");
    }

    /// <summary>
    /// 全タグを DB から読み込み masterTags を置換する。失敗時は例外を再送出して起動を中断させる
    /// （タグリストが空の状態で UI を立ち上げると、利用者がタグ追加と既存タグの区別を失う）。
    /// </summary>
    public async Task loadTags()
    {
        AppLogger.Trace("TagMasterViewModel.loadTags: enter");
        try
        {
            var tags = await db.GetAllTagsAsync().ConfigureAwait(false);
            await dispatcherService.RunOnUiThread(() =>
                masterTags.ReplaceAll(tags.OrderBy(tag => tag).ToArray())).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 起動時のタグ読込失敗はフィルタ UI の初期化に影響するため、呼出側へ返す。
            AppLogger.Error($"TagMasterViewModel.loadTags: threw: {ex}");
            throw;
        }
        AppLogger.Trace($"TagMasterViewModel.loadTags: exit count={masterTags.Count}");
    }

    /// <summary>
    /// TagDraft の内容で新規タグを作成する。前後の空白を除いた結果が空なら処理せず、長さ超過は通知する。
    /// DB 追加成功時は loadTags() で一覧再取得し、ドラフトをクリアする。
    /// </summary>
    public async Task createTag()
    {
        AppLogger.Trace($"TagMasterViewModel.createTag: enter draft={TagDraft}");
        var normalized = TagDraft.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            AppLogger.Trace("TagMasterViewModel.createTag: skip (empty)");
            toastService.addToast(getMsg("TagMasterViewModel.tagNameRequired"), ToastType.error);
            return;
        }

        if (normalized.Length > MAX_TAG_LENGTH)
        {
            AppLogger.Trace("TagMasterViewModel.createTag: skip (too long)");
            toastService.addToast(
                getMsg("TagMasterViewModel.tagTooLong", ("maxLength", MAX_TAG_LENGTH)),
                ToastType.error);
            return;
        }

        if (masterTags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            AppLogger.Trace("TagMasterViewModel.createTag: skip (duplicate)");
            toastService.addToast(getMsg("TagMasterViewModel.tagAlreadyExists"), ToastType.error);
            return;
        }

        try
        {
            await RunDbWriteOffUiThread(() => db.CreateTagMasterAsync(normalized)).ConfigureAwait(false);
            try
            {
                await loadTags().ConfigureAwait(false);
            }
            catch (Exception refreshError)
            {
                // DB への追加は完了しているため、再読込だけ失敗した場合は現在一覧へ追加結果を反映する。
                AppLogger.Warn($"TagMasterViewModel.createTag: refresh failed after write: {refreshError.Message}");
                await dispatcherService.RunOnUiThread(() =>
                {
                    if (!masterTags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                        masterTags.ReplaceAll(masterTags.Append(normalized).OrderBy(tag => tag).ToArray());
                }).ConfigureAwait(false);
            }
            await dispatcherService.RunOnUiThread(() => TagDraft = string.Empty).ConfigureAwait(false);
            toastService.addToast(getMsg("TagMasterViewModel.tagSaved"));
        }
        catch (Exception err)
        {
            // 入力操作の失敗は画面上の通知に変換し、編集状態は維持する。
            AppLogger.Error($"TagMasterViewModel.createTag: threw: {err}");
            toastService.addToast(getMsg("TagMasterViewModel.tagAddFailed"), ToastType.error);
        }
        AppLogger.Trace("TagMasterViewModel.createTag: exit");
    }

    /// <summary>
    /// タグマスタからタグを削除する。photo_tags の中間行も DB 側でカスケード削除されるため、
    /// 既に写真に付与されていたタグも一括で外れる。失敗時は画面上で通知する。
    /// </summary>
    public async Task<bool> tryDeleteTag(string tag)
    {
        AppLogger.Trace($"TagMasterViewModel.deleteTag: enter tag={tag}");
        try
        {
            await RunDbWriteOffUiThread(() => db.DeleteTagMasterAsync(tag)).ConfigureAwait(false);
            try
            {
                await loadTags().ConfigureAwait(false);
            }
            catch (Exception refreshError)
            {
                // DB への削除は完了しているため、再読込だけ失敗した場合は現在一覧から削除結果を反映する。
                AppLogger.Warn($"TagMasterViewModel.deleteTag: refresh failed after write: {refreshError.Message}");
                await dispatcherService.RunOnUiThread(() =>
                {
                    var existing = masterTags.FirstOrDefault(current =>
                        string.Equals(current, tag, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                        masterTags.Remove(existing);
                }).ConfigureAwait(false);
            }
            toastService.addToast(getMsg("TagMasterViewModel.tagDeleted"));
            AppLogger.Trace("TagMasterViewModel.deleteTag: exit result=true");
            return true;
        }
        catch (Exception err)
        {
            AppLogger.Error($"TagMasterViewModel.deleteTag: threw: {err}");
            toastService.addToast(getMsg("TagMasterViewModel.tagDeleteFailed"), ToastType.error);
            AppLogger.Trace("TagMasterViewModel.deleteTag: exit result=false");
            return false;
        }
    }

    /// <summary>戻り値を使わない呼出元向けに、タグ削除を実行する互換ラッパ。</summary>
    public async Task deleteTag(string tag)
    {
        await tryDeleteTag(tag).ConfigureAwait(false);
    }
}
