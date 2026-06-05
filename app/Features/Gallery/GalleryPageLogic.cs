namespace Alpheratz.Features.Gallery;

/// <summary>
/// GalleryPage の表示同期に使う小さな状態計算を UI 要素から切り離して扱う補助ロジック。
/// バルク操作バーと月ナビゲーションの次状態だけを返す。
/// </summary>
internal static class GalleryPageLogic
{
    /// <summary>マルチセレクト状態と直前表示状態から、バルク操作バーの次の操作を決める。</summary>
    public static BulkOperationBarState ComputeBulkOperationBarState(
        bool isMultiSelectMode,
        bool wasVisible,
        int selectedCount)
    {
        var transition = (isMultiSelectMode, wasVisible) switch
        {
            (true, false) => BulkOperationBarTransition.Show,
            (false, true) => BulkOperationBarTransition.Hide,
            _ => BulkOperationBarTransition.None,
        };
        return new BulkOperationBarState(transition, isMultiSelectMode, $"{selectedCount} 枚選択");
    }

    /// <summary>表示中の先頭写真インデックスに対応する月グループのインデックスを返す。</summary>
    public static int? FindActiveMonthGroupIndex(IReadOnlyList<GalleryMonthGroup> groups, int firstVisibleIndex)
    {
        if (groups.Count == 0) return null;
        var matchedGroupIdx = 0;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (firstVisibleIndex >= groups[i].FirstIndex)
            {
                matchedGroupIdx = i;
                break;
            }
        }
        return matchedGroupIdx;
    }
}

/// <summary>バルク操作バーの表示遷移。</summary>
internal enum BulkOperationBarTransition
{
    None,
    Show,
    Hide,
}

/// <summary>バルク操作バー同期後の状態。</summary>
internal sealed record BulkOperationBarState(
    BulkOperationBarTransition Transition,
    bool NextWasVisible,
    string SelectionLabel);
