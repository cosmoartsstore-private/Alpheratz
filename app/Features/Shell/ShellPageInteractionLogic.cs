using Windows.System;

namespace Alpheratz.Features.Shell;

/// <summary>
/// ShellPage のオーバーレイ状態とショートカット判定を UI 要素から切り離して扱う補助ロジック。
/// 表示やクリック実行は ShellPage 側に残し、ここでは次に取るべき動作だけを返す。
/// </summary>
internal static class ShellPageInteractionLogic
{
    /// <summary>現在のオーバーレイ状態からヘッダー操作可否と不透明度を算出する。</summary>
    public static ShellHeaderInteractivity ComputeHeaderInteractivity(ShellOverlayState state)
    {
        return new ShellHeaderInteractivity(
            ControlsInteractive: !state.HeaderDimOverlayOpen,
            Opacity: state.ModalOpen ? 0.4 : (state.FilterOpen ? 0.6 : 1.0));
    }

    /// <summary>Settings モーダルを新規に開けるかを返す。</summary>
    public static bool CanOpenSettingsModal(ShellOverlayState state)
        => !state.HeaderDimOverlayOpen && !state.ConfirmOpen;

    /// <summary>検索条件オーバーレイをトグルできるかを判定する。閉じる操作は常に許可する。</summary>
    public static bool CanToggleFilter(ShellOverlayState state)
        => state.FilterOpen || (!state.ModalOpen && !state.ConfirmOpen);

    /// <summary>Shell ルートの tunneling キー入力を、子ページより先に処理する動作へ変換する。</summary>
    public static ShellPreviewKeyAction ResolveShellPreviewKey(ShellOverlayState state, VirtualKey key)
    {
        if (state.ConfirmOpen && key == VirtualKey.Escape)
            return ShellPreviewKeyAction.CloseConfirm;
        if (state.ConfirmOpen && key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Back)
            return ShellPreviewKeyAction.Suppress;

        return ShellPreviewKeyAction.None;
    }

    /// <summary>写真モーダル表示中の tunneling キー入力を、写真移動またはクローズ動作へ変換する。</summary>
    public static PhotoModalKeyAction ResolvePhotoModalPreviewKey(
        bool isPhotoModalOpen,
        bool hasActivePhotoModal,
        bool hasPhotoModalInnerOverlay,
        bool isTextInputFocused,
        VirtualKey key)
    {
        if (!isPhotoModalOpen || !hasActivePhotoModal || hasPhotoModalInnerOverlay || isTextInputFocused)
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
    public static ShellKeyAction ResolveShellKey(ShellOverlayState state, bool ctrlDown, VirtualKey key)
    {
        if (key == VirtualKey.Escape)
        {
            if (state.ConfirmOpen) return ShellKeyAction.CloseConfirm;
            if (state.FilterOpen) return ShellKeyAction.CloseFilter;
            if (state.MultiSelectMode) return ShellKeyAction.ExitMultiSelect;
            return ShellKeyAction.None;
        }

        if (state.ConfirmOpen || state.HeaderDimOverlayOpen || !ctrlDown)
            return ShellKeyAction.None;

        if (key == VirtualKey.F)
            return ShellKeyAction.OpenFilter;
        if (key == (VirtualKey)188)
            return ShellKeyAction.OpenSettings;
        return ShellKeyAction.None;
    }

    /// <summary>ヘッダー余白タップで閉じるべきモーダル階層を判定する。</summary>
    public static ModalDismissAction ResolveModalDismiss(
        long currentTick,
        long lastModalOpenTick,
        ShellOverlayState state)
    {
        if (currentTick - lastModalOpenTick < 400) return ModalDismissAction.None;
        if (state.PhotoModalOpen) return ModalDismissAction.ClosePhotoModal;
        if (state.MiddleModalOpen) return ModalDismissAction.CloseMiddleModal;
        return ModalDismissAction.None;
    }
}

/// <summary>
/// ShellPage が扱うオーバーレイ状態を 1 つにまとめた純粋状態。
/// Confirm と MultiSelect はヘッダー dim には使わず、Confirm は新規 overlay 起動の抑止にも使う。
/// MultiSelect は Esc 優先順位だけに使う。
/// </summary>
internal sealed record ShellOverlayState(
    bool PhotoModalOpen,
    bool MiddleModalOpen,
    bool FilterOpen,
    bool ConfirmOpen = false,
    bool MultiSelectMode = false)
{
    /// <summary>写真モーダルまたは中位モーダルが開いているかを返す。</summary>
    public bool ModalOpen => PhotoModalOpen || MiddleModalOpen;

    /// <summary>HeaderBar の dim と不活性化に使うオーバーレイが開いているかを返す。</summary>
    public bool HeaderDimOverlayOpen => ModalOpen || FilterOpen;
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

/// <summary>ShellPage の tunneling キー操作結果。</summary>
internal enum ShellPreviewKeyAction
{
    None,
    Suppress,
    CloseConfirm,
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
