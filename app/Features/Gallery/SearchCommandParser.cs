using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// Twitter-style search command parser.
/// Extracts structured filter commands (e.g. "since:2025-01-01", "is:fav") from a raw
/// search query string and returns the remaining plain text for free-text search.
/// </summary>
public static class SearchCommandParser
{
    // Matches word:value tokens. Value can be unquoted (no spaces) or quoted with double quotes.
    private static readonly Regex CommandPattern = new(
        @"(?<key>[a-zA-Z]+):(?:""(?<qval>[^""]*)""|(?<val>\S+))",
        RegexOptions.Compiled);

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
            // Collect any text before this command token
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
                // Unknown command: keep as plain text
                plainParts.Add(match.Value);
            }

            lastIndex = match.Index + match.Length;
        }

        // Collect any trailing text
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
/// Result of parsing search commands from a raw query string.
/// Null/empty fields mean the command was not present in the query.
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

    public SearchCommandResult() { }

    public SearchCommandResult(string plainText)
    {
        PlainText = plainText;
    }

    /// <summary>
    /// Returns true if any command was parsed (i.e. any field is non-null/non-default).
    /// </summary>
    public bool HasCommands =>
        DateFrom is not null ||
        DateTo is not null ||
        OrientationFilter is not null ||
        FavoritesOnly is not null ||
        Tags.Count > 0 ||
        FolderMode is not null ||
        SortMode is not null;
}
