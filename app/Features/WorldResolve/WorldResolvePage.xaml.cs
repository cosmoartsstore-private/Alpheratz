using System;
using System.ComponentModel;
using System.Threading;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Features.WorldResolve;

public sealed partial class WorldResolvePage : Page
{
    private readonly WorldResolveViewModel viewModel;
    private CancellationTokenSource? cts;

    public Action? OnClose { get; set; }
    public Func<System.Threading.Tasks.Task>? OnApplied { get; set; }

    public WorldResolvePage(WorldResolveViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;

        ItemsListView.ItemsSource = viewModel.Items;
        CandidateListView.ItemsSource = viewModel.CandidateList;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncUi();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(SyncUi);
    }

    private void SyncUi()
    {
        LoadingOverlay.Visibility = viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        var hasItems = viewModel.Items.Count > 0;
        ItemsListView.Visibility = hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = viewModel.IsLoading ? "検索中..."
            : $"{viewModel.Items.Count} 件の未解決写真";
        ApplyCountText.Text = $"{viewModel.ApplyCount} 件を適用予定";
        ApplyConfirmedBtn.Content = $"確認した {viewModel.ApplyCount} 件を適用";
        ApplyConfirmedBtn.IsEnabled = viewModel.ApplyCount > 0 && !viewModel.IsApplying;

        if (viewModel.IsCandidatePickerOpen)
        {
            MainView.Visibility = Visibility.Collapsed;
            CandidatePickerView.Visibility = Visibility.Visible;
            CandidateLoadingOverlay.Visibility = viewModel.IsCandidateLoading ? Visibility.Visible : Visibility.Collapsed;

            if (viewModel.ActivePickerItem is { } item)
            {
                PickerTargetFilename.Text = item.TargetPhotoFilename;
                SetPickerTargetImage(item.TargetThumbPath);
            }
        }
        else
        {
            MainView.Visibility = Visibility.Visible;
            CandidatePickerView.Visibility = Visibility.Collapsed;
        }
    }

    private void SetPickerTargetImage(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            PickerTargetImage.Source = null;
            return;
        }
        PickerTargetImage.Source = new BitmapImage
        {
            CreateOptions = BitmapCreateOptions.IgnoreImageCache,
            DecodePixelWidth = 120,
            DecodePixelType = DecodePixelType.Logical,
            UriSource = new Uri(path, UriKind.Absolute),
        };
    }

    // --- Event handlers ---

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!viewModel.IsCandidatePickerOpen)
            OnClose?.Invoke();
    }

    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (viewModel.IsCandidatePickerOpen)
                viewModel.CloseCandidatePicker();
            else
                OnClose?.Invoke();
            e.Handled = true;
        }
    }

    private void ApplyAll_Click(object sender, RoutedEventArgs e) => viewModel.ApplyAll();
    private void SkipAll_Click(object sender, RoutedEventArgs e) => viewModel.SkipAll();

    private void ToggleApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorldResolveItem item })
            viewModel.ToggleApply(item);
    }

    private async void OpenCandidatePicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorldResolveItem item })
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            try { await viewModel.OpenCandidatePickerAsync(item, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Error($"WorldResolvePage.OpenCandidatePicker: {ex}"); }
        }
    }

    private void CandidateItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CandidateEntry entry })
            viewModel.SelectCandidate(entry);
    }

    private void BackFromPicker_Click(object sender, RoutedEventArgs e) => viewModel.CloseCandidatePicker();

    private async void ApplyConfirmed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var count = await viewModel.ApplyConfirmedAsync().ConfigureAwait(false);
            if (count > 0 && OnApplied is not null)
                await OnApplied().ConfigureAwait(false);

            DispatcherQueue?.TryEnqueue(() => OnClose?.Invoke());
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolvePage.ApplyConfirmed: {ex}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => OnClose?.Invoke();
}
