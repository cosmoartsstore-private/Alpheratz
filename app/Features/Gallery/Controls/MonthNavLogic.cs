using System.Collections.Generic;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Gallery.Controls;

/// <summary>
/// MonthNav の年見出し挿入位置と月ボタン表示を UI 要素なしで決める補助ロジック。
/// Button や Brush は扱わず、StackPanel へ追加する行の種類とテーマキーだけを返す。
/// </summary>
internal static class MonthNavLogic
{
    public const string YearHeaderForegroundKey = "ATextFaint";
    public const string ActiveMonthForegroundKey = "ATextOnPrimary";
    public const string RestMonthForegroundKey = "ATextDim";
    public const string ActiveMonthBackgroundKey = "APrimary";

    /// <summary>月グループ一覧と active index から、年見出しと月ボタンの描画行を返す。</summary>
    public static IReadOnlyList<MonthNavRenderItem> BuildRenderItems(IReadOnlyList<GalleryMonthGroup> groups, int activeIndex)
    {
        var items = new List<MonthNavRenderItem>();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (IsYearStart(groups, i))
            {
                items.Add(MonthNavRenderItem.YearHeader(group.Year));
            }
            items.Add(MonthNavRenderItem.MonthButton(group, MonthButton(group, i == activeIndex)));
        }
        return items;
    }

    /// <summary>指定 index のグループが年見出しを出す先頭月かを返す。</summary>
    public static bool IsYearStart(IReadOnlyList<GalleryMonthGroup> groups, int index)
        => index >= 0
            && index < groups.Count
            && (index == 0 || groups[index - 1].Year != groups[index].Year);

    /// <summary>月グループと active 状態から、月ボタンのラベルとテーマキーを返す。</summary>
    public static MonthNavButtonDisplay MonthButton(GalleryMonthGroup group, bool isActive)
        => new MonthNavButtonDisplay(
            Label: getMsg("MonthNav.monthLabel", ("month", group.Month)),
            FontWeight: isActive ? MonthNavFontWeight.ExtraBold : MonthNavFontWeight.SemiBold,
            ForegroundKey: isActive ? ActiveMonthForegroundKey : RestMonthForegroundKey,
            BackgroundKey: isActive ? ActiveMonthBackgroundKey : null);
}

/// <summary>MonthNav の StackPanel に追加する行の種類。</summary>
internal enum MonthNavRenderKind
{
    YearHeader,
    MonthButton,
}

/// <summary>月ボタンの太さ指定。UI 側で Microsoft.UI.Text.FontWeights へ変換する。</summary>
internal enum MonthNavFontWeight
{
    SemiBold,
    ExtraBold,
}

/// <summary>MonthNav の 1 行分の描画指示。</summary>
internal sealed record MonthNavRenderItem(
    MonthNavRenderKind Kind,
    int? Year,
    GalleryMonthGroup? Group,
    MonthNavButtonDisplay? Button)
{
    /// <summary>年見出し行を作る。</summary>
    public static MonthNavRenderItem YearHeader(int year)
        => new(MonthNavRenderKind.YearHeader, year, null, null);

    /// <summary>月ボタン行を作る。</summary>
    public static MonthNavRenderItem MonthButton(GalleryMonthGroup group, MonthNavButtonDisplay display)
        => new(MonthNavRenderKind.MonthButton, null, group, display);
}

/// <summary>月ボタンに適用するラベル、太さ、テーマリソースキー。</summary>
internal sealed record MonthNavButtonDisplay(
    string Label,
    MonthNavFontWeight FontWeight,
    string ForegroundKey,
    string? BackgroundKey);
