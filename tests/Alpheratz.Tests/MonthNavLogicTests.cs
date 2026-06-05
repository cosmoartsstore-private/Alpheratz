using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;

namespace Alpheratz.Tests;

/// <summary>
/// MonthNav から分離した年見出しと月ボタンの描画指示を検証するテスト。
///
/// MonthNav 本体は StackPanel へ Border/Button を生成し、テーマ Brush を解決する。
/// ここでは UI 要素を作らず、月グループの並びから年見出しを挿入する位置、月ラベル、
/// active 月のテーマキーとフォント太さが正しく決まることを確認する。
/// </summary>
public sealed class MonthNavLogicTests
{
    /// <summary>
    /// 先頭グループと年が変わったグループだけを年見出しの開始位置として扱うことを確認する。
    ///
    /// MonthNav は年月降順のグループをそのまま表示する。
    /// 同じ年の連続月では見出しを繰り返さず、別年に入った最初の月で新しい年見出しを出す。
    /// 範囲外 index は UI 構築側からは渡さないが、helper 単体では false として扱う。
    /// </summary>
    [Fact]
    public void IsYearStart_ReturnsTrueForFirstGroupAndYearBoundary()
    {
        var groups = SampleGroups();

        Assert.True(MonthNavLogic.IsYearStart(groups, 0));
        Assert.False(MonthNavLogic.IsYearStart(groups, 1));
        Assert.True(MonthNavLogic.IsYearStart(groups, 2));
        Assert.False(MonthNavLogic.IsYearStart(groups, -1));
        Assert.False(MonthNavLogic.IsYearStart(groups, 3));
    }

    /// <summary>
    /// グループ一覧から、年見出しと月ボタンが表示順どおりに生成されることを確認する。
    ///
    /// 2026 年の 6月・5月が続いた後に 2025 年 12月へ移る入力では、
    /// 2026 見出し、6月、5月、2025 見出し、12月の順になる。
    /// activeIndex=1 の 5月だけが active 表示になり、背景キーと太字が付く。
    /// </summary>
    [Fact]
    public void BuildRenderItems_InsertsYearHeadersAndMarksActiveMonth()
    {
        var items = MonthNavLogic.BuildRenderItems(SampleGroups(), activeIndex: 1);

        Assert.Collection(
            items,
            item => Assert.Equal(MonthNavRenderItem.YearHeader(2026), item),
            item => AssertMonthButton(item, "2026-06", "6月", isActive: false),
            item => AssertMonthButton(item, "2026-05", "5月", isActive: true),
            item => Assert.Equal(MonthNavRenderItem.YearHeader(2025), item),
            item => AssertMonthButton(item, "2025-12", "12月", isActive: false));
    }

    /// <summary>
    /// 月ボタンの active/通常状態から、ラベル、文字太さ、テーマリソースキーが選ばれることを確認する。
    ///
    /// 通常月は控えめな文字色で背景を透明にし、active 月だけ primary 文字色、primary soft 背景、
    /// ExtraBold の太さにする。ラベルは GalleryMonthGroup.Label ではなく Month 値から作るため、
    /// DB 側のラベル表記が変わってもナビゲーション表示は「N月」で安定する。
    /// </summary>
    [Fact]
    public void MonthButton_ReturnsDisplayKeysForActiveAndRestStates()
    {
        var group = new GalleryMonthGroup("2026-06", 2026, 6, "Jun", 0, 8);

        Assert.Equal(
            new MonthNavButtonDisplay("6月", MonthNavFontWeight.SemiBold, MonthNavLogic.RestMonthForegroundKey, null),
            MonthNavLogic.MonthButton(group, isActive: false));
        Assert.Equal(
            new MonthNavButtonDisplay("6月", MonthNavFontWeight.ExtraBold, MonthNavLogic.ActiveMonthForegroundKey, MonthNavLogic.ActiveMonthBackgroundKey),
            MonthNavLogic.MonthButton(group, isActive: true));
    }

    /// <summary>
    /// 月ボタン行の主要値を検証する。
    /// テスト本文では表示順を読みやすくするため、月ボタンの詳細比較をこの補助にまとめる。
    /// </summary>
    private static void AssertMonthButton(MonthNavRenderItem item, string key, string label, bool isActive)
    {
        Assert.Equal(MonthNavRenderKind.MonthButton, item.Kind);
        Assert.Equal(key, item.Group?.Key);
        Assert.Equal(label, item.Button?.Label);
        Assert.Equal(
            isActive ? MonthNavFontWeight.ExtraBold : MonthNavFontWeight.SemiBold,
            item.Button?.FontWeight);
        Assert.Equal(
            isActive ? MonthNavLogic.ActiveMonthForegroundKey : MonthNavLogic.RestMonthForegroundKey,
            item.Button?.ForegroundKey);
        Assert.Equal(
            isActive ? MonthNavLogic.ActiveMonthBackgroundKey : null,
            item.Button?.BackgroundKey);
    }

    /// <summary>
    /// 年境界を含む月グループのサンプルを返す。
    /// FirstIndex/Count は MonthNav の表示順テストでは直接使わないが、実データに近い値を入れる。
    /// </summary>
    private static IReadOnlyList<GalleryMonthGroup> SampleGroups() =>
    [
        new GalleryMonthGroup("2026-06", 2026, 6, "6月", 0, 10),
        new GalleryMonthGroup("2026-05", 2026, 5, "5月", 10, 12),
        new GalleryMonthGroup("2025-12", 2025, 12, "12月", 22, 4),
    ];
}
