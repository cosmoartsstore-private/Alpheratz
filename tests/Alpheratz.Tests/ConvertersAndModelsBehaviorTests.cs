using Alpheratz.Shared.Converters;
using Alpheratz.Shared.Icons;
using Alpheratz.Shared.Models;
using Alpheratz.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Tests;

/// <summary>
/// XAML バインディングコンバータと小さなモデル拡張の振る舞いを固定するテスト。
///
/// これらは画面の見た目を支える薄い部品だが、XAML から文字列 parameter や object 値として呼ばれるため、
/// コンパイル時には誤用を検出しにくい。
/// ここでは WinUI の画面を起動せずに、純粋な変換結果だけを確認する。
/// </summary>
public sealed class ConvertersAndModelsBehaviorTests
{
    /// <summary>
    /// BoolToVisibilityConverter が bool と invert parameter を正しく扱うことを確認する。
    ///
    /// XAML では true/false を Visibility に直接バインドできないため、この converter が表示可否の境界になる。
    /// bool 以外の値は false と同等に扱う現在仕様も、想定外入力で UI が表示されっぱなしにならないために固定する。
    /// </summary>
    [Fact]
    public void BoolToVisibilityConverter_MapsBooleanValuesAndInvertParameter()
    {
        var converter = new BoolToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert(true, typeof(Visibility), "", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert(false, typeof(Visibility), "", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert(true, typeof(Visibility), "invert", ""));
        Assert.Equal(Visibility.Visible, converter.Convert(false, typeof(Visibility), "invert", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert("true", typeof(Visibility), "", ""));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(Visibility.Visible, typeof(bool), "", ""));
    }

    /// <summary>
    /// IntToVisibilityConverter が正の数だけを Visible とし、invert parameter で反転することを確認する。
    ///
    /// 件数バッジや選択数表示では 0 件を非表示にする場面が多い。
    /// 0 と負数を同じ Collapsed とし、正数だけを表示する現在仕様を明示的に検証する。
    /// </summary>
    [Fact]
    public void IntToVisibilityConverter_MapsPositiveCountsAndInvertParameter()
    {
        var converter = new IntToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert(3, typeof(Visibility), "", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert(0, typeof(Visibility), "", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert(-1, typeof(Visibility), "", ""));
        Assert.Equal(Visibility.Collapsed, converter.Convert(3, typeof(Visibility), "invert", ""));
        Assert.Equal(Visibility.Visible, converter.Convert(0, typeof(Visibility), "invert", ""));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(Visibility.Visible, typeof(int), "", ""));
    }

    /// <summary>
    /// MatchPercentConverter が PDQ ハミング距離を一致率表示へ変換することを確認する。
    ///
    /// 類似写真候補の UI は距離そのものではなく「一致率：xx%」を表示する。
    /// 距離 0、半分程度、最大距離、範囲外、null の代表値を確認し、丸めと上下限クランプを固定する。
    /// </summary>
    [Fact]
    public void MatchPercentConverter_MapsPdqDistanceToClampedPercentText()
    {
        var converter = new MatchPercentConverter();

        Assert.Equal(MessageCatalog.getMsg("MatchPercentConverter.value", ("percent", 100)), converter.Convert(0, typeof(string), "", ""));
        Assert.Equal(MessageCatalog.getMsg("MatchPercentConverter.value", ("percent", 52)), converter.Convert(124, typeof(string), "", ""));
        Assert.Equal(MessageCatalog.getMsg("MatchPercentConverter.value", ("percent", 0)), converter.Convert(256, typeof(string), "", ""));
        Assert.Equal(MessageCatalog.getMsg("MatchPercentConverter.value", ("percent", 0)), converter.Convert(999, typeof(string), "", ""));
        Assert.Equal(MessageCatalog.getMsg("MatchPercentConverter.empty"), converter.Convert(null!, typeof(string), "", ""));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("一致率：100%", typeof(int), "", ""));
    }

