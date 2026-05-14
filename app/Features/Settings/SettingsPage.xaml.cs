using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Template;
using Alpheratz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Settings;

/// <summary>
/// 設定モーダル。3 セクション (全般 / タグマスタ / 投稿テンプレート) をスクロール可能な
/// 1 画面に統合した。<see cref="SettingsCompositeViewModel"/> をルート DataContext として、
/// 各セクションは <c>{Binding Settings.X}</c> / <c>{Binding TagMaster.X}</c> /
/// <c>{Binding Template.X}</c> でネストアクセスする。
///
/// 表示は ShellStage の ModalContent スロット経由 (<see cref="ShellPage.ShowSettings"/>)。
/// 旧 <c>Stage.MainContent</c> 差し替え経路は廃止。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly SettingsCompositeViewModel viewModel;

    // ===== 共通 =====
    /// <summary>モーダル閉じる操作 (× ボタン / 背景クリック / ESC)。</summary>
    public Action? OnClose { get; set; }

    // ===== 全般 =====
    /// <summary>フォルダ変更ボタン押下 (slot=1: メイン / slot=2: セカンダリ)。</summary>
    public Func<int, Task>? OnChooseFolder { get; set; }
    /// <summary>フォルダ初期化（ペンディング状態を経由してから実行される）。</summary>
    public Action<int>? OnResetFolder { get; set; }
    /// <summary>起動時自動起動 トグル変更。</summary>
    public Func<bool, Task>? OnStartupPreferenceChanged { get; set; }
    /// <summary>テーマ切替 (true=Dark)。</summary>
    public Func<bool, Task>? OnThemeChanged { get; set; }
    /// <summary>「ワールド不明写真を解析する」ボタン押下。WorldResolve モーダルを開く。</summary>
    public Func<Task>? OnStartWorldAnalysis { get; set; }

    // ===== タグマスタ =====
    /// <summary>タグマスタ追加 (TagDraft の内容で作成)。</summary>
    public Func<Task>? OnCreateTag { get; set; }
    /// <summary>タグマスタ削除。</summary>
    public Func<string, Task>? OnDeleteTag { get; set; }

    // ===== 投稿テンプレート =====
    /// <summary>テンプレート編集モード解除。</summary>
    public Action? OnCancelEdit { get; set; }
    /// <summary>テンプレート保存 (新規 / 既存上書きどちらも)。</summary>
    public Func<Task>? OnSaveTemplate { get; set; }
    /// <summary>テンプレート編集モード開始。</summary>
    public Action<string>? OnStartEdit { get; set; }
    /// <summary>テンプレート削除 (永続化を伴う)。</summary>
    public Func<string, Task>? OnDeleteTemplate { get; set; }
    /// <summary>テンプレート選択 (アクティブテンプレートの切替)。</summary>
    public Func<string, Task>? OnSelectTemplate { get; set; }

    public SettingsPage(SettingsCompositeViewModel viewModel)
    {
        AppLogger.Trace("SettingsPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SettingsPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        this.viewModel = viewModel;
        DataContext = viewModel;

        // ShellPage は SettingsPage インスタンスをキャッシュして使い回すため、
        // モーダル開閉ごとに Loaded/Unloaded が繰り返し発火する。サブスクリプションは
        // Loaded で張り、Unloaded で剥がす対称形にしておかないと、
        // 「Unloaded で剥がしたあと再 Loaded したときに張り直されない」バグが入る。
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;

        AppLogger.Trace("SettingsPage.ctor: exit");
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.TagMaster.masterTags.CollectionChanged += MasterTags_CollectionChanged;
            viewModel.Template.PropertyChanged += TemplateViewModel_PropertyChanged;
            UpdateTagEmptyState();
            UpdateEditorState();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsPage_Loaded: threw: {ex}"); }
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.TagMaster.masterTags.CollectionChanged -= MasterTags_CollectionChanged;
            viewModel.Template.PropertyChanged -= TemplateViewModel_PropertyChanged;
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsPage_Unloaded: threw: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // モーダル開閉用ハンドラ
    // -----------------------------------------------------------------------

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        OnClose?.Invoke();
    }

    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // モーダル内側のクリックが背景にバブルしないようにする (Backdrop_Tapped で閉じてしまうのを防ぐ)
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        OnClose?.Invoke();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    // -----------------------------------------------------------------------
    // 全般セクションのハンドラ
    // -----------------------------------------------------------------------

    private async void ChoosePrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(1).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChoosePrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: exit");
    }

    private async void ChooseSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(2).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChooseSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: exit");
    }

    private void ResetPrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(1); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetPrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: exit");
    }

    private void ResetSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(2); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: exit");
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.StartupToggle_Toggled: enter");
        try
        {
            if (sender is ToggleSwitch toggleSwitch && OnStartupPreferenceChanged is not null)
                await OnStartupPreferenceChanged(toggleSwitch.IsOn).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.StartupToggle_Toggled: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.StartupToggle_Toggled: exit");
    }

    private async void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ThemeLight_Click: enter");
        try { if (OnThemeChanged is not null) await OnThemeChanged(false).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ThemeLight_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ThemeLight_Click: exit");
    }

    private async void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ThemeDark_Click: enter");
        try { if (OnThemeChanged is not null) await OnThemeChanged(true).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ThemeDark_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ThemeDark_Click: exit");
    }

    private void RegisterStellaRecord_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: enter");
        try
        {
            if (!StellaRecordRegistration.IsStellaRecordAvailable())
            {
                AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: StellaRecord not available");
                return;
            }
            var exePath = Environment.ProcessPath ?? string.Empty;
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            StellaRecordRegistration.Register(exePath, iconPath);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.RegisterStellaRecord_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: exit");
    }

    private async void StartWorldAnalysis_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: enter");
        try { if (OnStartWorldAnalysis is not null) await OnStartWorldAnalysis().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.StartWorldAnalysis_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: exit");
    }

    // -----------------------------------------------------------------------
    // タグマスタセクションのハンドラ
    // -----------------------------------------------------------------------

    private void MasterTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateTagEmptyState);
    }

    private void UpdateTagEmptyState()
    {
        var isEmpty = viewModel.TagMaster.masterTags.Count == 0;
        TagEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TagInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            AddTag_Click(sender, e);
        }
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.AddTag_Click: enter");
        try
        {
            if (OnCreateTag is not null)
            {
                await OnCreateTag().ConfigureAwait(false);
                return;
            }
            await viewModel.TagMaster.createTag().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.AddTag_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.AddTag_Click: exit");
    }

    private async void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.DeleteTag_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string tag) return;
            if (OnDeleteTag is not null)
            {
                await OnDeleteTag(tag).ConfigureAwait(false);
                return;
            }
            await viewModel.TagMaster.deleteTag(tag).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.DeleteTag_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.DeleteTag_Click: exit");
    }

    // -----------------------------------------------------------------------
    // 投稿テンプレートセクションのハンドラ
    // -----------------------------------------------------------------------

    private void TemplateViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TemplatePageViewModel.EditingTweetTemplate))
        {
            DispatcherQueue.TryEnqueue(UpdateEditorState);
        }
        else if (e.PropertyName is nameof(TemplatePageViewModel.ActiveTweetTemplate))
        {
            DispatcherQueue.TryEnqueue(RefreshTemplateCardVisuals);
        }
    }

    private void UpdateEditorState()
    {
        var isEditing = viewModel.Template.EditingTweetTemplate is not null;
        CancelEditButton.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = isEditing ? "更新" : "登録";
        EditorModeLabel.Text = isEditing ? "テンプレート編集" : "新規テンプレート";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.CancelEdit_Click: enter");
        try
        {
            if (OnCancelEdit is not null) OnCancelEdit();
            else viewModel.Template.cancelEdit();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.CancelEdit_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.CancelEdit_Click: exit");
    }

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.SaveTemplate_Click: enter");
        try
        {
            if (OnSaveTemplate is not null)
            {
                await OnSaveTemplate().ConfigureAwait(false);
                return;
            }
            viewModel.Template.saveTemplateDraft();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SaveTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.SaveTemplate_Click: exit");
    }

    private void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.EditTemplate_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string template) return;
            if (OnStartEdit is not null) OnStartEdit(template);
            else viewModel.Template.startEdit(template);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.EditTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.EditTemplate_Click: exit");
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.DeleteTemplate_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string template) return;
            if (OnDeleteTemplate is not null)
            {
                await OnDeleteTemplate(template).ConfigureAwait(false);
            }
            else
            {
                AppLogger.Warn("SettingsPage.DeleteTemplate_Click: OnDeleteTemplate not wired; collection-only delete");
                if (viewModel.Template.tweetTemplates.Contains(template))
                    viewModel.Template.tweetTemplates.Remove(template);
            }
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.DeleteTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.DeleteTemplate_Click: exit");
    }

    private async void TemplateCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.TemplateCard_PointerPressed: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string template) return;
            if (OnSelectTemplate is not null)
                await OnSelectTemplate(template).ConfigureAwait(false);
            else
                viewModel.Template.ActiveTweetTemplate = template;
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.TemplateCard_PointerPressed: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.TemplateCard_PointerPressed: exit");
    }

    private void TemplateCard_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Border card) return;
            if (card.DataContext is not string template) return;
            ApplyTemplateCardStyle(card, template == viewModel.Template.ActiveTweetTemplate);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.TemplateCard_Loaded: threw: {ex}"); }
    }

    private void RefreshTemplateCardVisuals()
    {
        try
        {
            for (int i = 0; i < TemplateList.Items.Count; i++)
            {
                var container = TemplateList.ContainerFromIndex(i);
                if (container is null) continue;
                var card = FindChildBorder(container);
                if (card?.DataContext is string template)
                    ApplyTemplateCardStyle(card, template == viewModel.Template.ActiveTweetTemplate);
            }
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.RefreshTemplateCardVisuals: threw: {ex}"); }
    }

    private static Border? FindChildBorder(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b) return b;
            var found = FindChildBorder(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static void ApplyTemplateCardStyle(Border card, bool isActive)
    {
        card.BorderBrush = (Brush)Application.Current.Resources[isActive ? "ABorderStrong" : "ABorder"];
        card.Background = (Brush)Application.Current.Resources[isActive ? "AAccentSoft" : "ASurfaceSoft"];

        var grid = card.Child as Grid;
        var stack = grid?.Children[0] as StackPanel;
        if (stack?.Children[0] is TextBlock label)
        {
            label.Text = isActive ? "使用中" : "テンプレート";
            label.Foreground = (Brush)Application.Current.Resources[isActive ? "APrimary" : "ATextDim"];
        }
    }
}
