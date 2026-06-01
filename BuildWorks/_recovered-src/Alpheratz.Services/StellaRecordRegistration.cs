using System;
using System.IO;
using Alpheratz.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Alpheratz.Services;

internal static class StellaRecordRegistration
{
	private const string AppName = "Alpheratz v2";

	private const string AppDescription = "VRChatフレンド情報・ログ閲覧（新版）";

	private const string RegKeyPath = "Software\\CosmoArtsStore\\StellaRecord";

	private const string CreateTableSql = "CREATE TABLE IF NOT EXISTS apps (\n    id              INTEGER PRIMARY KEY AUTOINCREMENT,\n    name            TEXT NOT NULL,\n    description     TEXT NOT NULL DEFAULT '',\n    path            TEXT NOT NULL UNIQUE,\n    icon            BLOB,\n    registered_at   DATETIME DEFAULT (datetime('now', 'localtime'))\n);";

	public static void Register(string exePath, string? iconPath = null)
	{
		try
		{
			string dbPath = GetDbPath();
			if (dbPath == null)
			{
				return;
			}
			byte[] array = null;
			if (iconPath != null && File.Exists(iconPath))
			{
				array = File.ReadAllBytes(iconPath);
			}
			using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + dbPath);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "CREATE TABLE IF NOT EXISTS apps (\n    id              INTEGER PRIMARY KEY AUTOINCREMENT,\n    name            TEXT NOT NULL,\n    description     TEXT NOT NULL DEFAULT '',\n    path            TEXT NOT NULL UNIQUE,\n    icon            BLOB,\n    registered_at   DATETIME DEFAULT (datetime('now', 'localtime'))\n);";
			sqliteCommand.ExecuteNonQuery();
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "INSERT INTO apps (name, description, path, icon)\nVALUES ($name, $description, $path, $icon)\nON CONFLICT(path) DO UPDATE SET\n    name = excluded.name,\n    description = excluded.description,\n    icon = excluded.icon";
			sqliteCommand2.Parameters.AddWithValue("$name", "Alpheratz v2");
			sqliteCommand2.Parameters.AddWithValue("$description", "VRChatフレンド情報・ログ閲覧（新版）");
			sqliteCommand2.Parameters.AddWithValue("$path", exePath);
			sqliteCommand2.Parameters.AddWithValue("$icon", ((object)array) ?? ((object)DBNull.Value));
			sqliteCommand2.ExecuteNonQuery();
		}
		catch (Exception value)
		{
			AppLogger.Error($"StellaRecordRegistration.Register failed: {value}");
		}
	}

	public static void Unregister(string? exePath = null)
	{
		try
		{
			string dbPath = GetDbPath();
			if (dbPath == null || !File.Exists(dbPath))
			{
				return;
			}
			using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + dbPath);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			if (exePath != null)
			{
				sqliteCommand.CommandText = "DELETE FROM apps WHERE path = $path";
				sqliteCommand.Parameters.AddWithValue("$path", exePath);
			}
			else
			{
				sqliteCommand.CommandText = "DELETE FROM apps WHERE name = $name";
				sqliteCommand.Parameters.AddWithValue("$name", "Alpheratz v2");
			}
			sqliteCommand.ExecuteNonQuery();
		}
		catch (Exception value)
		{
			AppLogger.Error($"StellaRecordRegistration.Unregister failed: {value}");
		}
	}

	public static bool IsStellaRecordAvailable()
	{
		string dbPath = GetDbPath();
		if (dbPath != null)
		{
			return File.Exists(dbPath);
		}
		return false;
	}

	private static string? GetDbPath()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\CosmoArtsStore\\StellaRecord");
		return registryKey?.GetValue("DbPath") as string;
	}
}
