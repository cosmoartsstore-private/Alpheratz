using Alpheratz.Core.Database;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Messages;
using Alpheratz.Services;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>
/// PhotoModalState のナビゲーション境界と補助データ読込失敗分岐を補うテスト。
///
/// 既存の StateViewModelBehaviorTests は通常操作の流れをまとめて確認している。
/// ここでは履歴が空、選択がない、先頭/末尾にいる、といった早期 return 分岐と、
/// タグ読込に失敗した場合に UI を落とさず toast へ変換する分岐を個別に固定する。
/// </summary>
public sealed class PhotoModalStateAdditionalTests : IDisposable
{
    private readonly string tempDir;

    /// <summary>PhotoModalState が使う一時 DB ファイル用のディレクトリを作る。</summary>
    public PhotoModalStateAdditionalTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "Alpheratz.PhotoModalState.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    /// <summary>一時 DB ディレクトリを削除する。削除失敗はテスト結果と無関係なので握りつぶす。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Cleanup failure should not hide the assertion result.
        }
    }

    /// <summary>
    /// 履歴が空、未選択、先頭/末尾にいる場合のナビゲーションが何も変更しないことを確認する。
    ///
    /// PhotoModal の前後ボタンは CanGoPrev / CanGoNext と連動して無効化されるが、
    /// 実際のメソッド側も境界条件を防御している。
    /// ここでは UI ボタンを介さずメソッドを直接呼び、選択写真が範囲外へ動かないことを検証する。
    /// </summary>
    [Fact]
    public void NavigationMethods_SkipWhenHistorySelectionOrAdjacentPhotoIsMissing()
    {
        var db = InitializedDb("nav.db");
        var state = new PhotoModalState(new PhotoService(db), new ToastService());
        var first = Thumb("/photos/a.jpg");
        var second = Thumb("/photos/b.jpg");
        var third = Thumb("/photos/c.jpg");
        state.setPhotoList([first, second, third]);

        state.goBackPhoto();
        state.goPrevPhoto();
        state.goNextPhoto();
        state.onSelectPhoto(first);
        state.goPrevPhoto();
        state.onSelectPhoto(third);
        state.goNextPhoto();

        Assert.Same(third, state.SelectedPhoto);
        Assert.False(state.CanGoBack);
        Assert.False(state.CanGoNext);
        Assert.True(state.CanGoPrev);
    }

    /// <summary>
    /// タグ読込に失敗した場合、例外を外へ漏らさずエラー toast を追加することを確認する。
    ///
    /// setSelectedPhoto は補助データ読込を fire-and-forget で開始する。
    /// 未初期化 DB を使うことで tags テーブル参照が失敗し、PhotoModalState が catch して
    /// ユーザー向けの「写真のタグを読み込めませんでした」通知へ変換する現在仕様を検証する。
    /// </summary>
    [Fact]
    public async Task SetSelectedPhoto_ReportsAuxiliaryTagLoadFailureAsToast()
    {
        var db = new AlpheratzDb(Path.Combine(tempDir, "missing-schema.db"));
        var toastService = new ToastService();
        var state = new PhotoModalState(new PhotoService(db), toastService);

        state.setSelectedPhoto(Thumb("/photos/a.jpg"));
        for (var i = 0; i < 50 && toastService.toasts.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.NotNull(state.SelectedPhoto);
        Assert.Contains(
            toastService.toasts,
            toast => toast.Msg == MessageCatalog.getMsg("PhotoModalState.tagLoadFailed"));
    }

    /// <summary>
    /// テスト用 DB を初期化して返す。
    /// PhotoModalState の通常ナビゲーションではタグ読込が裏で走るため、正常なスキーマを用意する。
    /// </summary>
    private AlpheratzDb InitializedDb(string fileName)
    {
        var db = new AlpheratzDb(Path.Combine(tempDir, fileName));
        db.Initialize();
        return db;
    }

    /// <summary>
    /// PhotoModalState の選択と前後移動に必要な最小限の写真オブジェクトを作る。
    /// </summary>
    private static PhotoThumbnailItem Thumb(string path)
        => new()
        {
            PhotoPath = path,
            PhotoFilename = Path.GetFileName(path),
            Timestamp = "2026-06-05 12:00:00",
            SourceSlot = 1,
        };
}
