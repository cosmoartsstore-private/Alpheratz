using System;
using System.Collections.Generic;
using System.Linq;
using Alpheratz.Shared.Models;
using Alpheratz.Models;
using Windows.System;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// ShellHeaderBar のトグル状態と表示テキストを UI 要素から切り離して扱う補助ロジック。
/// Button や Brush は扱わず、次のモードや表示キーだけを返す。
/// </summary>
internal static class ShellHeaderBarLogic
{
    /// <summary>グループ化ボタン押下後の次モードを返す。</summary>
    public static GroupingMode NextGroupingMode(GroupingMode current)
        => current == GroupingMode.world ? GroupingMode.none : GroupingMode.world;

    /// <summary>表示モードボタン押下後に ShellPage へ渡すモード文字列を返す。</summary>
    public static string NextViewModeName(ViewMode current)
        => current == ViewMode.gallery ? "standard" : "gallery";

    /// <summary>グループ化ボタンの active/disabled 表示状態を返す。</summary>
    public static HeaderToggleState GroupingToggleState(GroupingMode groupingMode, ViewMode viewMode)
    {
        var disabled = viewMode == ViewMode.gallery;
        return new HeaderToggleState(
            Active: groupingMode == GroupingMode.world,
            Enabled: !disabled,
            Opacity: disabled ? 0.4 : 1.0,
            IconName: null);
    }

    /// <summary>表示モードボタンの active 状態とアイコン名を返す。</summary>
    public static HeaderToggleState ViewModeToggleState(ViewMode viewMode)
    {
        var galleryActive = viewMode == ViewMode.gallery;
        return new HeaderToggleState(
            Active: galleryActive,
            Enabled: true,
            Opacity: 1.0,
            IconName: galleryActive ? "gallery" : "grid");
    }

    /// <summary>検索ボックス枠のテーマリソースキーをフォーカス状態から返す。</summary>
    public static SearchBoxVisualKeys SearchBoxKeys(bool focused)
        => focused
            ? new SearchBoxVisualKeys("ABorderStrong", "ASurface")
            : new SearchBoxVisualKeys("ABorder", "ASurfaceSoft");

    /// <summary>検索ボックスのキー入力が検索実行かを返す。</summary>
    public static bool ShouldSubmitSearch(VirtualKey key) => key == VirtualKey.Enter;

    /// <summary>候補リストを出す入力かを返す。空白や記号だけの入力では候補を出さない。</summary>
    public static bool ShouldShowWorldSuggestions(string? query)
        => (query ?? string.Empty).Trim().Any(char.IsLetterOrDigit);

    /// <summary>ヘッダー検索用のワールド名候補を、現在の入力に部分一致するものから最大件数だけ返す。</summary>
    public static IReadOnlyList<HeaderWorldSuggestion> BuildWorldNameSuggestions(
        IEnumerable<WorldFilterOptionDto> worlds,
        string? query,
        int maxCount = 5)
    {
        if (maxCount <= 0) return [];

        var trimmed = query?.Trim() ?? string.Empty;
        if (!ShouldShowWorldSuggestions(trimmed)) return [];

        return worlds
            .Where(world => !string.IsNullOrWhiteSpace(world.world_name))
            .Where(world => world.world_name!.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(maxCount)
            .Select(world => new HeaderWorldSuggestion(world.world_name!, $"{world.count}枚"))
            .ToList();
    }
}

/// <summary>ヘッダーのトグルボタン表示状態。</summary>
internal sealed record HeaderToggleState(bool Active, bool Enabled, double Opacity, string? IconName);

/// <summary>検索ボックスの枠線と背景に使うテーマリソースキー。</summary>
internal sealed record SearchBoxVisualKeys(string BorderKey, string FillKey);

/// <summary>ヘッダー検索のワールド名候補表示。</summary>
public sealed class HeaderWorldSuggestion
{
    public HeaderWorldSuggestion() { }

    public HeaderWorldSuggestion(string displayName, string countText)
    {
        DisplayName = displayName;
        CountText = countText;
    }

    public string DisplayName { get; set; } = string.Empty;
    public string CountText { get; set; } = string.Empty;
}