    /// <summary>
    /// ToastIconConverter が ToastType を短い記号へ変換することを確認する。
    ///
    /// Toast のアクセントブラシ変換は Application.Resources に依存するため、このテストでは扱わない。
    /// アイコン変換は純粋な switch なので、success/error/info と想定外入力のフォールバックを検証する。
    /// </summary>
    [Fact]
    public void ToastIconConverter_MapsToastTypesToSymbols()
    {
        var converter = new ToastIconConverter();

        Assert.Equal("✔", converter.Convert(ToastType.success, typeof(string), "", ""));
        Assert.Equal("⚠", converter.Convert(ToastType.error, typeof(string), "", ""));
        Assert.Equal("ℹ", converter.Convert(ToastType.info, typeof(string), "", ""));
        Assert.Equal("ℹ", converter.Convert("unknown", typeof(string), "", ""));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("✔", typeof(ToastType), "", ""));
    }

    /// <summary>
    /// ToastStyleLogic が ToastType からテーマブラシキーと記号を選択することを確認する。
    ///
    /// 実際の Brush 解決は Application.Resources に依存するため、このテストでは扱わない。
    /// success/error/info と想定外入力が、ToastHost のアクセント表示に渡す安定したキーへ変換されることを固定する。
    /// </summary>
    [Fact]
    public void ToastStyleLogic_MapsToastTypesToAccentKeysAndSymbols()
    {
        Assert.Equal("APrimary", ToastStyleLogic.AccentBrushKey(ToastType.success));
        Assert.Equal("ADangerSolid", ToastStyleLogic.AccentBrushKey(ToastType.error));
        Assert.Equal("ATextDim", ToastStyleLogic.AccentBrushKey(ToastType.info));
        Assert.Equal("ATextDim", ToastStyleLogic.AccentBrushKey("unknown"));
        Assert.Equal("✔", ToastStyleLogic.IconText(ToastType.success));
        Assert.Equal("⚠", ToastStyleLogic.IconText(ToastType.error));
        Assert.Equal("ℹ", ToastStyleLogic.IconText(ToastType.info));
        Assert.Equal("ℹ", ToastStyleLogic.IconText("unknown"));
    }

    /// <summary>
    /// ThumbnailSourceConverter が空値や不正値を null にし、逆変換を拒否することを確認する。
    ///
    /// XAML バインディングでは画像パスが未生成・空・不正形式になることがある。
    /// その場合に例外でバインディングを止めず null に落とすことを検証する。
    /// BitmapImage の成功生成は XAML ランタイムに依存するため、UI を起動しないこのテストでは扱わない。
    /// </summary>
    [Fact]
    public void ThumbnailSourceConverter_ReturnsNullForInvalidValuesAndRejectsConvertBack()
    {
        var converter = new ThumbnailSourceConverter { DecodePixelWidth = 128 };

        Assert.Null(converter.Convert(null!, typeof(BitmapImage), "", ""));
        Assert.Null(converter.Convert("", typeof(BitmapImage), "", ""));
        Assert.Null(converter.Convert("not a uri", typeof(BitmapImage), "", ""));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(new object(), typeof(string), "", ""));
    }

    /// <summary>
    /// AppIcons が未知アイコン名では null を返すことを確認する。
    ///
    /// 既知アイコンの Geometry 変換は XAML 型変換に依存するが、未知名の扱いは純粋な辞書検索である。
    /// XAML から typo された iconName が渡っても例外にならないよう、null フォールバックを固定する。
    /// </summary>
    [Fact]
    public void AppIcons_GetGeometryReturnsNullForUnknownIconNames()
    {
        Assert.Null(AppIcons.GetGeometry("missing-icon"));
    }

    /// <summary>
    /// SourceSlotExtensions が DB 値と画面側列挙値を相互変換することを確認する。
    ///
    /// source_slot は DB では 1/2、画面側では Primary/Secondary、既存フィルタ文字列では primary/secondary として使われる。
    /// これらの対応がずれると、フォルダごとのスキャン・表示・リセット対象が誤るため、代表変換を固定する。
    /// </summary>
    [Fact]
    public void SourceSlotExtensions_MapBetweenBackendValuesAndDisplayModeText()
    {
        Assert.Equal(1, SourceSlot.Primary.ToBackendValue());
        Assert.Equal(2, SourceSlot.Secondary.ToBackendValue());
        Assert.Equal(SourceSlot.Primary, SourceSlotExtensions.FromBackendValue(1));
        Assert.Equal(SourceSlot.Secondary, SourceSlotExtensions.FromBackendValue(2));
        Assert.Equal(SourceSlot.Primary, SourceSlotExtensions.FromBackendValue(99));
        Assert.Equal("primary", SourceSlot.Primary.ToTsDisplayFolderMode());
        Assert.Equal("secondary", SourceSlot.Secondary.ToTsDisplayFolderMode());
    }
}
