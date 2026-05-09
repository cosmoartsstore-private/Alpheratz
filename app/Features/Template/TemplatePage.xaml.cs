using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Features.Template;

public sealed partial class TemplatePage : Page
{
    private readonly TemplatePageViewModel viewModel;

    public Action? OnCancelEdit { get; set; }
    public Func<Task>? OnSaveTemplate { get; set; }
    public Action<string>? OnStartEdit { get; set; }
    public Action<string>? OnDeleteTemplate { get; set; }
    public Func<string, Task>? OnSelectTemplate { get; set; }

    public TemplatePage(TemplatePageViewModel viewModel)
    {
        AppLogger.Trace("TemplatePage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TemplatePage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        this.viewModel = viewModel;
        DataContext = viewModel;

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateEditorState();
        AppLogger.Trace("TemplatePage.ctor: exit");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TemplatePageViewModel.EditingTweetTemplate))
        {
            DispatcherQueue.TryEnqueue(UpdateEditorState);
        }
        else if (e.PropertyName is nameof(TemplatePageViewModel.ActiveTweetTemplate))
        {
            DispatcherQueue.TryEnqueue(RefreshCardVisuals);
        }
    }

    private void UpdateEditorState()
    {
        var isEditing = viewModel.EditingTweetTemplate is not null;
        CancelEditButton.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = isEditing ? "更新" : "登録";
        EditorModeLabel.Text = isEditing ? "テンプレート編集" : "新規テンプレート";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TemplatePage.CancelEdit_Click: enter");
        try
        {
            if (OnCancelEdit is not null)
            {
                OnCancelEdit();
            }
            else
            {
                viewModel.cancelEdit();
            }
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.CancelEdit_Click: threw: {ex}"); }
        AppLogger.Trace("TemplatePage.CancelEdit_Click: exit");
    }

    private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TemplatePage.SaveTemplate_Click: enter");
        try
        {
            if (OnSaveTemplate is not null)
            {
                await OnSaveTemplate().ConfigureAwait(false);
                AppLogger.Trace("TemplatePage.SaveTemplate_Click: exit (handler)");
                return;
            }

            viewModel.saveTemplateDraft();
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.SaveTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("TemplatePage.SaveTemplate_Click: exit");
    }

    private void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TemplatePage.EditTemplate_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is string template)
            {
                if (OnStartEdit is not null)
                {
                    OnStartEdit(template);
                }
                else
                {
                    viewModel.startEdit(template);
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.EditTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("TemplatePage.EditTemplate_Click: exit");
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TemplatePage.DeleteTemplate_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is string template)
            {
                if (OnDeleteTemplate is not null)
                {
                    OnDeleteTemplate(template);
                }
                else
                {
                    viewModel.deleteTemplate(template);
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.DeleteTemplate_Click: threw: {ex}"); }
        AppLogger.Trace("TemplatePage.DeleteTemplate_Click: exit");
    }

    private async void TemplateCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AppLogger.Trace("TemplatePage.TemplateCard_PointerPressed: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string template) return;

            if (OnSelectTemplate is not null)
            {
                await OnSelectTemplate(template).ConfigureAwait(false);
            }
            else
            {
                viewModel.ActiveTweetTemplate = template;
            }
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.TemplateCard_PointerPressed: threw: {ex}"); }
        AppLogger.Trace("TemplatePage.TemplateCard_PointerPressed: exit");
    }

    private void TemplateCard_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Border card) return;
            if (card.DataContext is not string template) return;
            ApplyCardStyle(card, template == viewModel.ActiveTweetTemplate);
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.TemplateCard_Loaded: threw: {ex}"); }
    }

    private void RefreshCardVisuals()
    {
        try
        {
            for (int i = 0; i < TemplateList.Items.Count; i++)
            {
                var container = TemplateList.ContainerFromIndex(i);
                if (container is null) continue;
                var card = FindChildBorder(container);
                if (card?.DataContext is string template)
                    ApplyCardStyle(card, template == viewModel.ActiveTweetTemplate);
            }
        }
        catch (Exception ex) { AppLogger.Error($"TemplatePage.RefreshCardVisuals: threw: {ex}"); }
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

    private static void ApplyCardStyle(Border card, bool isActive)
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
