using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Alpheratz.Features.TagMaster;

public sealed partial class TagMasterPage : Page
{
    private readonly TagMasterViewModel viewModel;

    public Func<Task>? OnCreateTag { get; set; }
    public Func<string, Task>? OnDeleteTag { get; set; }

    public TagMasterPage(TagMasterViewModel viewModel)
    {
        AppLogger.Trace("TagMasterPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"TagMasterPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        this.viewModel = viewModel;
        DataContext = viewModel;

        viewModel.masterTags.CollectionChanged += MasterTags_CollectionChanged;
        UpdateEmptyState();
        AppLogger.Trace("TagMasterPage.ctor: exit");
    }

    private void MasterTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyState);
    }

    private void UpdateEmptyState()
    {
        var isEmpty = viewModel.masterTags.Count == 0;
        TagEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TagInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            AddTag_Click(sender, e);
        }
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TagMasterPage.AddTag_Click: enter");
        try
        {
            if (OnCreateTag is not null)
            {
                await OnCreateTag().ConfigureAwait(false);
                AppLogger.Trace("TagMasterPage.AddTag_Click: exit (handler)");
                return;
            }

            await viewModel.createTag().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"TagMasterPage.AddTag_Click: threw: {ex}"); }
        AppLogger.Trace("TagMasterPage.AddTag_Click: exit");
    }

    private async void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Trace("TagMasterPage.DeleteTag_Click: enter");
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not string tag)
            {
                AppLogger.Trace("TagMasterPage.DeleteTag_Click: skip (no DataContext tag)");
                return;
            }

            if (OnDeleteTag is not null)
            {
                await OnDeleteTag(tag).ConfigureAwait(false);
                AppLogger.Trace("TagMasterPage.DeleteTag_Click: exit (handler)");
                return;
            }

            await viewModel.deleteTag(tag).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"TagMasterPage.DeleteTag_Click: threw: {ex}"); }
        AppLogger.Trace("TagMasterPage.DeleteTag_Click: exit");
    }
}
