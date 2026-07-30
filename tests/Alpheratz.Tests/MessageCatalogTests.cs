using Alpheratz.Messages;
using System.Text.RegularExpressions;

namespace Alpheratz.Tests;

/// <summary>properties ファイルを正本とする利用者向け文言の解決契約を検証する。</summary>
public sealed class MessageCatalogTests
{
    private static readonly Regex MessageDefinitionPattern = new(
        @"^(?<key>[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+)\s*[=:]",
        RegexOptions.CultureInvariant);

    private static readonly Regex LiteralMessageCallPattern = new(
        "\\bgetMsg\\s*\\(\\s*['\"](?<key>[A-Za-z][A-Za-z0-9]*(?:\\.[A-Za-z][A-Za-z0-9]*)+)['\"]",
        RegexOptions.CultureInvariant);

    [Fact]
    public void GetMsg_ReturnsKnownMessage()
    {
        Assert.Equal("キャンセル", MessageCatalog.getMsg("common.cancel"));
    }

    [Fact]
    public void GetMsg_ReplacesNamedVariables()
    {
        var message = MessageCatalog.getMsg(
            "WorldResolveViewModel.searchProgress",
            ("processed", 2),
            ("total", 5));

        Assert.Equal("2/5件", message);
    }

    [Fact]
    public void GetMsg_PreservesDoubleBracesAsLiteralBraces()
    {
        Assert.Equal("{world-name}", MessageCatalog.getMsg("SettingsPage.worldNameToken"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenKeyIsMissing()
    {
        Assert.Throws<KeyNotFoundException>(() => MessageCatalog.getMsg("Missing.messageKey"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenRequiredVariableIsMissing()
    {
        Assert.Throws<ArgumentException>(() =>
            MessageCatalog.getMsg("PhotoGridItem.groupCount"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenVariableIsSpecifiedMoreThanOnce()
    {
        Assert.Throws<ArgumentException>(() => MessageCatalog.getMsg(
            "PhotoGridItem.groupCount",
            ("count", 1),
            ("count", 2)));
    }

    /// <summary>ソース内で直接指定した文言キーが properties にすべて定義されていることを確認する。</summary>
    [Fact]
    public void LiteralGetMsgKeys_AreDefinedInJapaneseProperties()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "app");
        var propertiesPath = Path.Combine(appRoot, "Messages", "messages.ja.properties");
        var definedKeys = File.ReadLines(propertiesPath)
            .Select(line => MessageDefinitionPattern.Match(line.TrimStart()))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var usedKeys = Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => LiteralMessageCallPattern.Matches(File.ReadAllText(path)).Select(match => match.Groups["key"].Value))
            .ToHashSet(StringComparer.Ordinal);
        var undefinedKeys = usedKeys.Except(definedKeys, StringComparer.Ordinal).Order().ToArray();

        Assert.True(
            undefinedKeys.Length == 0,
            $"properties に未定義の文言キーがあります: {string.Join(", ", undefinedKeys)}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "app", "Alpheratz.Frontend.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Alpheratz.Frontend.csproj を含むリポジトリルートが見つかりません。");
    }
}
