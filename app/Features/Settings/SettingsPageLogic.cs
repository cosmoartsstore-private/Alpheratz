using System;
using System.IO;
using Alpheratz.Features.Template;
using Windows.System;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Settings;

/// <summary>
/// SettingsPage の入力状態とラベル表示を UI 要素から切り離して扱う補助ロジック。
/// Visibility や Brush は扱わず、画面へ反映する文字列と bool だけを返す。
/// </summary>
internal static class SettingsPageLogic
{
    public const int PrimaryFolderSlot = 1;
    public const int SecondaryFolderSlot = 2;

    /// <summary>タグ一覧の空状態を表示するかを返す。</summary>
    public static bool IsTagEmpty(int tagCount) => tagCount == 0;

    /// <summary>キー入力がモーダルを閉じる Escape かを返す。</summary>
    public static bool ShouldCloseModal(VirtualKey key) => key == VirtualKey.Escape;

    /// <summary>キー入力がタグ追加の Enter かを返す。</summary>
    public static bool ShouldSubmitTag(VirtualKey key) => key == VirtualKey.Enter;

    /// <summary>テンプレート編集状態から、編集 UI のラベルとキャンセル表示可否を返す。</summary>
    public static TemplateEditorDisplay TemplateEditor(string? editingTemplate)
    {
        var isEditing = editingTemplate is not null;
        return new TemplateEditorDisplay(
            CancelVisible: isEditing,
            SaveButtonText: getMsg(isEditing
                ? "SettingsPageLogic.templateUpdateButton"
                : "common.register"),
            ModeLabel: getMsg(isEditing
                ? "SettingsPageLogic.templateEditHeading"
                : "SettingsPageLogic.templateNewHeading"));
    }

    /// <summary>テンプレートカードの状態ラベルとテーマリソースキーを返す。</summary>
    public static TemplateCardDisplay TemplateCard(bool isActive)
        => isActive
            ? new TemplateCardDisplay(
                getMsg("SettingsPageLogic.templateActiveLabel"),
                "APrimary",
                "ASurface",
                "APrimary",
                "AText")
            : new TemplateCardDisplay(
                getMsg("SettingsPageLogic.templateInactiveLabel"),
                "ABorder",
                "ASurface",
                "ATextDim",
                "AText");

    /// <summary>テーマボタンに表示する現在テーマ名を返す。</summary>
    public static string ThemeButtonText(bool isDark)
        => getMsg(isDark ? "SettingsPageLogic.themeDark" : "SettingsPageLogic.themeLight");

    /// <summary>テーマボタンの補助説明に表示する次の切替先を返す。</summary>
    public static string ThemeButtonTooltip(bool isDark)
        => getMsg(isDark
            ? "SettingsPageLogic.switchToLightTheme"
            : "SettingsPageLogic.switchToDarkTheme");

    /// <summary>現在テーマから次に保存する Dark 状態を返す。</summary>
    public static bool NextThemeIsDark(bool isDark) => !isDark;

    /// <summary>ワールド名補完が使用可能になるまでの PDQ 解析進捗表示を返す。</summary>
    public static WorldAnalysisProgressDisplay WorldAnalysisProgress(
        int done,
        int total,
        string? current,
        bool isRunning,
        bool canStartWorldAnalysis)
    {
        if (canStartWorldAnalysis)
            return WorldAnalysisProgressDisplay.Hidden;

        if (total <= 0)
        {
            var summary = isRunning
                ? getMsg("SettingsPageLogic.comparisonPreparationStarting")
                : getMsg("SettingsPageLogic.comparisonPreparationChecking");
            return new WorldAnalysisProgressDisplay(
                ProgressVisible: true,
                IsIndeterminate: true,
                Maximum: 1,
                Value: 0,
                SummaryText: summary,
                DetailText: getMsg("SettingsPageLogic.comparisonPreparationGuidance"));
        }

        var safeDone = Math.Clamp(done, 0, total);
        var percent = (int)Math.Round(safeDone * 100.0 / total);
        var detail = !string.IsNullOrWhiteSpace(current)
            ? getMsg("SettingsPageLogic.comparisonPreparationCurrent", ("current", current))
            : isRunning
                ? getMsg("SettingsPageLogic.comparisonPreparationRunning")
                : safeDone <= 0
                    ? getMsg("SettingsPageLogic.comparisonPreparationWaitingToStart")
                    : getMsg("SettingsPageLogic.comparisonPreparationWaitingToFinish");

        return new WorldAnalysisProgressDisplay(
            ProgressVisible: true,
            IsIndeterminate: false,
            Maximum: total,
            Value: safeDone,
            SummaryText: getMsg(
                "SettingsPageLogic.comparisonPreparationProgress",
                ("done", safeDone),
                ("total", total),
                ("percent", percent)),
            DetailText: detail);
    }

