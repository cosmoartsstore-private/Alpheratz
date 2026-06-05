using Alpheratz.Features.PhotoModal;
using Windows.System;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoModalPage から分離した表示・操作判定を検証するテスト。
///
/// PhotoModalPage は Page、Image、TextBlock、ComboBox などの WinUI 要素を直接操作する。
/// ここでは UI 要素を生成せず、選択写真の値から表示文言やタグ追加可否、キーボード操作を決める
/// helper の戻り値を固定する。これにより code-behind は値の適用と callback 実行に集中できる。
/// </summary>
public sealed class PhotoModalPageLogicTests
{
    /// <summary>
    /// SelectedPhoto の変更だけがモーダル表示全体の再同期対象になることを確認する。
    ///
    /// PhotoModalPage は選択写真が差し替わったときだけ、ワールド名、match_source、タグ追加欄、
    /// 表示画像をまとめて更新する。その他の通知で毎回画像を作り直すと不要なデコードが増えるため、
    /// property name の判定を helper に閉じ込めている。
    /// </summary>
    [Fact]
    public void ShouldSyncForPropertyChanged_ReturnsTrueOnlyForSelectedPhoto()
    {
        Assert.True(PhotoModalPageLogic.ShouldSyncForPropertyChanged(nameof(PhotoModalState.SelectedPhoto)));
        Assert.False(PhotoModalPageLogic.ShouldSyncForPropertyChanged(nameof(PhotoModalState.CanGoBack)));
        Assert.False(PhotoModalPageLogic.ShouldSyncForPropertyChanged(null));
    }

    /// <summary>
    /// PhotoModal の画像リクエストが空パスを拒否し、forward-slash を指定セパレータへ正規化することを確認する。
    ///
    /// DB 由来のパスは正規化済みで forward-slash を含む場合がある。
    /// BitmapImage へ渡す直前に OS のディレクトリセパレータへ揃え、モーダルの最大表示幅に合わせて
    /// DecodePixelWidth を 1920px に固定することで、4K 画像の過剰なフルデコードを避ける。
    /// </summary>
    [Fact]
    public void ModalImageRequest_NormalizesPathAndKeepsDecodeWidthLimit()
    {
        Assert.Null(PhotoModalPageLogic.ModalImageRequest(null, '#'));
        Assert.Null(PhotoModalPageLogic.ModalImageRequest("", '#'));

        Assert.Equal(
            new PhotoModalImageRequest("C:#photos#display.jpg", PhotoModalPageLogic.ModalDecodePixelWidth),
            PhotoModalPageLogic.ModalImageRequest("C:/photos/display.jpg", '#'));
    }

    /// <summary>
    /// ワールド名 fallback と match_source 表示ラベルを確認する。
    ///
    /// ワールド名が空の写真は「ワールド不明」と表示する。
    /// match_source は補完元をユーザーに説明できる既知値だけ表示し、DB 上の未設定値や未知値では
    /// 補足チップを隠して古いラベルが残らないようにする。
    /// </summary>
    [Fact]
    public void WorldNameAndMatchSource_ReturnUserFacingLabels()
    {
        Assert.Equal(PhotoModalPageLogic.UnknownWorldName, PhotoModalPageLogic.WorldNameText(null));
        Assert.Equal(PhotoModalPageLogic.UnknownWorldName, PhotoModalPageLogic.WorldNameText(""));
        Assert.Equal("Moonlight Station", PhotoModalPageLogic.WorldNameText("Moonlight Station"));

        Assert.Equal(new MatchSourceDisplay(true, "archive ログから補完"),
            PhotoModalPageLogic.MatchSource("polaris_archive"));
        Assert.Equal(new MatchSourceDisplay(true, "類似写真から推測"),
            PhotoModalPageLogic.MatchSource("phash"));
        Assert.Equal(new MatchSourceDisplay(true, "類似写真から確認済み"),
            PhotoModalPageLogic.MatchSource("phash_confirmed"));
        Assert.Equal(new MatchSourceDisplay(false, null),
            PhotoModalPageLogic.MatchSource("manual"));
        Assert.Equal(new MatchSourceDisplay(false, null),
            PhotoModalPageLogic.MatchSource(null));
    }

