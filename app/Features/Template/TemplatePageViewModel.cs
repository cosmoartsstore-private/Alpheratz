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

public partial class TemplatePageViewModel : UiThreadSafeObservableObject
{
    private readonly SettingsService settingsService;
    private readonly WorldService worldService;
    private readonly ToastService toastService;

    public UiObservableCollection<string> tweetTemplates { get; } = [];
    [ObservableProperty] private string activeTweetTemplate = string.Empty;
    [ObservableProperty] private string tweetTemplateDraft = string.Empty;
    [ObservableProperty] private string? editingTweetTemplate;

    public TemplatePageViewModel(SettingsService settingsService, WorldService worldService, ToastService toastService)
    {
        AppLogger.Trace("TemplatePageViewModel.ctor: enter");
        this.settingsService = settingsService;
        this.worldService = worldService;
        this.toastService = toastService;
        AppLogger.Trace("TemplatePageViewModel.ctor: exit");
    }

    public static string replaceTemplateToken(string template, string token, string value)
    {
        // Pure string helper; no traces.
        return template.Replace(token, value, StringComparison.Ordinal);
    }

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

    public async Task openTweetIntent(PhotoThumbnailItem photo)
    {
        AppLogger.Trace("TemplatePageViewModel.openTweetIntent: enter");
        if (string.IsNullOrWhiteSpace(activeTweetTemplate))
        {
            AppLogger.Trace("TemplatePageViewModel.openTweetIntent: skip (no active template)");
            return;
        }

        try
        {
            var text = Uri.EscapeDataString(buildTweetText(activeTweetTemplate, photo));
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

    public void startEdit(string template)
    {
        AppLogger.Trace($"TemplatePageViewModel.startEdit: enter template={template}");
        EditingTweetTemplate = template;
        TweetTemplateDraft = template;
        AppLogger.Trace("TemplatePageViewModel.startEdit: exit");
    }

    public void cancelEdit()
    {
        AppLogger.Trace("TemplatePageViewModel.cancelEdit: enter");
        EditingTweetTemplate = null;
        TweetTemplateDraft = string.Empty;
        AppLogger.Trace("TemplatePageViewModel.cancelEdit: exit");
    }

    /// <summary>
    /// テンプレートを削除して即座に永続化する。
    /// 旧実装: コレクションから消すだけで saveTemplates を呼ばないため、
    /// アプリ再起動で削除が消えてしまう永続化漏れがあった。
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
                activeTweetTemplate = activeTweetTemplate,
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