    /// <summary>設定ページで表示できるセクション ID かを返す。</summary>
    public static bool IsKnownSection(string? section)
        => section is "general" or "similar" or "tags" or "templates" or "credits";

    /// <summary>StellaRecord 登録に必要な exe と icon のパスを返す。未導入なら null。</summary>
    public static StellaRecordRegistrationRequest? StellaRecordRegistration(
        bool stellaRecordAvailable,
        string? processPath,
        string baseDirectory)
    {
        if (!stellaRecordAvailable) return null;
        if (string.IsNullOrWhiteSpace(processPath)) return null;
        return new StellaRecordRegistrationRequest(
            processPath,
            Path.Combine(baseDirectory, "Assets", "icon.png"));
    }

    /// <summary>TemplatePageViewModel の PropertyChanged から SettingsPage 側の更新種別を返す。</summary>
    public static TemplatePropertyUpdate TemplatePropertyAction(string? propertyName)
        => propertyName switch
        {
            nameof(TemplatePageViewModel.EditingTweetTemplate) => TemplatePropertyUpdate.UpdateEditor,
            nameof(TemplatePageViewModel.ActiveTweetTemplate) => TemplatePropertyUpdate.RefreshCards,
            _ => TemplatePropertyUpdate.None,
        };

    /// <summary>ItemsControl の DataContext が文字列のときだけ操作対象名として返す。</summary>
    public static string? StringItem(object? dataContext) => dataContext as string;

    /// <summary>削除 callback 未設定時にテンプレート一覧から直接削除してよいかを返す。</summary>
    public static bool ShouldRemoveTemplateLocally(bool hasDeleteCallback, bool templateExists)
        => !hasDeleteCallback && templateExists;
}

/// <summary>テンプレート編集欄の表示状態。</summary>
internal sealed record TemplateEditorDisplay(bool CancelVisible, string SaveButtonText, string ModeLabel);

/// <summary>テンプレートカードの表示状態。</summary>
internal sealed record TemplateCardDisplay(
    string Label,
    string BorderKey,
    string BackgroundKey,
    string LabelForegroundKey,
    string BodyForegroundKey);

/// <summary>ワールド名補完を開始する前の PDQ 解析進捗表示。</summary>
internal sealed record WorldAnalysisProgressDisplay(
    bool ProgressVisible,
    bool IsIndeterminate,
    double Maximum,
    double Value,
    string SummaryText,
    string DetailText)
{
    public static WorldAnalysisProgressDisplay Hidden { get; } = new(
        ProgressVisible: false,
        IsIndeterminate: false,
        Maximum: 1,
        Value: 0,
        SummaryText: string.Empty,
        DetailText: string.Empty);
}

/// <summary>StellaRecord ランチャー登録に渡すパス。</summary>
internal sealed record StellaRecordRegistrationRequest(string ExePath, string IconPath);

/// <summary>テンプレート ViewModel の変更通知から必要になる画面更新。</summary>
internal enum TemplatePropertyUpdate
{
    None,
    UpdateEditor,
    RefreshCards,
}
