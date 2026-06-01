using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Settings;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class SettingsViewModel : UiThreadSafeObservableObject
{
	private readonly SettingsService settingsService;

	private readonly DialogService dialogService;

	[ObservableProperty]
	private string photoFolderPath = string.Empty;

	[ObservableProperty]
	private string secondaryPhotoFolderPath = string.Empty;

	[ObservableProperty]
	private bool startupEnabled;

	[ObservableProperty]
	private ThemeMode themeMode;

	[ObservableProperty]
	private ViewMode viewMode;

	[ObservableProperty]
	private string activeTweetTemplate = string.Empty;

	public UiObservableCollection<string> tweetTemplates { get; } = new UiObservableCollection<string>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PhotoFolderPath
	{
		get
		{
			return photoFolderPath;
		}
		[MemberNotNull("photoFolderPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(photoFolderPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PhotoFolderPath);
				photoFolderPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PhotoFolderPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SecondaryPhotoFolderPath
	{
		get
		{
			return secondaryPhotoFolderPath;
		}
		[MemberNotNull("secondaryPhotoFolderPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(secondaryPhotoFolderPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SecondaryPhotoFolderPath);
				secondaryPhotoFolderPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SecondaryPhotoFolderPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool StartupEnabled
	{
		get
		{
			return startupEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(startupEnabled, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StartupEnabled);
				startupEnabled = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StartupEnabled);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ThemeMode ThemeMode
	{
		get
		{
			return themeMode;
		}
		set
		{
			if (!EqualityComparer<ThemeMode>.Default.Equals(themeMode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ThemeMode);
				themeMode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ThemeMode);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ViewMode ViewMode
	{
		get
		{
			return viewMode;
		}
		set
		{
			if (!EqualityComparer<ViewMode>.Default.Equals(viewMode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ViewMode);
				viewMode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ViewMode);
			}
		}
	}

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

	public SettingsViewModel(SettingsService settingsService, DialogService dialogService)
	{
		this.settingsService = settingsService;
		this.dialogService = dialogService;
	}

	public AlpheratzSettingDto buildSettingPayload(AlpheratzSettingDto? overrides = null)
	{
		return new AlpheratzSettingDto
		{
			photoFolderPath = (overrides?.photoFolderPath ?? PhotoFolderPath),
			secondaryPhotoFolderPath = (overrides?.secondaryPhotoFolderPath ?? SecondaryPhotoFolderPath),
			enableStartup = (overrides?.enableStartup ?? StartupEnabled),
			themeMode = (overrides?.themeMode ?? ThemeMode),
			viewMode = (overrides?.viewMode ?? ViewMode),
			tweetTemplates = (overrides?.tweetTemplates ?? tweetTemplates),
			activeTweetTemplate = (overrides?.activeTweetTemplate ?? ActiveTweetTemplate)
		};
	}

	public async Task refreshSettings()
	{
		try
		{
			AlpheratzSettingDto alpheratzSettingDto = await settingsService.GetSettingAsync().ConfigureAwait(continueOnCapturedContext: false);
			PhotoFolderPath = alpheratzSettingDto.photoFolderPath ?? string.Empty;
			SecondaryPhotoFolderPath = alpheratzSettingDto.secondaryPhotoFolderPath ?? string.Empty;
			StartupEnabled = alpheratzSettingDto.enableStartup == true;
			ThemeMode = alpheratzSettingDto.themeMode.GetValueOrDefault();
			ViewMode = alpheratzSettingDto.viewMode.GetValueOrDefault();
			ActiveTweetTemplate = alpheratzSettingDto.activeTweetTemplate ?? string.Empty;
			tweetTemplates.Clear();
			foreach (string item in alpheratzSettingDto.tweetTemplates ?? Array.Empty<string>())
			{
				tweetTemplates.Add(item);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsViewModel.refreshSettings: threw: {value}");
			throw;
		}
	}

	public async Task<string?> handleChooseFolderPathOnly()
	{
		try
		{
			return await dialogService.openDirectoryAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsViewModel.handleChooseFolderPathOnly: threw: {value}");
			return null;
		}
	}
}
