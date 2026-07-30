using System;
using System.Collections.Generic;
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
using static Alpheratz.Messages.MessageCatalog;

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
    [ObservableProperty] private bool openWorldLinkOnPost;
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

    /// <summary>
    /// 設定 JSON から読み込んだテンプレート状態を VM に反映する。
    /// Shell / Settings と状態が分岐すると、別設定の保存時に古いテンプレートで
    /// 上書きされるため、投稿テンプレートの真実源はこの VM に寄せる。
    /// </summary>
    public void applySettings(IReadOnlyList<string>? templates, string? activeTemplate, bool openWorldLinkOnPost = false)
    {
        var nextTemplates = templates?
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .ToArray()
            ?? Array.Empty<string>();
        OpenWorldLinkOnPost = openWorldLinkOnPost;
        tweetTemplates.ReplaceAll(nextTemplates);
        ActiveTweetTemplate = !string.IsNullOrWhiteSpace(activeTemplate) && nextTemplates.Contains(activeTemplate)
            ? activeTemplate
            : nextTemplates.FirstOrDefault() ?? string.Empty;
        if (EditingTweetTemplate is not null && !nextTemplates.Contains(EditingTweetTemplate))
        {
            cancelEdit();
        }
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
            var world = string.IsNullOrWhiteSpace(photo.WorldName) ? getMsg("common.unknownWorld") : photo.WorldName.Trim();
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
            // 投稿処理側が空文字を検知して中断できるよう、本文生成の失敗はここで閉じる。
            AppLogger.Error($"TemplatePageViewModel.buildTweetText: threw: {ex}");
            return string.Empty;
        }
    }

    /// <summary>
    /// アクティブテンプレートで写真からツイートを組み立て、画像をクリップボードに置いて
    /// Twitter Web Intent をブラウザで開く。すべて完了した場合だけ true を返す。
    /// 「画像クリップボード + テキスト Intent」の組合せにしているのは、Twitter Intent URL に
    /// 画像を直接添付する公式 API が存在しないため。利用者はブラウザ側で画像を貼り付ける。
    /// </summary>
    public async Task<bool> openTweetIntent(PhotoThumbnailItem photo)
    {
        AppLogger.Trace("TemplatePageViewModel.openTweetIntent: enter");
        if (string.IsNullOrWhiteSpace(ActiveTweetTemplate))
        {
            AppLogger.Trace("TemplatePageViewModel.openTweetIntent: skip (no active template)");
            toastService.addToast(getMsg("TemplatePageViewModel.activeTemplateRequired"), ToastType.error);
            return false;
        }

        try
        {
            var tweetText = buildTweetText(ActiveTweetTemplate, photo);
            if (string.IsNullOrEmpty(tweetText))
            {
                AppLogger.Trace("TemplatePageViewModel.openTweetIntent: skip (empty tweet text)");
                toastService.addToast(getMsg("TemplatePageViewModel.postTextBuildFailed"), ToastType.error);
                return false;
            }

            var text = Uri.EscapeDataString(tweetText);
            var intentUrl = $"https://twitter.com/intent/tweet?text={text}";
            await worldService.CopyImageToClipboardAsync(photo.PhotoPath).ConfigureAwait(false);
            if (!await worldService.OpenTweetIntentAsync(intentUrl).ConfigureAwait(false))
            {
                toastService.addToast(getMsg("TemplatePageViewModel.postScreenOpenFailed"), ToastType.error);
                return false;
            }
            if (OpenWorldLinkOnPost && !string.IsNullOrWhiteSpace(photo.WorldId))
            {
                try
                {
                    await worldService.OpenWorldUrlAsync(photo.WorldId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"TemplatePageViewModel.openTweetIntent: world page open failed: {ex}");
                    toastService.addToast(getMsg("TemplatePageViewModel.worldPageOpenFailed"), ToastType.error);
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.openTweetIntent: threw: {ex}");
            toastService.addToast(getMsg("TemplatePageViewModel.postPreparationFailed"), ToastType.error);
            return false;
        }
        finally
        {
            AppLogger.Trace("TemplatePageViewModel.openTweetIntent: exit");
        }
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
    /// saveTemplates をその場で await し、削除直後の状態を設定ファイルへ確実に反映する。
    /// </summary>
    public async Task deleteTemplate(string template, AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace($"TemplatePageViewModel.deleteTemplate: enter template={template}");
        try
        {
            if (tweetTemplates.Contains(template))
            {
                tweetTemplates.Remove(template);
            }

            if (ActiveTweetTemplate == template)
            {
                ActiveTweetTemplate = tweetTemplates.FirstOrDefault() ?? string.Empty;
            }

            if (EditingTweetTemplate == template)
            {
                cancelEdit();
            }

            // コレクション変更後に即時保存することで、アプリ再起動でも削除を保持する。
            await saveTemplates(currentSetting).ConfigureAwait(false);
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
    ///   - 新規モード → 末尾に追加し、アクティブテンプレ未選択なら自動的にアクティブ化
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

    /// <summary>ドラフトをテンプレート一覧へ反映してから、設定ファイルへ保存する。</summary>
    public async Task saveTemplate(AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace("TemplatePageViewModel.saveTemplate: enter");
        var normalized = TweetTemplateDraft.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            AppLogger.Trace("TemplatePageViewModel.saveTemplate: skip (empty)");
            toastService.addToast(getMsg("TemplatePageViewModel.templateRequired"), ToastType.error);
            return;
        }
        var changesToExistingTemplate = EditingTweetTemplate is not null
            && !string.Equals(EditingTweetTemplate, normalized, StringComparison.Ordinal);
        if (tweetTemplates.Contains(normalized)
            && (EditingTweetTemplate is null || changesToExistingTemplate))
        {
            AppLogger.Trace("TemplatePageViewModel.saveTemplate: skip (duplicate)");
            toastService.addToast(getMsg("TemplatePageViewModel.templateAlreadyExists"), ToastType.error);
            return;
        }
        saveTemplateDraft();
        await saveTemplates(currentSetting).ConfigureAwait(false);
        AppLogger.Trace("TemplatePageViewModel.saveTemplate: exit");
    }

    /// <summary>現在のテンプレート一覧とアクティブテンプレートを設定ファイルへ保存する。</summary>
    public async Task saveTemplates(AlpheratzSettingDto currentSetting)
    {
        AppLogger.Trace($"TemplatePageViewModel.saveTemplates: enter count={tweetTemplates.Count}");
        try
        {
            await settingsService.SaveSettingAsync(new AlpheratzSettingDto
            {
                tweetTemplates = tweetTemplates,
                activeTweetTemplate = ActiveTweetTemplate,
            }).ConfigureAwait(false);
            toastService.addToast(getMsg("TemplatePageViewModel.templateSaved"));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePageViewModel.saveTemplates: threw: {ex}");
            toastService.addToast(getMsg("TemplatePageViewModel.templateSaveFailed"), ToastType.error);
        }
        AppLogger.Trace("TemplatePageViewModel.saveTemplates: exit");
    }
}
