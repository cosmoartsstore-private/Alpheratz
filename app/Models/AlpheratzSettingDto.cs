using System.Collections.Generic;
using System.Text.Json.Serialization;
using Alpheratz.Shared.Models;

namespace Alpheratz.Models;

public sealed record AlpheratzSettingDto
{
    [JsonPropertyName("photoFolderPath")]
    public string? photoFolderPath { get; init; }

    [JsonPropertyName("secondaryPhotoFolderPath")]
    public string? secondaryPhotoFolderPath { get; init; }

    [JsonPropertyName("enableStartup")]
    public bool? enableStartup { get; init; }

    [JsonPropertyName("themeMode")]
    public ThemeMode? themeMode { get; init; }

    [JsonPropertyName("viewMode")]
    public ViewMode? viewMode { get; init; }

    [JsonPropertyName("tweetTemplates")]
    public IReadOnlyList<string>? tweetTemplates { get; init; }

    [JsonPropertyName("activeTweetTemplate")]
    public string? activeTweetTemplate { get; init; }
}
