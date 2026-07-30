using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Alpheratz.Messages;

/// <summary>UTF-8 の properties ファイルを正本として、利用者向け文言を解決する。</summary>
public static class MessageCatalog
{
    private const string ResourceName = "Alpheratz.Messages.messages.ja.properties";
    private const string LiteralOpenBrace = "\uE000";
    private const string LiteralCloseBrace = "\uE001";

    private static readonly Regex MessageKeyPattern = new(
        @"^[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+$",
        RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderPattern = new(
        @"\{([A-Za-z][A-Za-z0-9_]*)\}",
        RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Messages = new(
        LoadMessages,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>文言キーを解決する。</summary>
    public static string getMsg(string key) => ResolveMessage(key, []);

    /// <summary>文言キーを解決し、<c>{name}</c> 形式の変数を埋め込む。</summary>
    public static string getMsg(string key, params (string Name, object? Value)[] parameters)
        => ResolveMessage(key, parameters);

    private static IReadOnlyDictionary<string, string> LoadMessages()
    {
        using var stream = typeof(MessageCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋め込み文言リソース「{ResourceName}」が見つかりません。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ParseMessages(reader.ReadToEnd());
    }

    private static IReadOnlyDictionary<string, string> ParseMessages(string source)
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].TrimStart();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!')) continue;

            var separatorIndex = FindSeparator(line);
            if (separatorIndex < 1)
                throw new InvalidOperationException($"messages.ja.properties:{lineIndex + 1} の形式が不正です。");

            var key = line[..separatorIndex].Trim();
            if (!MessageKeyPattern.IsMatch(key))
                throw new InvalidOperationException($"messages.ja.properties:{lineIndex + 1} のキー「{key}」が不正です。");
            if (!messages.TryAdd(key, UnescapePropertyValue(line[(separatorIndex + 1)..])))
                throw new InvalidOperationException($"messages.ja.properties:{lineIndex + 1} のキー「{key}」が重複しています。");
        }

        return new ReadOnlyDictionary<string, string>(messages);
    }

    private static int FindSeparator(string line)
    {
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '=' or ':') return index;
        }

        return -1;
    }

    private static string UnescapePropertyValue(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index + 1 >= value.Length)
            {
                result.Append(character);
                continue;
            }

            var escaped = value[index + 1];
            switch (escaped)
            {
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case '\\': result.Append('\\'); break;
                case '=': result.Append('='); break;
                case ':': result.Append(':'); break;
                default:
                    result.Append(character);
                    continue;
            }

            index++;
        }

        return result.ToString();
    }

    private static string ResolveMessage(string key, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        if (!Messages.Value.TryGetValue(key, out var template))
            throw new KeyNotFoundException($"文言キー「{key}」が定義されていません。");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (!values.TryAdd(parameter.Name, parameter.Value))
                throw new ArgumentException($"文言キー「{key}」の変数「{parameter.Name}」が重複しています。", nameof(parameters));
        }

        var escapedTemplate = template
            .Replace("{{", LiteralOpenBrace, StringComparison.Ordinal)
            .Replace("}}", LiteralCloseBrace, StringComparison.Ordinal);
        var resolved = PlaceholderPattern.Replace(escapedTemplate, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
                throw new ArgumentException($"文言キー「{key}」の変数「{name}」が指定されていません。", nameof(parameters));
            return Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        });

        return resolved
            .Replace(LiteralOpenBrace, "{", StringComparison.Ordinal)
            .Replace(LiteralCloseBrace, "}", StringComparison.Ordinal);
    }
}
