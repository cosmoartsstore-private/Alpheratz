using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// 検索欄の key:value コマンドを抽出し、残りを通常検索語として返す。
/// 例: since:2025-01-01、is:fav、tag:"blue sky"。
/// </summary>
public static class SearchCommandParser
{
    // 値は空白なし、または二重引用符で囲んだ文字列を受け付ける。
    private static readonly Regex CommandPattern = new(
        @"(?<key>[a-zA-Z]+):(?:""(?<qval>[^""]*)""|(?<val>\S+))",
        RegexOptions.Compiled);

    /// <summary>検索文字列を構造化コマンドと通常検索語に分ける。</summary>
    public static SearchCommandResult Parse(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return new SearchCommandResult(plainText: string.Empty);
        }

        var result = new SearchCommandResult();
        var plainParts = new List<string>();
        var lastIndex = 0;

        foreach (Match match in CommandPattern.Matches(rawQuery))
        {
            // コマンドの前にある通常検索語を保持する。
            if (match.Index > lastIndex)
            {
                var before = rawQuery.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrWhiteSpace(before))
                {
                    plainParts.Add(before.Trim());
                }
            }

            var key = match.Groups["key"].Value.ToLowerInvariant();
            var value = match.Groups["qval"].Success
                ? match.Groups["qval"].Value
                : match.Groups["val"].Value;

            var consumed = tryApplyCommand(key, value, result);
            if (!consumed)
            {
                // 不明または不正なコマンドは検索語として残す。
                plainParts.Add(match.Value);
            }

            lastIndex = match.Index + match.Length;
        }

        // 最後のコマンドより後ろにある通常検索語を保持する。
        if (lastIndex < rawQuery.Length)
        {
            var trailing = rawQuery.Substring(lastIndex);
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                plainParts.Add(trailing.Trim());
            }
        }

        result.PlainText = string.Join(" ", plainParts);
        return result;
    }

    /// <summary>認識済みコマンドなら結果へ反映し、消費できたかを返す。</summary>
    private static bool tryApplyCommand(string key, string value, SearchCommandResult result)
    {
        switch (key)
        {
            case "since":
                if (isValidDate(value))
                {
                    result.DateFrom = value;
                    return true;
                }
                return false;

            case "until":
                if (isValidDate(value))
                {
                    result.DateTo = value;
                    return true;
                }
                return false;

            case "orientation":
                var orientation = value.ToLowerInvariant();
                if (orientation is "portrait" or "landscape")
                {
                    result.OrientationFilter = orientation;
                    return true;
                }
                return false;

            case "is":
                var flag = value.ToLowerInvariant();
                if (flag is "favorite" or "fav")
                {
                    result.FavoritesOnly = true;
                    return true;
                }
                return false;

            case "tag":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Tags.Add(value);
                    return true;
                }
                return false;

            case "folder":
                var folder = value.ToLowerInvariant();
                if (folder is "primary" or "secondary")
                {
                    result.FolderMode = folder;
                    return true;
                }
                return false;

            case "sort":
                var sort = value.ToLowerInvariant();
                if (sort is "date" or "world")
                {
                    result.SortMode = sort;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>検索コマンドで許可する yyyy-MM-dd 形式の日付か判定する。</summary>
    private static bool isValidDate(string value)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }
}

/// <summary>
/// 検索コマンドの解析結果。
/// null または空の値は、そのコマンドが入力に含まれていないことを示す。
/// </summary>
public class SearchCommandResult
{
    public string PlainText { get; set; } = string.Empty;
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? OrientationFilter { get; set; }
    public bool? FavoritesOnly { get; set; }
    public List<string> Tags { get; } = [];
    public string? FolderMode { get; set; }
    public string? SortMode { get; set; }

    /// <summary>空の解析結果を作成する。</summary>
    public SearchCommandResult() { }

    /// <summary>プレーン検索文字列だけを持つ解析結果を作成する。</summary>
    public SearchCommandResult(string plainText)
    {
        PlainText = plainText;
    }

    /// <summary>1つ以上のコマンドが解析されたかを返す。</summary>
    public bool HasCommands =>
        DateFrom is not null ||
        DateTo is not null ||
        OrientationFilter is not null ||
        FavoritesOnly is not null ||
        Tags.Count > 0 ||
        FolderMode is not null ||
        SortMode is not null;
}
