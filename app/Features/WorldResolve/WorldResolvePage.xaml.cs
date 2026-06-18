using System;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Threading;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Features.WorldResolve;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class WorldResolvePage : Page
{
    private readonly WorldResolveViewModel viewModel;
    private CancellationTokenSource? cts;

    public Action? OnClose { get; set; }
    public Func<System.Threading.Tasks.Task>? OnApplied { get; set; }

    // ViewModel と一覧を接続し、初期表示を現在ステートに同期する。
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

    // ViewModel の状態変化を UI スレッドへ戻して画面表示に反映する。
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(SyncUi);
    }

    // 読み込み状態、件数、候補ピッカー表示を ViewModel の現在値へ同期する。
    private void SyncUi()
    {
        LoadingOverlay.Visibility = viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SyncLoadingProgress();

        var hasItems = viewModel.Items.Count > 0;
        ItemsListView.Visibility = hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = viewModel.IsLoading ? FormatLoadingStatus()
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

    // 対象件数が分かるまでは不定バー、取得後は処理済み件数で決定バーに切り替える。
    private void SyncLoadingProgress()
    {
        var total = viewModel.SearchProgressTotal;
        var processed = Math.Clamp(viewModel.SearchProgressProcessed, 0, Math.Max(total, 0));
        LoadingProgressBar.IsIndeterminate = total <= 0;
        LoadingProgressBar.Maximum = Math.Max(total, 1);
        LoadingProgressBar.Value = total > 0 ? processed : 0;
        LoadingProgressTitle.Text = total > 0 ? "候補を検索中..." : "対象写真を確認中...";
        LoadingProgressText.Text = viewModel.SearchProgressText;
    }

    private string FormatLoadingStatus()
        => viewModel.SearchProgressTotal > 0
            ? $"候補を検索中... {viewModel.SearchProgressText}"
            : viewModel.SearchProgressText;

    // 候補ピッカーで比較対象として表示する写真のサムネイルを差し替える。
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

    // 候補ピッカー以外の背景タップではワールド解析モーダルを閉じる。
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!viewModel.IsCandidatePickerOpen)
            OnClose?.Invoke();
    }

    // モーダル本文のタップを背景へ伝播させない。
    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    // Esc キーで候補ピッカーを閉じ、ピッカー外ではモーダル全体を閉じる。
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

    // 一覧内の全候補を適用対象にする。
    private void ApplyAll_Click(object sender, RoutedEventArgs e) => viewModel.ApplyAll();
    // 一覧内の全候補を適用対象から外す。
    private void SkipAll_Click(object sender, RoutedEventArgs e) => viewModel.SkipAll();

    // 個別の未解決写真について適用予定のオン/オフを切り替える。
    private void ToggleApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorldResolveItem item })
            viewModel.ToggleApply(item);
    }

    // 未解決写真の候補一覧を読み込み、候補ピッカーを開く。
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

    // 候補ピッカーで選ばれた候補を現在の未解決写真に割り当てる。
    private void CandidateItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CandidateEntry entry })
            viewModel.SelectCandidate(entry);
    }

    // 候補ピッカーを閉じ、未解決写真一覧へ戻る。
    private void BackFromPicker_Click(object sender, RoutedEventArgs e) => viewModel.CloseCandidatePicker();

    // 確認済みのワールド割り当てを保存し、ギャラリー更新後に閉じる。
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

    // 閉じるボタンからモーダルの終了通知を発火する。
    private void Close_Click(object sender, RoutedEventArgs e) => OnClose?.Invoke();
}
