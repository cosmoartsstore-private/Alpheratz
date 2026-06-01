using System;
using System.IO;
using Microsoft.Win32;

namespace Alpheratz.Core;

public static class AppPaths
{
	private const string RegistryKeyPath = "Software\\CosmoArtsStore\\Alpheratz";

	private const string PolarisKeyPath = "Software\\CosmoArtsStore\\Polaris";

	public static string? InstallLocation { get; } = ResolveInstallLocation();

	private static string? ResolveInstallLocation()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\CosmoArtsStore\\Alpheratz");
			string text = registryKey?.GetValue("InstallLocation") as string;
			if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
			{
				return text;
			}
		}
		catch
		{
		}
		string directoryName = Path.GetDirectoryName(Environment.ProcessPath);
		if (directoryName != null && Directory.Exists(Path.Combine(directoryName, "Data")))
		{
			return directoryName;
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmoArtsStore", "Alpheratz");
	}

	public static string? GetDataDir()
	{
		return EnsureDir((InstallLocation == null) ? null : Path.Combine(InstallLocation, "Data"));
	}

	public static string? GetLogDir()
	{
		string dataDir = GetDataDir();
		return EnsureDir((dataDir != null) ? Path.Combine(dataDir, "logs") : null);
	}

	public static string? GetSettingDir()
	{
		string dataDir = GetDataDir();
		return EnsureDir((dataDir != null) ? Path.Combine(dataDir, "db") : null);
	}

	public static string? GetCacheDir()
	{
		string dataDir = GetDataDir();
		return EnsureDir((dataDir != null) ? Path.Combine(dataDir, "cache") : null);
	}

	public static string? GetDbDir()
	{
		string dataDir = GetDataDir();
		return EnsureDir((dataDir != null) ? Path.Combine(dataDir, "db") : null);
	}

	public static string? GetDbPath()
	{
		string dbDir = GetDbDir();
		if (dbDir == null)
		{
			return null;
		}
		return Path.Combine(dbDir, "Alpheratz.db");
	}

	public static string? GetImgCacheDir(long sourceSlot = 1L)
	{
		string path = ((sourceSlot == 2) ? "2nd-cache" : "1st-cache");
		string cacheDir = GetCacheDir();
		return EnsureDir((cacheDir != null) ? Path.Combine(cacheDir, path, "imgCache") : null);
	}

	public static string? GetPolarisArchiveDir()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\CosmoArtsStore\\Polaris");
			string text = registryKey?.GetValue("InstallLocation") as string;
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			string text2 = Path.Combine(text, "archive");
			return Directory.Exists(text2) ? text2 : null;
		}
		catch
		{
			return null;
		}
	}

	public static string NormalizePathForDb(string path)
	{
		return path.Replace('\\', '/');
	}

	private static string? EnsureDir(string? path)
	{
		if (path == null)
		{
			return null;
		}
		try
		{
			Directory.CreateDirectory(path);
			return path;
		}
		catch
		{
			return null;
		}
	}
}
