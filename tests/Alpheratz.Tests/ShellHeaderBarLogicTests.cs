using Alpheratz.Features.Shell.Controls;
using Alpheratz.Shared.Models;
using Windows.System;

namespace Alpheratz.Tests;

/// <summary>
/// ShellHeaderBar の表示専用ロジックを検証するテスト。
///
/// ShellHeaderBar 本体は Button、AppIcon、ThemeResource を直接操作するが、
    /// トグル押下後の次モード、検索枠のテーマキーは UI 要素なしで決まる。
/// ここではそれらの小さな規則を固定し、HeaderBar の code-behind が表示反映だけを担えるようにする。
/// </summary>
public sealed class ShellHeaderBarLogicTests
{
    /// <summary>
    /// グループ化ボタンと表示モードボタンが、現在状態から次状態へトグルされることを確認する。
    ///
    /// グループ化は none/world の往復で、表示モードは ShellPage へ渡す文字列として
    /// standard/gallery の往復になる。
    /// </summary>
    [Fact]
    public void ToggleHelpers_ReturnNextGroupingAndViewModeValues()
    {
        Assert.Equal(GroupingMode.world, ShellHeaderBarLogic.NextGroupingMode(GroupingMode.none));
        Assert.Equal(GroupingMode.none, ShellHeaderBarLogic.NextGroupingMode(GroupingMode.world));
        Assert.Equal("gallery", ShellHeaderBarLogic.NextViewModeName(ViewMode.standard));
        Assert.Equal("standard", ShellHeaderBarLogic.NextViewModeName(ViewMode.gallery));
    }

    /// <summary>
    /// グループ化ボタンが world で active になり、gallery 表示中だけ disabled になることを確認する。
    ///
    /// masonry/gallery 表示はワールドグルーピング非対応なので、ボタン自体を無効化し opacity を落とす。
    /// ただし現在の grouping 値が world なら active 表示は維持し、状態と利用不可を別々に表す。
    /// </summary>
    [Fact]
    public void GroupingToggleState_DisablesGroupingInGalleryView()
    {
        Assert.Equal(new HeaderToggleState(false, true, 1.0, null),
            ShellHeaderBarLogic.GroupingToggleState(GroupingMode.none, ViewMode.standard));
        Assert.Equal(new HeaderToggleState(true, true, 1.0, null),
            ShellHeaderBarLogic.GroupingToggleState(GroupingMode.world, ViewMode.standard));
        Assert.Equal(new HeaderToggleState(true, false, 0.4, null),
            ShellHeaderBarLogic.GroupingToggleState(GroupingMode.world, ViewMode.gallery));
    }

    /// <summary>
    /// 表示モードボタンが現在モードを表すアイコン名と active 状態を返すことを確認する。
    ///
    /// standard では grid アイコンかつ通常色、gallery では gallery アイコンかつ active 色になる。
    /// </summary>
    [Fact]
    public void ViewModeToggleState_ReturnsIconNameAndActiveFlag()
    {
        Assert.Equal(new HeaderToggleState(false, true, 1.0, "grid"),
            ShellHeaderBarLogic.ViewModeToggleState(ViewMode.standard));
        Assert.Equal(new HeaderToggleState(true, true, 1.0, "gallery"),
            ShellHeaderBarLogic.ViewModeToggleState(ViewMode.gallery));
    }

    /// <summary>
    /// 検索ボックスのフォーカス状態から枠線・背景のテーマキーが選ばれることを確認する。
    ///
    /// focused ではアクセント寄りの枠と通常サーフェイス、blurred では通常枠と薄いサーフェイスへ戻す。
    /// </summary>
    [Fact]
    public void SearchBoxKeys_ReturnsFocusedAndRestThemeKeys()
    {
        Assert.Equal(new SearchBoxVisualKeys("ABorderStrong", "ASurface"), ShellHeaderBarLogic.SearchBoxKeys(true));
        Assert.Equal(new SearchBoxVisualKeys("ABorder", "ASurfaceSoft"), ShellHeaderBarLogic.SearchBoxKeys(false));
    }

    /// <summary>
    /// 検索ボックスでは Enter だけを即時検索として扱うことを確認する。
    /// 文字入力や矢印キーではバインディング確定や検索実行を行わない。
    /// </summary>
    [Fact]
    public void ShouldSubmitSearch_ReturnsTrueOnlyForEnter()
    {
        Assert.True(ShellHeaderBarLogic.ShouldSubmitSearch(VirtualKey.Enter));
        Assert.False(ShellHeaderBarLogic.ShouldSubmitSearch(VirtualKey.Escape));
        Assert.False(ShellHeaderBarLogic.ShouldSubmitSearch(VirtualKey.F));
    }
}
