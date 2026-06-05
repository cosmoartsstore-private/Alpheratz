using Alpheratz.Features.Shell;
using Windows.System;

namespace Alpheratz.Tests;

/// <summary>
/// ShellPage のオーバーレイ状態とショートカット判定を検証するテスト。
///
/// ShellPage 本体は WinUI の Page、HeaderBar、ModalContent を直接操作するため、
/// 通常のユニットテストでは安全にインスタンス化しにくい。
/// ここでは UI 操作の前段にある状態判定だけを ShellPageInteractionLogic として分離し、
/// モーダル多重化防止、ヘッダーの不活性化、Esc/Ctrl ショートカットの優先順位を固定する。
/// </summary>
public sealed class ShellPageInteractionLogicTests
{
    /// <summary>
    /// オーバーレイ状態からヘッダーの操作可否と opacity が決まることを確認する。
    ///
    /// 写真モーダルまたは中位モーダルが開いている間はヘッダー操作を止め、opacity を 0.4 に落とす。
    /// 検索条件ドロワーだけが開いている場合は軽い dim として 0.6 を使い、何も開いていなければ通常表示に戻す。
    /// </summary>
    [Fact]
    public void ComputeHeaderInteractivity_DisablesHeaderForModalsAndFilterOverlay()
    {
        Assert.Equal(new ShellHeaderInteractivity(true, 1.0),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(false, false, false));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.6),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(false, false, true));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.4),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(true, false, true));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.4),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(false, true, false));
    }

    /// <summary>
    /// 検索条件ドロワーはモーダル表示中に新規オープンできず、閉じる操作だけは許可されることを確認する。
    ///
    /// モーダルの上に検索条件を重ねると操作対象が曖昧になる。
    /// ただし既に開いている検索条件を閉じる操作は、残留 overlay を解消するため常に許可する。
    /// </summary>
    [Fact]
    public void CanToggleFilter_BlocksOpeningOverModalsButAllowsClosing()
    {
        Assert.True(ShellPageInteractionLogic.CanToggleFilter(false, false, false));
        Assert.False(ShellPageInteractionLogic.CanToggleFilter(false, true, false));
        Assert.False(ShellPageInteractionLogic.CanToggleFilter(false, false, true));
        Assert.True(ShellPageInteractionLogic.CanToggleFilter(true, true, true));
    }

    /// <summary>
    /// 写真モーダル表示中の PreviewKeyDown が、テキスト入力中を除いて写真移動とクローズへ変換されることを確認する。
    ///
    /// PreviewKeyDown は背後の GridView より先に発火するため、写真モーダルの左右移動を安定させる境界になる。
    /// タグ入力欄など TextBox にフォーカスがある場合は、矢印キーや Esc の誤爆を避けて何もしない。
    /// </summary>
    [Fact]
    public void ResolvePhotoModalPreviewKey_HandlesNavigationOnlyWhenModalCanReceiveKeys()
    {
        Assert.Equal(PhotoModalKeyAction.GoPrevious,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.GoNext,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, VirtualKey.Right));
        Assert.Equal(PhotoModalKeyAction.Close,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, VirtualKey.Escape));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, true, VirtualKey.Escape));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(false, true, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, false, false, VirtualKey.Left));
    }

    /// <summary>
    /// Shell 全体の Esc キーが、確認ダイアログ、検索条件、マルチセレクトの順に処理されることを確認する。
    ///
    /// Esc の対象が複数ある場合は、最も前面または明示的な確認 UI から閉じる。
    /// これにより、確認モーダルが出ているのに背後の検索条件や選択状態だけが変わる退行を防ぐ。
    /// </summary>
    [Fact]
    public void ResolveShellKey_PrioritizesEscapeTargets()
    {
        Assert.Equal(ShellKeyAction.CloseConfirm,
            ShellPageInteractionLogic.ResolveShellKey(true, true, true, false, false, false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.CloseFilter,
            ShellPageInteractionLogic.ResolveShellKey(false, true, true, false, false, false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.ExitMultiSelect,
            ShellPageInteractionLogic.ResolveShellKey(false, false, true, false, false, false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, false, false, false, VirtualKey.Escape));
    }

    /// <summary>
    /// Ctrl 系ショートカットがモーダル非表示時だけ発火することを確認する。
    ///
    /// Ctrl+F は検索条件ドロワーを開き、既に開いていれば何もしない。
    /// Ctrl+Comma は設定モーダルを開く。写真モーダルまたは中位モーダル中は、
    /// 追加で別モーダルを重ねないために no-op とする。
    /// </summary>
    [Fact]
    public void ResolveShellKey_HandlesControlShortcutsOnlyWithoutOpenModals()
    {
        Assert.Equal(ShellKeyAction.OpenFilter,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, false, false, true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(false, true, false, false, false, true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.OpenSettings,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, false, false, true, (VirtualKey)188));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, true, false, true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, false, true, true, (VirtualKey)188));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(false, false, false, false, false, false, VirtualKey.F));
    }

    /// <summary>
    /// ヘッダー余白タップで、開いた直後は何も閉じず、その後は最上位から順に閉じることを確認する。
    ///
    /// モーダルを開いた直後の同じクリックやタップが閉じ操作として扱われないよう、400ms の抑制時間を置く。
    /// 抑制時間後は PhotoModal があればそれを先に閉じ、なければ Settings/GroupDrillDown などの中位モーダルを閉じる。
    /// </summary>
    [Fact]
    public void ResolveModalDismiss_UsesCooldownAndClosesTopmostLayerFirst()
    {
        Assert.Equal(ModalDismissAction.None,
            ShellPageInteractionLogic.ResolveModalDismiss(1300, 1000, true, true));
        Assert.Equal(ModalDismissAction.ClosePhotoModal,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, true, true));
        Assert.Equal(ModalDismissAction.CloseMiddleModal,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, false, true));
        Assert.Equal(ModalDismissAction.None,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, false, false));
    }
}
