namespace Alpheratz.Core;

/// <summary>複数機能で共有する識別子、保存先の契約値、入力上限と単位。</summary>
public static class AppConstants
{
    public const string ApplicationName = "Alpheratz";
    public const string PublisherName = "CosmoArtsStore";
    public const string RegistryKeyPath = @"Software\CosmoArtsStore\Alpheratz";
    public const string PolarisRegistryKeyPath = @"Software\CosmoArtsStore\Polaris";
    public const string InstallLocationRegistryValue = "InstallLocation";
    public const string DataDirectoryName = "Data";
    public const string DatabaseDirectoryName = "db";
    public const string CacheDirectoryName = "cache";
    public const string LogDirectoryName = "logs";
    public const string LogFileName = "info.log";
    public const string DatabaseFileName = "Alpheratz.db";
    public const string SettingsFileName = "setting.json";
    public const int MaxTagLength = 25;
    /// <summary>投稿本文とテンプレート入力で共通の標準文字数上限。</summary>
    public const int StandardPostCharacterLimit = 280;
    public const int BytesPerKibibyte = 1024;
    public const int BytesPerMebibyte = BytesPerKibibyte * BytesPerKibibyte;
}
