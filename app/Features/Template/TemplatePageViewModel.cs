using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Features.Template;

/// <summary>
/// ツイート投稿用テンプレートの管理 + 写真から投稿テキストを組み立てる ViewModel。
/// テンプレート群は SettingsService 経由で JSON に保存される。
/// テンプレートには {world} {date} {tags} 等のプレースホルダを含み、buildTweetText で展開する。
/// </summary>
public partial class TemplatePageViewModel : UiThreadSafeObservableObject
{
    private readonly SettingsService settingsService;
    private readonly WorldService worldService;
    private readonly ToastService toastService;

    /// <summary>保存済みテンプレートの一覧。</summary>
    public UiObservableCollection<string> tweetTemplates { get; } = [];
    /// <summary>現在「投稿時に使う」として選ばれているテンプレート（空文字なら未選択）。</summary>
    [ObservableProperty] private string activeTweetTemplate = string.Empty;
    /// <summary>編集中フォームの入力文字列。</summary>
    [ObservableProperty] private string tweetTemplateDraft = string.Empty;
    /// <summary>編集対象の旧テンプレート文字列（新規モードでは null）。差分更新の判定に使う。</summary>
    [ObservableProperty] private string? editingTweetTemplate;

    public TemplatePageViewModel(SettingsService settingsService, WorldService worldService, ToastService toastService)
    {
        AppLogger.Trace("TemplatePageViewModel.ctor: enter");
        this.settingsService = settingsService;
        this.worldService = worldService;
        this.toastService = toastService;
        AppLogger.Trace("TemplatePageViewModel.ctor: exit");
    }

