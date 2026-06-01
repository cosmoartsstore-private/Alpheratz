using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Alpheratz.Core;

public sealed class AppConfig
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly object _lock = new object();

	public AlpheratzSetting LoadSetting()
	{
		lock (_lock)
		{
			string settingDir = AppPaths.GetSettingDir();
			string text = ((settingDir != null) ? Path.Combine(settingDir, "setting.json") : null);
			if (text != null && File.Exists(text))
			{
				try
				{
					AlpheratzSetting alpheratzSetting = JsonSerializer.Deserialize<AlpheratzSetting>(File.ReadAllText(text, Encoding.UTF8));
					if (alpheratzSetting != null)
					{
						return alpheratzSetting;
					}
				}
				catch (Exception ex)
				{
					AppLogger.Warn("設定ファイルの読み込みに失敗しました: " + ex.Message);
				}
			}
			return new AlpheratzSetting();
		}
	}

	public void SaveSetting(AlpheratzSetting setting)
	{
		try
		{
			lock (_lock)
			{
				string path = Path.Combine(AppPaths.GetSettingDir() ?? throw new InvalidOperationException("設定ファイルの保存先を取得できません"), "setting.json");
				string contents = JsonSerializer.Serialize(setting, JsonOptions);
				File.WriteAllText(path, contents, Encoding.UTF8);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"AppConfig.SaveSetting: threw: {value}");
			throw;
		}
	}

	public (bool enabled, bool preferenceSet) GetStartupPreference()
	{
		AlpheratzSetting alpheratzSetting = LoadSetting();
		return (enabled: alpheratzSetting.EnableStartup, preferenceSet: alpheratzSetting.StartupPreferenceSet);
	}

	public void SaveStartupPreference(bool enabled)
	{
		try
		{
			AlpheratzSetting alpheratzSetting = LoadSetting();
			alpheratzSetting.EnableStartup = enabled;
			alpheratzSetting.StartupPreferenceSet = true;
			SaveSetting(alpheratzSetting);
			SetStartupEnabled(enabled);
		}
		catch (Exception value)
		{
			AppLogger.Error($"AppConfig.SaveStartupPreference: threw: {value}");
			throw;
		}
	}

	private static void SetStartupEnabled(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey != null)
			{
				if (enabled)
				{
					string text = Process.GetCurrentProcess().MainModule?.FileName ?? "";
					registryKey.SetValue("Alpheratz", "\"" + text + "\"");
				}
				else
				{
					registryKey.DeleteValue("Alpheratz", throwOnMissingValue: false);
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.Warn("自動起動の設定に失敗しました: " + ex.Message);
		}
	}
}
