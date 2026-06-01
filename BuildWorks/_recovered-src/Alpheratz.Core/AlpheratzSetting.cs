using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
	public List<string> TweetTemplates { get; set; }

	[JsonPropertyName("activeTweetTemplate")]
	public string ActiveTweetTemplate { get; set; }

	public AlpheratzSetting()
	{
		int num = 3;
		List<string> list = new List<string>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<string> span = CollectionsMarshal.AsSpan(list);
		span[0] = "おは{world-name}\n\n#{タグを追加}";
		span[1] = "World: {world-name}\nAuthor:\n\n#VRChat_world紹介";
		span[2] = "World: {world-name}\nAuthor:\nCloth:\n\n#VRChatPhotography";
		TweetTemplates = list;
		ActiveTweetTemplate = "おは{world-name}\n\n#{タグを追加}";
		base._002Ector();
	}
}
