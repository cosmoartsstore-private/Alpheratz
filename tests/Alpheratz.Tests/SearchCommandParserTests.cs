using Alpheratz.Features.Gallery;

namespace Alpheratz.Tests;

/// <summary>
/// ギャラリー検索欄のコマンドパーサを検証するテスト。
///
/// SearchCommandParser はUI入力を構造化フィルタへ変換する境界であり、
/// DBやViewModelよりも手前で入力解釈を確定させる役割を持つ。
/// ここでは、既知コマンドがフィルタ値へ移り、未知または不正なコマンドは
/// 通常検索文字列として残るという現在の仕様を固定する。
/// </summary>
public sealed class SearchCommandParserTests
{
    /// <summary>
    /// 複数の既知コマンドと通常テキストを混在させた場合の変換結果を確認する。
    ///
    /// tag は引用符付きの値を許可し、is:fav はお気に入りフラグに変換される。
    /// コマンドとして消費された部分は PlainText から取り除かれ、
    /// 残った通常語だけが自由検索語として使われる。
    /// </summary>
    [Fact]
    public void Parse_ExtractsKnownCommandsAndKeepsPlainText()
    {
        var result = SearchCommandParser.Parse("summer tag:\"blue sky\" since:2026-06-01 is:fav folder:secondary");

        Assert.Equal("summer", result.PlainText);
        Assert.Equal("2026-06-01", result.DateFrom);
        Assert.True(result.FavoritesOnly);
        Assert.Equal("secondary", result.FolderMode);
        Assert.Equal(["blue sky"], result.Tags);
        Assert.True(result.HasCommands);
    }

    /// <summary>
    /// 未知コマンドと不正な日付は通常検索文字列に戻すことを確認する。
    ///
    /// パーサはユーザーの入力を勝手に捨てない方針である。
    /// そのため、認識できない key:value や形式不正な since は PlainText に残し、
    /// 後続のファイル名/ワールド名検索で使えるようにする。
    /// </summary>
    [Fact]
    public void Parse_KeepsUnknownOrInvalidCommandsAsPlainText()
    {
        var result = SearchCommandParser.Parse("alpha near:world since:2026/06/01 orientation:square beta");

        Assert.Equal("alpha near:world since:2026/06/01 orientation:square beta", result.PlainText);
        Assert.Null(result.DateFrom);
        Assert.Null(result.OrientationFilter);
        Assert.False(result.HasCommands);
    }
}
