using System.Collections.Generic;
using System.Text.Json.Serialization;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Core;

public sealed class AlpheratzSetting
{
    [JsonPropertyName("photoFolderPath")]
    public string PhotoFolderPath { get; set; } = string.Empty;

    [JsonPropertyName("secondaryPhotoFolderPath")]
    public string SecondaryPhotoFolderPath { get; set; } = string.Empty;

    [JsonPropertyName("themeMode")]
    public string ThemeMode { get; set; } = "light";

    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "standard";

    [JsonPropertyName("enableStartup")]
    public bool EnableStartup { get; set; }

    [JsonPropertyName("startupPreferenceSet")]
    public bool StartupPreferenceSet { get; set; }

    [JsonPropertyName("openWorldLinkOnPost")]
    public bool OpenWorldLinkOnPost { get; set; }

    [JsonPropertyName("tweetTemplates")]
    public List<string> TweetTemplates { get; set; } =
    [
        getMsg("AlpheratzSetting.defaultTweetTemplateGreeting"),
        getMsg("AlpheratzSetting.defaultTweetTemplateWorld"),
        getMsg("AlpheratzSetting.defaultTweetTemplatePhotography"),
    ];

    [JsonPropertyName("activeTweetTemplate")]
    public string ActiveTweetTemplate { get; set; } = getMsg("AlpheratzSetting.defaultTweetTemplateGreeting");

    /// <summary>
    /// フォルダ設定の保存後、対象スロットの旧 DB／キャッシュをまだ整理中であることを示す。
    /// 処理途中で終了しても次回起動時に同じ整理を再実行できるよう、設定と同時に永続化する。
    /// </summary>
    [JsonPropertyName("pendingFolderCleanup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PendingFolderCleanupSetting? PendingFolderCleanup { get; set; }
}

/// <summary>フォルダ変更・リセット後に再実行可能な旧データ整理要求。</summary>
public sealed class PendingFolderCleanupSetting
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyName("sourceSlot")]
    public int SourceSlot { get; set; }

    [JsonPropertyName("committedPath")]
    public string CommittedPath { get; set; } = string.Empty;
}
