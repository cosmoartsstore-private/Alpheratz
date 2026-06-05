using Microsoft.UI.Xaml;

namespace Alpheratz.Features.Shell.Controls;

/// <summary>
/// ShellStage の重ね合わせレイヤに使う表示遷移と version 判定。
/// 実際のアニメーションは code-behind に残し、分岐だけを純粋関数で決める。
/// </summary>
internal static class ShellStageLayerLogic
{
    public const int ModalFadeInDurationMilliseconds = 250;
    public const int ModalScaleInDurationMilliseconds = 350;
    public const int ModalFadeOutDurationMilliseconds = 200;
    public const int ModalScaleOutDurationMilliseconds = 200;
    public const int OverlayFadeInDurationMilliseconds = 300;
    public const int OverlayFadeOutDurationMilliseconds = 250;

    /// <summary>モーダルレイヤの要求 Visibility と現在値から、必要な遷移を返す。</summary>
    public static ShellStageLayerTransition ModalTransition(Visibility requested, Visibility current)
        => requested switch
        {
            Visibility.Visible => ShellStageLayerTransition.Open,
            _ when current == Visibility.Visible => ShellStageLayerTransition.Close,
            _ => ShellStageLayerTransition.SetDirectly,
        };

    /// <summary>スキャンオーバーレイの要求 Visibility と現在値から、必要な遷移を返す。</summary>
    public static ShellStageLayerTransition OverlayTransition(Visibility requested, Visibility current)
        => requested switch
        {
            Visibility.Visible when current != Visibility.Visible => ShellStageLayerTransition.Open,
            Visibility.Collapsed when current == Visibility.Visible => ShellStageLayerTransition.Close,
            _ => ShellStageLayerTransition.SetDirectly,
        };

    /// <summary>レイヤ開閉操作ごとに進める version 値を返す。</summary>
    public static int NextVersion(int currentVersion) => currentVersion + 1;

    /// <summary>閉じるアニメ完了時、自分が開始した close がまだ最新かを返す。</summary>
    public static bool CanCompleteClose(int currentVersion, int closeVersion)
        => currentVersion == closeVersion;
}

/// <summary>ShellStage のレイヤ表示遷移種別。</summary>
internal enum ShellStageLayerTransition
{
    Open,
    Close,
    SetDirectly,
}
