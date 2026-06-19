using Alpheratz.Features.Shell;
using System.Xml.Linq;
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
    /// ShellOverlayState がモーダル系と HeaderBar の dim 対象 overlay を正しく集約することを確認する。
    ///
    /// Confirm と MultiSelect はヘッダー dim には含めない。
    /// Confirm は新規 overlay 起動の抑止にも使い、MultiSelect は Esc 優先順位だけに使う。
    /// </summary>
    [Fact]
    public void ShellOverlayState_DerivedFlagsSeparateHeaderDimFromPreviewOnlyState()
    {
        var confirmOnly = Overlay(
            confirmOpen: true,
            multiSelectMode: true);
        var modalAndFilter = Overlay(
            photoModalOpen: true,
            filterOpen: true);

        Assert.False(confirmOnly.ModalOpen);
        Assert.False(confirmOnly.HeaderDimOverlayOpen);
        Assert.True(modalAndFilter.ModalOpen);
        Assert.True(modalAndFilter.HeaderDimOverlayOpen);
    }

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
            ShellPageInteractionLogic.ComputeHeaderInteractivity(Overlay()));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.6),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(Overlay(filterOpen: true)));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.4),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(Overlay(photoModalOpen: true, filterOpen: true)));
        Assert.Equal(new ShellHeaderInteractivity(false, 0.4),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(Overlay(middleModalOpen: true)));
        Assert.Equal(new ShellHeaderInteractivity(true, 1.0),
            ShellPageInteractionLogic.ComputeHeaderInteractivity(Overlay(confirmOpen: true)));
    }

    /// <summary>
    /// 検索条件ドロワーはモーダルや確認 UI の表示中に新規オープンできず、閉じる操作だけは許可されることを確認する。
    ///
    /// モーダルの上に検索条件を重ねると操作対象が曖昧になる。
    /// ただし既に開いている検索条件を閉じる操作は、残留 overlay を解消するため常に許可する。
    /// </summary>
    [Fact]
    public void CanToggleFilter_BlocksOpeningOverModalsButAllowsClosing()
    {
        Assert.True(ShellPageInteractionLogic.CanToggleFilter(Overlay()));
        Assert.False(ShellPageInteractionLogic.CanToggleFilter(Overlay(photoModalOpen: true)));
        Assert.False(ShellPageInteractionLogic.CanToggleFilter(Overlay(middleModalOpen: true)));
        Assert.True(ShellPageInteractionLogic.CanToggleFilter(Overlay(filterOpen: true)));
        Assert.False(ShellPageInteractionLogic.CanToggleFilter(Overlay(confirmOpen: true)));
        Assert.True(ShellPageInteractionLogic.CanToggleFilter(Overlay(photoModalOpen: true, middleModalOpen: true, filterOpen: true, confirmOpen: true)));
    }

    /// <summary>
    /// Settings モーダルを新規に開ける状態を確認する。
    ///
    /// Confirm は明示的な確認 UI なので新規モーダル起動を止める。
    /// MultiSelect は一覧の選択状態であり、Settings 起動ガードには含めない。
    /// </summary>
    [Fact]
    public void CanOpenSettingsModal_BlocksHeaderDimAndConfirmOverlays()
    {
        Assert.True(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay()));
        Assert.True(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay(multiSelectMode: true)));
        Assert.False(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay(confirmOpen: true)));
        Assert.False(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay(photoModalOpen: true)));
        Assert.False(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay(middleModalOpen: true)));
        Assert.False(ShellPageInteractionLogic.CanOpenSettingsModal(Overlay(filterOpen: true)));
    }

    /// <summary>
    /// 写真モーダル表示中の PreviewKeyDown が、入力中や内側 overlay 表示中を除いて写真移動とクローズへ変換されることを確認する。
    ///
    /// PreviewKeyDown は背後の GridView より先に発火するため、写真モーダルの左右移動を安定させる境界になる。
    /// TextBox にフォーカスがある場合や PhotoModal 内側 overlay がある場合は、矢印キーや Esc の誤爆を避けて何もしない。
    /// </summary>
    [Fact]
    public void ResolvePhotoModalPreviewKey_HandlesNavigationOnlyWhenModalCanReceiveKeys()
    {
        Assert.Equal(PhotoModalKeyAction.GoPrevious,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.GoNext,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, false, VirtualKey.Right));
        Assert.Equal(PhotoModalKeyAction.Close,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, false, VirtualKey.Escape));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, false, true, VirtualKey.Escape));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(false, true, false, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, false, false, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, true, false, VirtualKey.Escape));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, true, false, VirtualKey.Left));
        Assert.Equal(PhotoModalKeyAction.None,
            ShellPageInteractionLogic.ResolvePhotoModalPreviewKey(true, true, true, false, VirtualKey.Right));
    }

    /// <summary>
    /// Shell root の PreviewKeyDown が、確認 UI 表示中の Esc と背面ナビゲーションキーを先に処理することを確認する。
    /// </summary>
    [Fact]
    public void ResolveShellPreviewKey_HandlesConfirmBeforeChildPages()
    {
        Assert.Equal(ShellPreviewKeyAction.CloseConfirm,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(confirmOpen: true), VirtualKey.Escape));
        Assert.Equal(ShellPreviewKeyAction.Suppress,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(photoModalOpen: true, confirmOpen: true), VirtualKey.Left));
        Assert.Equal(ShellPreviewKeyAction.Suppress,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(photoModalOpen: true, confirmOpen: true), VirtualKey.Right));
        Assert.Equal(ShellPreviewKeyAction.Suppress,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(photoModalOpen: true, confirmOpen: true), VirtualKey.Back));
        Assert.Equal(ShellPreviewKeyAction.None,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(confirmOpen: true), VirtualKey.Tab));
        Assert.Equal(ShellPreviewKeyAction.None,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(confirmOpen: true), VirtualKey.Enter));
        Assert.Equal(ShellPreviewKeyAction.None,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(confirmOpen: true), VirtualKey.Space));
        Assert.Equal(ShellPreviewKeyAction.None,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(confirmOpen: true), VirtualKey.F));
        Assert.Equal(ShellPreviewKeyAction.None,
            ShellPageInteractionLogic.ResolveShellPreviewKey(Overlay(filterOpen: true), VirtualKey.Escape));
    }

    /// <summary>
    /// Shell 全体の bubbling Esc キーが、確認ダイアログ、検索条件、マルチセレクトの順に処理されることを確認する。
    ///
    /// Esc の対象が複数ある場合は、最も前面または明示的な確認 UI から閉じる。
    /// これにより、確認モーダルが出ているのに背後の検索条件や選択状態だけが変わる退行を防ぐ。
    /// </summary>
    [Fact]
    public void ResolveShellKey_PrioritizesEscapeTargets()
    {
        Assert.Equal(ShellKeyAction.CloseConfirm,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(filterOpen: true, confirmOpen: true, multiSelectMode: true), false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.CloseFilter,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(filterOpen: true, multiSelectMode: true), false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.ExitMultiSelect,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(multiSelectMode: true), false, VirtualKey.Escape));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(), false, VirtualKey.Escape));
    }

    /// <summary>
    /// Ctrl 系ショートカットが、HeaderBar の dim 対象や確認 UI がない時だけ発火することを確認する。
    ///
    /// Ctrl+F は検索条件ドロワーを開き、既に開いていれば何もしない。
    /// Ctrl+Comma は設定モーダルを開く。モーダル、検索条件、確認 UI の表示中は no-op とする。
    /// </summary>
    [Fact]
    public void ResolveShellKey_HandlesControlShortcutsOnlyWithoutOpenModals()
    {
        Assert.Equal(ShellKeyAction.OpenFilter,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(), true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(filterOpen: true), true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.OpenSettings,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(), true, (VirtualKey)188));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(photoModalOpen: true), true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(middleModalOpen: true), true, (VirtualKey)188));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(), false, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(filterOpen: true), true, (VirtualKey)188));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(confirmOpen: true), true, VirtualKey.F));
        Assert.Equal(ShellKeyAction.None,
            ShellPageInteractionLogic.ResolveShellKey(Overlay(confirmOpen: true), true, (VirtualKey)188));
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
            ShellPageInteractionLogic.ResolveModalDismiss(1300, 1000, Overlay(photoModalOpen: true, middleModalOpen: true)));
        Assert.Equal(ModalDismissAction.ClosePhotoModal,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, Overlay(photoModalOpen: true, middleModalOpen: true)));
        Assert.Equal(ModalDismissAction.CloseMiddleModal,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, Overlay(middleModalOpen: true)));
        Assert.Equal(ModalDismissAction.None,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, Overlay()));
        Assert.Equal(ModalDismissAction.None,
            ShellPageInteractionLogic.ResolveModalDismiss(1400, 1000, Overlay(filterOpen: true, confirmOpen: true, multiSelectMode: true)));
    }

    /// <summary>
    /// ShellPage の XAML root に tunneling / bubbling の両キーイベントが接続されていることを確認する。
    /// </summary>
    [Fact]
    public void ShellPageXaml_WiresPreviewAndBubbleKeyHandlers()
    {
        var xamlPath = Path.Combine(FindRepositoryRoot(), "app", "Features", "Shell", "ShellPage.xaml");
        var document = XDocument.Load(xamlPath);
        var root = document.Root!;

        Assert.Equal("ShellPage_PreviewKeyDown", root.Attribute("PreviewKeyDown")?.Value);
        Assert.Equal("ShellPage_KeyDown", root.Attribute("KeyDown")?.Value);

        var shellNamespace = root.Name.Namespace;
        var confirmOverlay = document.Descendants(shellNamespace + "Grid")
            .Single(element => element.Attribute("Name")?.Value == "ConfirmOverlay" || element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "ConfirmOverlay");
        var filterOverlay = document.Descendants(shellNamespace + "Grid")
            .Single(element => element.Attribute("Name")?.Value == "FilterOverlay" || element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "FilterOverlay");
        Assert.Equal("20", confirmOverlay.Attribute("Canvas.ZIndex")?.Value);
        Assert.Equal("10", filterOverlay.Attribute("Canvas.ZIndex")?.Value);
    }

    /// <summary>Shell の overlay 状態を名前付き引数で作成する。</summary>
    private static ShellOverlayState Overlay(
        bool photoModalOpen = false,
        bool middleModalOpen = false,
        bool filterOpen = false,
        bool confirmOpen = false,
        bool multiSelectMode = false)
        => new(
            PhotoModalOpen: photoModalOpen,
            MiddleModalOpen: middleModalOpen,
            FilterOpen: filterOpen,
            ConfirmOpen: confirmOpen,
            MultiSelectMode: multiSelectMode);

    /// <summary>テスト実行ディレクトリからリポジトリルートを探索する。</summary>
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "app", "Alpheratz.Frontend.csproj")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Alpheratz.Frontend.csproj を含むリポジトリルートが見つかりません。");
    }
}
