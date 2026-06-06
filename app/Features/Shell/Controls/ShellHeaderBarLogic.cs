using Alpheratz.Shared.Models;
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
}

/// <summary>ヘッダーのトグルボタン表示状態。</summary>
internal sealed record HeaderToggleState(bool Active, bool Enabled, double Opacity, string? IconName);

/// <summary>検索ボックスの枠線と背景に使うテーマリソースキー。</summary>
internal sealed record SearchBoxVisualKeys(string BorderKey, string FillKey);
