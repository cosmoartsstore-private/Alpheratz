using System.IO;
using Alpheratz.Features.Template;
using Windows.System;

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
            SaveButtonText: isEditing ? "更新" : "登録",
            ModeLabel: isEditing ? "テンプレート編集" : "新規テンプレート");
    }

    /// <summary>テンプレートカードの状態ラベルとテーマリソースキーを返す。</summary>
    public static TemplateCardDisplay TemplateCard(bool isActive)
        => isActive
            ? new TemplateCardDisplay("使用中", "ABorderStrong", "AAccentSoft", "APrimary")
            : new TemplateCardDisplay("テンプレート", "ABorder", "ASurfaceSoft", "ATextDim");

    /// <summary>StellaRecord 登録に必要な exe と icon のパスを返す。未導入なら null。</summary>
    public static StellaRecordRegistrationRequest? StellaRecordRegistration(
        bool stellaRecordAvailable,
        string? processPath,
        string baseDirectory)
    {
        if (!stellaRecordAvailable) return null;
        return new StellaRecordRegistrationRequest(
            processPath ?? string.Empty,
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
internal sealed record TemplateCardDisplay(string Label, string BorderKey, string BackgroundKey, string LabelForegroundKey);

/// <summary>StellaRecord ランチャー登録に渡すパス。</summary>
internal sealed record StellaRecordRegistrationRequest(string ExePath, string IconPath);

/// <summary>テンプレート ViewModel の変更通知から必要になる画面更新。</summary>
internal enum TemplatePropertyUpdate
{
    None,
    UpdateEditor,
    RefreshCards,
}
