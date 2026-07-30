using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Settings;

/// <summary>
/// 設定モーダル。左サイドバーで 4 セクション (全般 / タグマスタ / 投稿テンプレート / クレジット) を
/// 切り替える。<see cref="SettingsCompositeViewModel"/> をルート DataContext として、
/// 各セクションは <c>{Binding Settings.X}</c> / <c>{Binding TagMaster.X}</c> /
/// <c>{Binding Template.X}</c> でネストアクセスする。
///
/// 表示は ShellStage の ModalContent スロット経由 (<see cref="ShellPage.ShowSettings"/>)。
/// 旧 <c>Stage.MainContent</c> 差し替え経路は廃止。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class SettingsPage : Page
{
    private readonly SettingsCompositeViewModel viewModel;
    private string activeSettingsSection = "general";
    private PhashProgressEvent worldAnalysisProgress = PhashProgressEvent.Empty;
    private bool worldAnalysisRunning;
    private bool worldAnalysisEnabled;

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
    public Func<bool, Task>? OnOpenWorldOnPostChanged { get; set; }
    /// <summary>テーマ切替 (true=Dark)。</summary>
    public Func<bool, Task<bool>>? OnThemeChanged { get; set; }
    /// <summary>「ワールド名の推測」操作から WorldResolve モーダルを開く。</summary>
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

    /// <summary>PDQ 解析が完了するまで、ワールド名の推測画面への遷移を無効化する。</summary>
    public void SetWorldAnalysisEnabled(bool enabled)
    {
        try
        {
            worldAnalysisEnabled = enabled;
            ApplyWorldAnalysisState();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SetWorldAnalysisEnabled: threw: {ex}"); }
    }

    /// <summary>PDQ 解析の進捗を、ワールド名の推測ボタンの待機表示へ反映する。</summary>
    public void SetWorldAnalysisProgress(PhashProgressEvent progress, bool isRunning, bool enabled)
    {
        try
        {
            worldAnalysisProgress = progress;
            worldAnalysisRunning = isRunning;
            worldAnalysisEnabled = enabled;
            ApplyWorldAnalysisState();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SetWorldAnalysisProgress: threw: {ex}"); }
    }

    private void ApplyWorldAnalysisState()
    {
        var display = SettingsPageLogic.WorldAnalysisProgress(
            worldAnalysisProgress.done,
            worldAnalysisProgress.total,
            worldAnalysisProgress.current,
            worldAnalysisRunning,
            worldAnalysisEnabled);

        StartWorldAnalysisButton.IsEnabled = worldAnalysisEnabled;
        StartWorldAnalysisButton.Opacity = worldAnalysisEnabled ? 1.0 : 0.55;
        WorldAnalysisProgressPanel.Visibility = display.ProgressVisible ? Visibility.Visible : Visibility.Collapsed;
        WorldAnalysisProgressBar.IsIndeterminate = display.IsIndeterminate;
        WorldAnalysisProgressBar.Maximum = display.Maximum;
        WorldAnalysisProgressBar.Value = display.Value;
        WorldAnalysisProgressText.Text = display.SummaryText;
        WorldAnalysisProgressDetail.Text = display.DetailText;
    }

    /// <summary>外部遷移から指定セクションを表示する。サイドバークリックと同じ切替処理を使う。</summary>
    public void ShowSection(string section)
    {
        try
        {
            if (SettingsPageLogic.IsKnownSection(section))
                ShowSettingsSection(section);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ShowSection: threw: {ex}"); }
    }

    /// <summary>設定モーダルの幅に合わせて、2カラム/1カラム配置を切り替える。</summary>
    private void SettingsContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            ApplySettingsLayout(e.NewSize.Width);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsContentGrid_SizeChanged: threw: {ex}"); }
    }

    private void ApplySettingsLayout(double contentWidth)
    {
        RightSettingsColumn.Width = new GridLength(0);
        SettingsContentGrid.ColumnSpacing = 0;
        SettingsContentGrid.RowSpacing = 28;

        Grid.SetRow(GeneralSection, 0);
        Grid.SetColumn(GeneralSection, 0);
        Grid.SetRow(TagMasterSection, 1);
        Grid.SetColumn(TagMasterSection, 0);
        Grid.SetRow(TemplateSection, 2);
        Grid.SetColumn(TemplateSection, 0);
        Grid.SetRow(CreditsSection, 3);
        Grid.SetColumn(CreditsSection, 0);
    }

    /// <summary>設定画面表示時にタグ一覧の空状態とテンプレートカード表示を同期する。</summary>
    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.TagMaster.masterTags.CollectionChanged += MasterTags_CollectionChanged;
            viewModel.Template.PropertyChanged += TemplateViewModel_PropertyChanged;
            viewModel.Settings.PropertyChanged += SettingsViewModel_PropertyChanged;
            ActualThemeChanged += OnActualThemeChanged;
            UpdateTagEmptyState();
            UpdateEditorState();
            UpdateThemeSwitchVisual();
            ShowSettingsSection(activeSettingsSection);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsPage_Loaded: threw: {ex}"); }
    }

    /// <summary>設定画面破棄時にイベント購読とコールバック参照を解除する。</summary>
    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            viewModel.TagMaster.masterTags.CollectionChanged -= MasterTags_CollectionChanged;
            viewModel.Template.PropertyChanged -= TemplateViewModel_PropertyChanged;
            viewModel.Settings.PropertyChanged -= SettingsViewModel_PropertyChanged;
            ActualThemeChanged -= OnActualThemeChanged;
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsPage_Unloaded: threw: {ex}"); }
    }

    /// <summary>テーマ切替時に code-behind で着色したテンプレートカードを再描画する。</summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        try
        {
            RefreshTemplateCardVisuals();
            RefreshSettingsNavVisuals();
            UpdateThemeSwitchVisual();
            DispatcherQueue?.TryEnqueue(RefreshTemplateCardVisuals);
            DispatcherQueue?.TryEnqueue(RefreshSettingsNavVisuals);
            DispatcherQueue?.TryEnqueue(() => UpdateThemeSwitchVisual());
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.OnActualThemeChanged: {ex}"); }
    }

    private void SettingsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(viewModel.Settings.ThemeMode))
            DispatcherQueue?.TryEnqueue(() => UpdateThemeSwitchVisual());
    }

    /// <summary>左サイドバーから表示する設定セクションを切り替える。</summary>
    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.Tag is string section)
                ShowSettingsSection(section);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.SettingsNav_Click: threw: {ex}"); }
    }

    private void ShowSettingsSection(string section)
    {
        activeSettingsSection = section;
        GeneralSection.Visibility = section == "general" ? Visibility.Visible : Visibility.Collapsed;
        TagMasterSection.Visibility = section == "tags" ? Visibility.Visible : Visibility.Collapsed;
        TemplateSection.Visibility = section == "templates" ? Visibility.Visible : Visibility.Collapsed;
        CreditsSection.Visibility = section == "credits" ? Visibility.Visible : Visibility.Collapsed;
        RefreshSettingsNavVisuals();
    }

    private void RefreshSettingsNavVisuals()
    {
        ApplySettingsNavButtonStyle(GeneralNavButton, activeSettingsSection == "general");
        ApplySettingsNavButtonStyle(TagMasterNavButton, activeSettingsSection == "tags");
        ApplySettingsNavButtonStyle(TemplateNavButton, activeSettingsSection == "templates");
        ApplySettingsNavButtonStyle(CreditsNavButton, activeSettingsSection == "credits");
    }

    private static void ApplySettingsNavButtonStyle(Button button, bool active)
    {
        var fg = ThemeHelper.Brush(button, active ? "AText" : "ATextDim")
            ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Background = active
            ? ThemeHelper.Brush(button, "ASurfaceHover") ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Foreground = fg;

        if (button.Content is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is Alpheratz.Shared.Controls.AppIcon icon)
                    icon.Foreground = fg;
                else if (child is TextBlock text)
                    text.Foreground = fg;
            }
        }
    }

    /// <summary>背景タップで設定モーダルのクローズ要求を発行する。</summary>
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        OnClose?.Invoke();
    }

    /// <summary>モーダル本体のタップが背面閉じ処理へ伝播しないよう止める。</summary>
    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // モーダル内側のクリックが背景にバブルしないようにする (Backdrop_Tapped で閉じてしまうのを防ぐ)
        e.Handled = true;
    }

    /// <summary>閉じるボタンから設定モーダルのクローズ要求を発行する。</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        OnClose?.Invoke();
    }

    /// <summary>Escape キーで設定モーダルを閉じる。</summary>
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (SettingsPageLogic.ShouldCloseModal(e.Key))
        {
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    /// <summary>プライマリ写真フォルダの選択ダイアログを開く。</summary>
    private async void ChoosePrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(SettingsPageLogic.PrimaryFolderSlot).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChoosePrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChoosePrimaryFolder_Click: exit");
    }

    /// <summary>セカンダリ写真フォルダの選択ダイアログを開く。</summary>
    private async void ChooseSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: enter");
        try { if (OnChooseFolder is not null) await OnChooseFolder(SettingsPageLogic.SecondaryFolderSlot).ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ChooseSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ChooseSecondaryFolder_Click: exit");
    }

    /// <summary>プライマリ写真フォルダのリセット確認を要求する。</summary>
    private void ResetPrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(SettingsPageLogic.PrimaryFolderSlot); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetPrimaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetPrimaryFolder_Click: exit");
    }

    /// <summary>セカンダリ写真フォルダのリセット確認を要求する。</summary>
    private void ResetSecondaryFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: enter");
        try { OnResetFolder?.Invoke(SettingsPageLogic.SecondaryFolderSlot); }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ResetSecondaryFolder_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ResetSecondaryFolder_Click: exit");
    }

    /// <summary>自動起動トグルの変更を設定へ保存する。</summary>
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

    /// <summary>投稿画面を開くときのワールドリンク自動表示設定を保存する。</summary>
    private async void OpenWorldOnPostToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.OpenWorldOnPostToggle_Toggled: enter");
        try
        {
            if (sender is ToggleSwitch toggleSwitch && OnOpenWorldOnPostChanged is not null)
                await OnOpenWorldOnPostChanged(toggleSwitch.IsOn).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.OpenWorldOnPostToggle_Toggled: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.OpenWorldOnPostToggle_Toggled: exit");
    }

    /// <summary>現在テーマの逆側へ切り替えて保存する。</summary>
    private async void ThemeSwitch_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.ThemeSwitch_Click: enter");
        try
        {
            var nextDark = SettingsPageLogic.NextThemeIsDark(viewModel.Settings.ThemeMode == ThemeMode.dark);
            var themeChange = OnThemeChanged;
            if (themeChange is null)
            {
                UpdateThemeSwitchVisual(nextDark);
                return;
            }

            if (await themeChange(nextDark))
                viewModel.Settings.ThemeMode = nextDark ? ThemeMode.dark : ThemeMode.light;
            UpdateThemeSwitchVisual();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.ThemeSwitch_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.ThemeSwitch_Click: exit");
    }

    /// <summary>単一テーマボタンの表示名を現在テーマへ同期する。</summary>
    private void UpdateThemeSwitchVisual(bool? isDark = null)
    {
        var dark = isDark ?? viewModel.Settings.ThemeMode == ThemeMode.dark;
        ThemeSwitchButton.Content = SettingsPageLogic.ThemeButtonText(dark);
        ToolTipService.SetToolTip(ThemeSwitchButton, SettingsPageLogic.ThemeButtonTooltip(dark));
    }

    /// <summary>StellaRecord へ現在の実行ファイルを登録する。</summary>
    private void RegisterStellaRecord_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: enter");
        try
        {
            var request = SettingsPageLogic.StellaRecordRegistration(
                StellaRecordRegistration.IsStellaRecordAvailable(),
                Environment.ProcessPath,
                AppContext.BaseDirectory);
            if (request is null)
            {
                AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: StellaRecord not available");
                return;
            }
            StellaRecordRegistration.Register(request.ExePath, request.IconPath);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.RegisterStellaRecord_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.RegisterStellaRecord_Click: exit");
    }

    /// <summary>ワールド名の推測モーダルの表示を要求する。</summary>
    private async void StartWorldAnalysis_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: enter");
        try
        {
            if (!StartWorldAnalysisButton.IsEnabled) return;
            if (OnStartWorldAnalysis is not null) await OnStartWorldAnalysis().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.StartWorldAnalysis_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.StartWorldAnalysis_Click: exit");
    }

    /// <summary>タグマスタの件数変更を UI スレッドへ戻して空表示に反映する。</summary>
    private void MasterTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateTagEmptyState);
    }

    /// <summary>タグマスタ一覧の空表示を現在件数に合わせて切り替える。</summary>
    private void UpdateTagEmptyState()
    {
        TagEmptyState.Visibility = SettingsPageLogic.IsTagEmpty(viewModel.TagMaster.masterTags.Count)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>タグ入力欄の Enter キーでタグ追加を実行する。</summary>
    private void TagInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (SettingsPageLogic.ShouldSubmitTag(e.Key))
        {
            e.Handled = true;
            AddTag_Click(sender, e);
        }
    }

    /// <summary>入力中のタグをタグマスタへ追加する。</summary>
    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.AddTag_Click: enter");
        try
        {
            if (OnCreateTag is not null)
            {
                await OnCreateTag();
            }
            else
            {
                await viewModel.TagMaster.createTag();
            }
            UpdateTagEmptyState();
            TagInputBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.AddTag_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.AddTag_Click: exit");
    }

    /// <summary>クリックされたタグ削除ボタンに対応するタグを削除する。</summary>
    private async void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.DeleteTag_Click: enter");
        try
        {
            var tag = SettingsPageLogic.StringItem((sender as FrameworkElement)?.DataContext);
            if (tag is null) return;
            if (OnDeleteTag is not null)
            {
                await OnDeleteTag(tag);
            }
            else
            {
                await viewModel.TagMaster.deleteTag(tag);
            }
            UpdateTagEmptyState();
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.DeleteTag_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.DeleteTag_Click: exit");
    }

    /// <summary>テンプレート ViewModel の変更通知から編集欄またはカード表示を更新する。</summary>
    private void TemplateViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (SettingsPageLogic.TemplatePropertyAction(e.PropertyName))
        {
            case TemplatePropertyUpdate.UpdateEditor:
                DispatcherQueue.TryEnqueue(UpdateEditorState);
                break;
            case TemplatePropertyUpdate.RefreshCards:
                DispatcherQueue.TryEnqueue(RefreshTemplateCardVisuals);
                break;
        }
    }

    /// <summary>テンプレート編集モードに合わせてボタン表示と入力欄を更新する。</summary>
    private void UpdateEditorState()
    {
        var state = SettingsPageLogic.TemplateEditor(viewModel.Template.EditingTweetTemplate);
        CancelEditButton.Visibility = state.CancelVisible ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = state.SaveButtonText;
        EditorModeLabel.Text = state.ModeLabel;
    }

    /// <summary>テンプレート編集をキャンセルし、カード表示を更新する。</summary>
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

    /// <summary>テンプレートのドラフトを保存し、カード表示を更新する。</summary>
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

    /// <summary>選択されたテンプレートを編集モードへ移す。</summary>
    private void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.EditTemplate_Click: enter");
        try
        {
            var template = SettingsPageLogic.StringItem((sender as FrameworkElement)?.DataContext);
            if (template is null) return;
            if (OnStartEdit is not null) OnStartEdit(template);
            else viewModel.Template.startEdit(template);
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.EditTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.EditTemplate_Click: exit");
    }

    /// <summary>選択されたテンプレートを削除して保存する。</summary>
    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.DeleteTemplate_Click: enter");
        try
        {
            var template = SettingsPageLogic.StringItem((sender as FrameworkElement)?.DataContext);
            if (template is null) return;
            if (OnDeleteTemplate is not null)
            {
                await OnDeleteTemplate(template).ConfigureAwait(false);
            }
            else if (SettingsPageLogic.ShouldRemoveTemplateLocally(
                hasDeleteCallback: false,
                templateExists: viewModel.Template.tweetTemplates.Contains(template)))
            {
                AppLogger.Warn("SettingsPage.DeleteTemplate_Click: OnDeleteTemplate not wired; collection-only delete");
                viewModel.Template.tweetTemplates.Remove(template);
            }
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.DeleteTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.DeleteTemplate_Click: exit");
    }

    /// <summary>テンプレートカードをアクティブテンプレートとして選択する。</summary>
    private async void TemplateCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("SettingsPage.TemplateCard_PointerPressed: enter");
        try
        {
            var template = SettingsPageLogic.StringItem((sender as FrameworkElement)?.DataContext);
            if (template is null) return;
            if (OnSelectTemplate is not null)
                await OnSelectTemplate(template).ConfigureAwait(false);
            else
                viewModel.Template.ActiveTweetTemplate = template;
        }
        catch (Exception ex) { AppLogger.Error($"SettingsPage.TemplateCard_PointerPressed: threw: {ex}"); }
        AppLogger.Trace("SettingsPage.TemplateCard_PointerPressed: exit");
    }

    /// <summary>テンプレートカード読込時にアクティブ状態の見た目を適用する。</summary>
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

    /// <summary>表示中のテンプレートカードすべてのアクティブ表示を更新する。</summary>
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

    /// <summary>テンプレートカード内の Border を VisualTree から探す。</summary>
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

    /// <summary>テンプレートカードのアクティブ/通常スタイルを適用する。</summary>
    private static void ApplyTemplateCardStyle(Border card, bool isActive)
    {
        var state = SettingsPageLogic.TemplateCard(isActive);
        card.BorderBrush = ThemeHelper.Brush(card, state.BorderKey);
        card.Background = ThemeHelper.Brush(card, state.BackgroundKey);

        var grid = card.Child as Grid;
        var stack = grid?.Children[0] as StackPanel;
        if (stack?.Children[0] is TextBlock label)
        {
            label.Text = state.Label;
            label.Foreground = ThemeHelper.Brush(card, state.LabelForegroundKey);
        }
        if (stack?.Children[1] is TextBlock body)
            body.Foreground = ThemeHelper.Brush(card, state.BodyForegroundKey);
    }
}
