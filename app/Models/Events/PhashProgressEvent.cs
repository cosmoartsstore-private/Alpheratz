using System.Text.Json.Serialization;

namespace Alpheratz.Models.Events;

/// <summary>
/// PDQ ハッシュ計算ワーカーの進捗イベントペイロード。
/// done/total はそれぞれ完了済み/全体件数、current は処理中の写真ファイル名（任意）。
/// LocalEventBus で "phash:progress" として発行される。
/// </summary>
public sealed record PhashProgressEvent
{
    [JsonPropertyName("done")]
    public int done { get; init; }

    [JsonPropertyName("total")]
    public int total { get; init; }

    [JsonPropertyName("current")]
    public string? current { get; init; }

    public static PhashProgressEvent Empty { get; } = new()
    {
        done = 0,
        total = 0,
        current = null,
    };
}
