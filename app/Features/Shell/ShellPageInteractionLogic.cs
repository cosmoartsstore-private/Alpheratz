using Windows.System;

namespace Alpheratz.Features.Shell;

/// <summary>
/// ShellPage のオーバーレイ状態とショートカット判定を UI 要素から切り離して扱う補助ロジック。
/// 表示やクリック実行は ShellPage 側に残し、ここでは次に取るべき動作だけを返す。
/// </summary>
internal static class ShellPageInteractionLogic
{
    /// <summary>現在のオーバーレイ状態からヘッダー操作可否と不透明度を算出する。</summary>
    public static ShellHeaderInteractivity ComputeHeaderInteractivity(
        bool isPhotoModalOpen,
        bool isMiddleModalOpen,
        bool isFilterOpen)
    {
        var anyModalOpen = isPhotoModalOpen || isMiddleModalOpen;
        var anyOverlayOpen = anyModalOpen || isFilterOpen;
        return new ShellHeaderInteractivity(
            ControlsInteractive: !anyOverlayOpen,
            Opacity: anyModalOpen ? 0.4 : (isFilterOpen ? 0.6 : 1.0));
    }

    /// <summary>検索条件オーバーレイをトグルできるかを判定する。閉じる操作は常に許可する。</summary>
    public static bool CanToggleFilter(bool isFilterOpen, bool isPhotoModalOpen, bool isMiddleModalOpen)
        => isFilterOpen || (!isPhotoModalOpen && !isMiddleModalOpen);

    /// <summary>写真モーダル表示中の tunneling キー入力を、写真移動またはクローズ動作へ変換する。</summary>
    public static PhotoModalKeyAction ResolvePhotoModalPreviewKey(
        bool isPhotoModalOpen,
        bool hasActivePhotoModal,
        bool isTextInputFocused,
        VirtualKey key)
    {
        if (!isPhotoModalOpen || !hasActivePhotoModal || isTextInputFocused)
            return PhotoModalKeyAction.None;

        return key switch
        {
            VirtualKey.Left => PhotoModalKeyAction.GoPrevious,
            VirtualKey.Right => PhotoModalKeyAction.GoNext,
            VirtualKey.Escape => PhotoModalKeyAction.Close,
            _ => PhotoModalKeyAction.None,
        };
    }

    /// <summary>Shell 全体の bubbling キー入力を、検索条件・設定・選択解除などの動作へ変換する。</summary>
    public static ShellKeyAction ResolveShellKey(
        bool confirmOpen,
        bool isFilterOpen,
        bool isMultiSelectMode,
        bool isPhotoModalOpen,
        bool isMiddleModalOpen,
        bool ctrlDown,
        VirtualKey key)
    {
        if (key == VirtualKey.Escape)
        {
            if (confirmOpen) return ShellKeyAction.CloseConfirm;
            if (isFilterOpen) return ShellKeyAction.CloseFilter;
            if (isMultiSelectMode) return ShellKeyAction.ExitMultiSelect;
            return ShellKeyAction.None;
        }

        if (isPhotoModalOpen || isMiddleModalOpen || !ctrlDown)
            return ShellKeyAction.None;

        if (key == VirtualKey.F)
            return isFilterOpen ? ShellKeyAction.None : ShellKeyAction.OpenFilter;
        if (key == (VirtualKey)188)
            return ShellKeyAction.OpenSettings;
        return ShellKeyAction.None;
    }

    /// <summary>ヘッダー余白タップで閉じるべきモーダル階層を判定する。</summary>
    public static ModalDismissAction ResolveModalDismiss(
        long currentTick,
        long lastModalOpenTick,
        bool isPhotoModalOpen,
        bool isMiddleModalOpen)
    {
        if (currentTick - lastModalOpenTick < 400) return ModalDismissAction.None;
        if (isPhotoModalOpen) return ModalDismissAction.ClosePhotoModal;
        if (isMiddleModalOpen) return ModalDismissAction.CloseMiddleModal;
        return ModalDismissAction.None;
    }
}

/// <summary>ヘッダーの操作可否と見た目の弱め方。</summary>
internal sealed record ShellHeaderInteractivity(bool ControlsInteractive, double Opacity);

/// <summary>写真モーダル表示中のキー操作結果。</summary>
internal enum PhotoModalKeyAction
{
    None,
    GoPrevious,
    GoNext,
    Close,
}

/// <summary>ShellPage 全体のショートカット操作結果。</summary>
internal enum ShellKeyAction
{
    None,
    CloseConfirm,
    CloseFilter,
    ExitMultiSelect,
    OpenFilter,
    OpenSettings,
}

/// <summary>ヘッダー余白タップで閉じる対象。</summary>
internal enum ModalDismissAction
{
    None,
    ClosePhotoModal,
    CloseMiddleModal,
}