    /// <summary>テンプレート内の token (例: "{world}") を value に置換する純粋関数。</summary>
    public static string replaceTemplateToken(string template, string token, string value)
    {
        return template.Replace(token, value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 写真とテンプレートから最終的なツイート本文を組み立てる。
    /// プレースホルダ: {world} {world-name} {world_name} {world_id} {date} {timestamp} {file} {memo} {tags}
    /// 失敗時は空文字を返す（呼出側はそれを検知して送信を中断できる）。
    /// </summary>
    public string buildTweetText(string template, PhotoThumbnailItem photo)
    {
        AppLogger.Trace("TemplatePageViewModel.buildTweetText: enter");
        try
        {
            var world = string.IsNullOrWhiteSpace(photo.WorldName) ? "ワールド不明" : photo.WorldName.Trim();
            var date = photo.Timestamp.Length >= 16 ? photo.Timestamp[..16].Replace('T', ' ') : string.Empty;
            var tags = string.Join(" ", (photo.Tags ?? []).Select(t => $"#{t.Replace(" ", "", StringComparison.Ordinal)}"));

            var text = template;
            text = replaceTemplateToken(text, "{world}", world);
            text = replaceTemplateToken(text, "{world-name}", world);
            text = replaceTemplateToken(text, "{world_name}", world);
            text = replaceTemplateToken(text, "{world_id}", photo.WorldId ?? "");
            text = replaceTemplateToken(text, "{date}", date);
            text = replaceTemplateToken(text, "{timestamp}", photo.Timestamp);
            text = replaceTemplateToken(text, "{file}", photo.PhotoFilename);
            text = replaceTemplateToken(text, "{memo}", "");
            text = replaceTemplateToken(text, "{tags}", tags);
            AppLogger.Trace("TemplatePageViewModel.buildTweetText: exit");
            return text;
        }
        catch (Exception ex)
        {
            // Continue: return empty string so caller can detect & abort
            // safely; legacy alpheratz did not throw out of build.
            AppLogger.Error($"TemplatePageViewModel.buildTweetText: threw: {ex}");
            return string.Empty;
        }
    }

    /// <summary>
    /// アクティブテンプレートで写真からツイートを組み立て、画像をクリップボードに置いて
    /// Twitter Web Intent をブラウザで開く。
    /// 「画像クリップボード + テキスト Intent」の組合せにしているのは、Twitter Intent URL に
    /// 画像を直接添付する公式 API が存在しないため。ユーザはブラウザ側で貼り付ける動線になる。
    /// </summary>
    public async Task openTweetIntent(PhotoThumbnailItem photo)
    {
        AppLogger.Trace("TemplatePageViewModel.openTweetIntent: enter");
        if (string.IsNullOrWhiteSpace(ActiveTweetTemplate))
        {
            AppLogger.Trace("TemplatePageViewModel.openTweetIntent: skip (no active template)");
            return;
        }

        try
        {
            var text = Uri.EscapeDataString(buildTweetText(ActiveTweetTemplate, photo));
            var intentUrl = $"https://twitter.com/intent/tweet?text={text}";
            await worldService.CopyImageToClipboardAsync(photo.PhotoPath).ConfigureAwait(false);
            await worldService.OpenTweetIntentAsync(intentUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.openTweetIntent: threw: {ex}");
        }
        AppLogger.Trace("TemplatePageViewModel.openTweetIntent: exit");
    }

    /// <summary>指定テンプレートを編集モードに遷移する（EditingTweetTemplate=template, draft=template）。</summary>
    public void startEdit(string template)
    {
        AppLogger.Trace($"TemplatePageViewModel.startEdit: enter template={template}");
        EditingTweetTemplate = template;
        TweetTemplateDraft = template;
        AppLogger.Trace("TemplatePageViewModel.startEdit: exit");
    }

    /// <summary>編集モードを抜けてドラフトをクリアする（保存はしない）。</summary>
    public void cancelEdit()
    {
        AppLogger.Trace("TemplatePageViewModel.cancelEdit: enter");
        EditingTweetTemplate = null;
        TweetTemplateDraft = string.Empty;
        AppLogger.Trace("TemplatePageViewModel.cancelEdit: exit");
    }

    /// <summary>
    /// テンプレートを削除して即座に永続化する。
    /// 削除後の選択状態フォロー：
    ///   - 削除したものがアクティブテンプレートだった場合は先頭テンプレートに切り替え
    ///   - 削除したものが編集中だった場合は cancelEdit でドラフトをクリア
    /// saveTemplates をその場で await するのは、削除が即時永続化されないとアプリ再起動で
    /// 復活する（旧実装の永続化漏れ）バグを再発させないため。
    /// </summary>
    public async Task deleteTemplate(string template, AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace($"TemplatePageViewModel.deleteTemplate: enter template={template}");
        // ロールバック用に変更前の状態をキャプチャ。save 失敗時に in-memory が
        // DB と乖離して「再起動で復活」状態にならないように、save が成功するまで
        // メモリ側の確定を遅らせる代わりに「失敗時に戻す」アプローチを採る。
        var prevIndex = tweetTemplates.IndexOf(template);
        var prevActive = ActiveTweetTemplate;
        var prevEditing = EditingTweetTemplate;
        var prevDraft = TweetTemplateDraft;
        var didRemove = false;
        try
        {
            if (prevIndex >= 0)
            {
                tweetTemplates.RemoveAt(prevIndex);
                didRemove = true;
            }

            if (ActiveTweetTemplate == template)
            {
                ActiveTweetTemplate = tweetTemplates.FirstOrDefault() ?? string.Empty;
            }

            if (EditingTweetTemplate == template)
            {
                cancelEdit();
            }

            // 保存失敗時に呼ばれた場合は in-memory 状態を元に戻し、UI とディスクの整合を保つ。
            try
            {
                await settingsService.SaveSettingAsync(currentSetting with
                {
                    tweetTemplates = tweetTemplates,
                    activeTweetTemplate = ActiveTweetTemplate,
                }).ConfigureAwait(false);
                toastService.addToast("テンプレートを削除しました。");
            }
            catch (Exception saveEx)
            {
                AppLogger.Error($"TemplatePageViewModel.deleteTemplate: save failed, rolling back: {saveEx}");
                if (didRemove && prevIndex >= 0)
                {
                    var insertAt = Math.Min(prevIndex, tweetTemplates.Count);
                    tweetTemplates.Insert(insertAt, template);
                }
                ActiveTweetTemplate = prevActive;
                EditingTweetTemplate = prevEditing;
                TweetTemplateDraft = prevDraft;
                toastService.addToast($"テンプレートの削除に失敗しました: {saveEx.Message}", ToastType.error);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.deleteTemplate: threw: {ex}");
        }
        AppLogger.Trace("TemplatePageViewModel.deleteTemplate: exit");
    }

    /// <summary>
    /// ドラフトを保存する。
    /// 分岐：
    ///   - 編集モード (EditingTweetTemplate != null) → 該当 index を新値で差し替え、
    ///     アクティブテンプレートが旧値ならアクティブも新値に追従させる
    ///   - 新規モード → 重複が無ければ末尾に追加、アクティブテンプレ未選択なら自動的にアクティブ化
    /// 永続化自体は呼出側 (saveTemplates) の責務。本関数はインメモリ更新のみ。
    /// </summary>
    public void saveTemplateDraft()
    {
        AppLogger.Trace($"TemplatePageViewModel.saveTemplateDraft: enter editing={EditingTweetTemplate ?? "(new)"}");
        try
        {
            var normalized = TweetTemplateDraft.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                AppLogger.Trace("TemplatePageViewModel.saveTemplateDraft: skip (empty)");
                return;
            }

            if (EditingTweetTemplate is not null)
            {
                AppLogger.Trace("TemplatePageViewModel.saveTemplateDraft: branch=update existing");
                var index = tweetTemplates.IndexOf(EditingTweetTemplate);
                if (index >= 0)
                {
                    tweetTemplates[index] = normalized;
                }

                if (ActiveTweetTemplate == EditingTweetTemplate)
                {
                    ActiveTweetTemplate = normalized;
                }
            }
            else if (!tweetTemplates.Contains(normalized))
            {
                AppLogger.Trace("TemplatePageViewModel.saveTemplateDraft: branch=add new");
                tweetTemplates.Add(normalized);
                if (string.IsNullOrWhiteSpace(ActiveTweetTemplate))
                {
                    ActiveTweetTemplate = normalized;
                }
            }

            EditingTweetTemplate = null;
            TweetTemplateDraft = string.Empty;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.saveTemplateDraft: threw: {ex}");
        }
        AppLogger.Trace("TemplatePageViewModel.saveTemplateDraft: exit");
    }

    public async Task saveTemplate(AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace("TemplatePageViewModel.saveTemplate: enter");
        saveTemplateDraft();
        await saveTemplates(currentSetting).ConfigureAwait(false);
        AppLogger.Trace("TemplatePageViewModel.saveTemplate: exit");
    }

    public async Task saveTemplates(AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace($"TemplatePageViewModel.saveTemplates: enter count={tweetTemplates.Count}");
        try
        {
            await settingsService.SaveSettingAsync(currentSetting with
            {
                tweetTemplates = tweetTemplates,
                activeTweetTemplate = ActiveTweetTemplate,
            }).ConfigureAwait(false);
            toastService.addToast("テンプレートを保存しました。");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.saveTemplates: threw: {ex}");
            toastService.addToast($"テンプレートの保存に失敗しました: {ex}", ToastType.error);
        }
        AppLogger.Trace("TemplatePageViewModel.saveTemplates: exit");
    }
}