    /// <summary>
    /// マスタタグから現在写真に未付与のタグ数を数え、追加行の表示可否を返すことを確認する。
    ///
    /// マスタタグが未ロードなら追加候補はないものとして扱う。
    /// 写真側のタグ一覧が null 相当で渡された場合は、マスタタグすべてを追加可能として扱う。
    /// 現在付いているタグは候補から除外し、残りが 0 件なら追加欄を隠して空メッセージを表示する。
    /// </summary>
    [Fact]
    public void TagAddDisplay_CountsOnlyTagsNotAlreadyAssigned()
    {
        Assert.Equal(new TagAddDisplay(false, 0),
            PhotoModalPageLogic.TagAddDisplay(null, ["night"]));
        Assert.Equal(new TagAddDisplay(true, 3),
            PhotoModalPageLogic.TagAddDisplay(["night", "city", "avatar"], null));
        Assert.Equal(new TagAddDisplay(true, 1),
            PhotoModalPageLogic.TagAddDisplay(["night", "city", "avatar"], ["night", "avatar"]));
        Assert.Equal(new TagAddDisplay(false, 0),
            PhotoModalPageLogic.TagAddDisplay(["night"], ["night"]));
    }

    /// <summary>
    /// テキスト入力以外にフォーカスがある場合だけ、PhotoModal のキーボード操作へ変換されることを確認する。
    ///
    /// Escape は閉じる、左右キーは前後写真、Backspace は類似検索履歴の戻る操作に対応する。
    /// タグ ComboBox や TextBox などのテキスト入力へフォーカスがある場合は、入力中のキー操作を奪わない。
    /// </summary>
    [Fact]
    public void ResolveKeyAction_MapsModalKeysUnlessTextInputFocused()
    {
        Assert.Equal(PhotoModalPageKeyAction.Close, PhotoModalPageLogic.ResolveKeyAction(false, VirtualKey.Escape));
        Assert.Equal(PhotoModalPageKeyAction.GoPrevious, PhotoModalPageLogic.ResolveKeyAction(false, VirtualKey.Left));
        Assert.Equal(PhotoModalPageKeyAction.GoNext, PhotoModalPageLogic.ResolveKeyAction(false, VirtualKey.Right));
        Assert.Equal(PhotoModalPageKeyAction.GoBack, PhotoModalPageLogic.ResolveKeyAction(false, VirtualKey.Back));
        Assert.Equal(PhotoModalPageKeyAction.None, PhotoModalPageLogic.ResolveKeyAction(false, VirtualKey.Enter));
        Assert.Equal(PhotoModalPageKeyAction.None, PhotoModalPageLogic.ResolveKeyAction(true, VirtualKey.Escape));
    }

    /// <summary>
    /// タグ追加 request が callback、選択タグ、写真パスのすべてが揃った時だけ作られることを確認する。
    ///
    /// ComboBox の SelectedItem は object として渡るため、string 以外や空文字を拒否する。
    /// 写真未選択または callback 未設定の状態では、UI 側で何も実行しない null を返す。
    /// </summary>
    [Fact]
    public void AddExistingTagRequest_ReturnsRequestOnlyForValidInput()
    {
        Assert.Equal(new PhotoModalTagMutationRequest("C:/photo.png", "night"),
            PhotoModalPageLogic.AddExistingTagRequest("night", "C:/photo.png", canAddTag: true));
        Assert.Null(PhotoModalPageLogic.AddExistingTagRequest("night", "C:/photo.png", canAddTag: false));
        Assert.Null(PhotoModalPageLogic.AddExistingTagRequest("", "C:/photo.png", canAddTag: true));
        Assert.Null(PhotoModalPageLogic.AddExistingTagRequest(123, "C:/photo.png", canAddTag: true));
        Assert.Null(PhotoModalPageLogic.AddExistingTagRequest("night", null, canAddTag: true));
    }

    /// <summary>
    /// タグ削除 request が chip の Tag 値、写真パス、削除 callback のすべてが揃った時だけ作られることを確認する。
    ///
    /// ItemsControl の削除ボタンは FrameworkElement.Tag にタグ名を入れている。
    /// 空タグや写真未選択で削除 callback を呼ぶと対象不明の操作になるため、helper が null で止める。
    /// </summary>
    [Fact]
    public void RemoveTagRequest_ReturnsRequestOnlyForValidInput()
    {
        Assert.Equal(new PhotoModalTagMutationRequest("C:/photo.png", "night"),
            PhotoModalPageLogic.RemoveTagRequest("night", "C:/photo.png", canRemoveTag: true));
        Assert.Null(PhotoModalPageLogic.RemoveTagRequest("night", "C:/photo.png", canRemoveTag: false));
        Assert.Null(PhotoModalPageLogic.RemoveTagRequest("", "C:/photo.png", canRemoveTag: true));
        Assert.Null(PhotoModalPageLogic.RemoveTagRequest(new object(), "C:/photo.png", canRemoveTag: true));
        Assert.Null(PhotoModalPageLogic.RemoveTagRequest("night", null, canRemoveTag: true));
    }
}
