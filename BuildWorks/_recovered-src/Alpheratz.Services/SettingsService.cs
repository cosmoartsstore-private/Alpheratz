using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models;
using Alpheratz.Shared.Models;

namespace Alpheratz.Services;

public sealed class SettingsService
{
	private readonly AppConfig _config;

	private readonly object _writeGate = new object();

	public SettingsService(AppConfig config)
	{
		_config = config;
	}

	public Task<AlpheratzSettingDto> GetSettingAsync(CancellationToken ct = default(CancellationToken))
	{
		try
		{
			AlpheratzSettingDto result;
			lock (_writeGate)
			{
				AlpheratzSetting alpheratzSetting = _config.LoadSetting();
				AlpheratzSettingDto obj = new AlpheratzSettingDto
				{
					photoFolderPath = alpheratzSetting.PhotoFolderPath,
					secondaryPhotoFolderPath = alpheratzSetting.SecondaryPhotoFolderPath
				};
				ThemeMode value = ((alpheratzSetting.ThemeMode == "dark") ? ThemeMode.dark : ThemeMode.light);
				obj.themeMode = value;
				ViewMode value2 = ((alpheratzSetting.ViewMode == "gallery") ? ViewMode.gallery : ViewMode.standard);
				obj.viewMode = value2;
				obj.enableStartup = alpheratzSetting.EnableStartup;
				obj.tweetTemplates = ((alpheratzSetting.TweetTemplates == null) ? null : new List<string>(alpheratzSetting.TweetTemplates));
				obj.activeTweetTemplate = alpheratzSetting.ActiveTweetTemplate;
				result = obj;
			}
			return Task.FromResult(result);
		}
		catch (Exception value3)
		{
			AppLogger.Error($"SettingsService.GetSettingAsync: threw: {value3}");
			throw;
		}
	}

	public Task SaveSettingAsync(AlpheratzSettingDto dto, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			lock (_writeGate)
			{
				AlpheratzSetting alpheratzSetting = _config.LoadSetting();
				alpheratzSetting.PhotoFolderPath = dto.photoFolderPath ?? alpheratzSetting.PhotoFolderPath;
				alpheratzSetting.SecondaryPhotoFolderPath = dto.secondaryPhotoFolderPath ?? alpheratzSetting.SecondaryPhotoFolderPath;
				if (dto.themeMode.HasValue)
				{
					alpheratzSetting.ThemeMode = dto.themeMode.Value.ToString();
				}
				if (dto.viewMode.HasValue)
				{
					alpheratzSetting.ViewMode = dto.viewMode.Value.ToString();
				}
				if (dto.enableStartup.HasValue)
				{
					alpheratzSetting.EnableStartup = dto.enableStartup.Value;
				}
				if (dto.tweetTemplates != null)
				{
					alpheratzSetting.TweetTemplates = new List<string>(dto.tweetTemplates);
				}
				if (dto.activeTweetTemplate != null)
				{
					alpheratzSetting.ActiveTweetTemplate = dto.activeTweetTemplate;
				}
				_config.SaveSetting(alpheratzSetting);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsService.SaveSettingAsync: threw: {value}");
			throw;
		}
		return Task.CompletedTask;
	}

	public Task SaveStartupPreferenceAsync(bool enabled, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			lock (_writeGate)
			{
				_config.SaveStartupPreference(enabled);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"SettingsService.SaveStartupPreferenceAsync: threw: {value}");
			throw;
		}
		return Task.CompletedTask;
	}
}
