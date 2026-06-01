using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Template;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class TemplatePageViewModel : UiThreadSafeObservableObject
{
	private const int MAX_TEMPLATE_LENGTH = 280;

	private readonly SettingsService settingsService;

	private readonly WorldService worldService;

	private readonly ToastService toastService;

	[ObservableProperty]
	private string activeTweetTemplate = string.Empty;

	[ObservableProperty]
	private string tweetTemplateDraft = string.Empty;

	[ObservableProperty]
	private string? editingTweetTemplate;

	public UiObservableCollection<string> tweetTemplates { get; } = new UiObservableCollection<string>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActiveTweetTemplate
	{
		get
		{
			return activeTweetTemplate;
		}
		[MemberNotNull("activeTweetTemplate")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(activeTweetTemplate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ActiveTweetTemplate);
				activeTweetTemplate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ActiveTweetTemplate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TweetTemplateDraft
	{
		get
		{
			return tweetTemplateDraft;
		}
		[MemberNotNull("tweetTemplateDraft")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(tweetTemplateDraft, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TweetTemplateDraft);
				tweetTemplateDraft = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TweetTemplateDraft);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? EditingTweetTemplate
	{
		get
		{
			return editingTweetTemplate;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(editingTweetTemplate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.EditingTweetTemplate);
				editingTweetTemplate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.EditingTweetTemplate);
			}
		}
	}

	public TemplatePageViewModel(SettingsService settingsService, WorldService worldService, ToastService toastService)
	{
		this.settingsService = settingsService;
		this.worldService = worldService;
		this.toastService = toastService;
	}

	public static string replaceTemplateToken(string template, string token, string value)
	{
		return template.Replace(token, value, StringComparison.Ordinal);
	}

	public string buildTweetText(string template, PhotoThumbnailItem photo)
	{
		try
		{
			string value = (string.IsNullOrWhiteSpace(photo.WorldName) ? "ワールド不明" : photo.WorldName.Trim());
			string value2 = ((photo.Timestamp.Length >= 16) ? photo.Timestamp.Substring(0, 16).Replace('T', ' ') : string.Empty);
			string value3 = string.Join(" ", (photo.Tags ?? Array.Empty<string>()).Select((string t) => "#" + t.Replace(" ", "", StringComparison.Ordinal)));
			return replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(replaceTemplateToken(template, "{world}", value), "{world-name}", value), "{world_name}", value), "{world_id}", photo.WorldId ?? ""), "{date}", value2), "{timestamp}", photo.Timestamp), "{file}", photo.PhotoFilename), "{memo}", ""), "{tags}", value3);
		}
		catch (Exception value4)
		{
			AppLogger.Error($"TemplatePageViewModel.buildTweetText: threw: {value4}");
			return string.Empty;
		}
	}

	public async Task openTweetIntent(PhotoThumbnailItem photo)
	{
		if (string.IsNullOrWhiteSpace(ActiveTweetTemplate))
		{
			return;
		}
		try
		{
			string text = Uri.EscapeDataString(buildTweetText(ActiveTweetTemplate, photo));
			string intentUrl = "https://twitter.com/intent/tweet?text=" + text;
			await worldService.CopyImageToClipboardAsync(photo.PhotoPath).ConfigureAwait(continueOnCapturedContext: false);
			await worldService.OpenTweetIntentAsync(intentUrl).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"TemplatePageViewModel.openTweetIntent: threw: {value}");
		}
	}

	public void startEdit(string template)
	{
		EditingTweetTemplate = template;
		TweetTemplateDraft = template;
	}

	public void cancelEdit()
	{
		EditingTweetTemplate = null;
		TweetTemplateDraft = string.Empty;
	}

	public async Task deleteTemplate(string template, AlpheratzSettingDto currentSetting)
	{
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
			await saveTemplates(currentSetting).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"TemplatePageViewModel.deleteTemplate: threw: {value}");
		}
	}

	public void saveTemplateDraft()
	{
		try
		{
			string text = TweetTemplateDraft.Trim();
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (text.Length > 280)
			{
				toastService.addToast($"テンプレートは{280}文字以内で入力してください。", ToastType.error);
				return;
			}
			if (EditingTweetTemplate != null)
			{
				int num = tweetTemplates.IndexOf(EditingTweetTemplate);
				if (num >= 0)
				{
					tweetTemplates[num] = text;
				}
				if (ActiveTweetTemplate == EditingTweetTemplate)
				{
					ActiveTweetTemplate = text;
				}
			}
			else if (!tweetTemplates.Contains(text))
			{
				tweetTemplates.Add(text);
				if (string.IsNullOrWhiteSpace(ActiveTweetTemplate))
				{
					ActiveTweetTemplate = text;
				}
			}
			EditingTweetTemplate = null;
			TweetTemplateDraft = string.Empty;
		}
		catch (Exception value)
		{
			AppLogger.Error($"TemplatePageViewModel.saveTemplateDraft: threw: {value}");
		}
	}

	public async Task saveTemplate(AlpheratzSettingDto currentSetting)
	{
		saveTemplateDraft();
		await saveTemplates(currentSetting).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task saveTemplates(AlpheratzSettingDto currentSetting)
	{
		try
		{
			await settingsService.SaveSettingAsync(currentSetting with
			{
				tweetTemplates = tweetTemplates,
				activeTweetTemplate = ActiveTweetTemplate
			}).ConfigureAwait(continueOnCapturedContext: false);
			toastService.addToast("テンプレートを保存しました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"TemplatePageViewModel.saveTemplates: threw: {value}");
			toastService.addToast($"テンプレートの保存に失敗しました: {value}", ToastType.error);
		}
	}
}
