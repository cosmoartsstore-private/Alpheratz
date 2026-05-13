using System.Collections.Generic;
using System.Text.Json.Serialization;

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

    [JsonPropertyName("tweetTemplates")]
    public List<string> TweetTemplates { get; set; } =
    [
        "おは{world-name}\n\n#{タグを追加}",
        "World: {world-name}\nAuthor:\n\n#VRChat_world紹介",
        "World: {world-name}\nAuthor:\nCloth:\n\n#VRChatPhotography",
    ];

    [JsonPropertyName("activeTweetTemplate")]
    public string ActiveTweetTemplate { get; set; } = "おは{world-name}\n\n#{タグを追加}";
}
