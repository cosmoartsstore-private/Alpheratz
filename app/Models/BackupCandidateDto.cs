using System.Text.Json.Serialization;

namespace Alpheratz.Models;

public sealed record BackupCandidateDto
{
    [JsonPropertyName("photo_folder_path")]
    public string photo_folder_path { get; init; } = string.Empty;

    [JsonPropertyName("backup_folder_name")]
    public string backup_folder_name { get; init; } = string.Empty;

    [JsonPropertyName("backup_path")]
    public string backup_path { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string created_at { get; init; } = string.Empty;
}
