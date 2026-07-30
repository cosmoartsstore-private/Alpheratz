using System;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.WorldResolve;

[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class WorldResolvePage : Page
{
    private readonly WorldResolveViewModel viewModel;
    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource? candidateCts;
    private readonly object shutdownGate = new();
    private Task? shutdownTask;
    private int applyRequestInProgress;
    private int closeRequestInProgress;
    private bool isUnloaded;

    public Func<Task>? OnClose { get; set; }
    public Func<Task>? OnApplied { get; set; }

    // ViewModel と一覧を接続し、初期表示を現在ステートに同期する。
    public WorldResolvePage(WorldResolveViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;

        ItemsListView.ItemsSource = viewModel.Items;
        CandidateListView.ItemsSource = viewModel.CandidateList;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += Page_Unloaded;
        SyncUi();
    }

    /// <summary>このページが表示されている間だけ有効な解析用トークン。</summary>
    public CancellationToken LifetimeToken => lifetimeCts.Token;
    /// <summary>確定した DB 更新の途中で、外側のモーダルを閉じてよいか判断する。</summary>
    public bool IsApplying => viewModel.IsApplying;

    /// <summary>初期解析と候補読込を中断する。確定済み DB 更新の途中では呼ばない。</summary>
    public void CancelPendingOperations()
    {
        try { lifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { AppLogger.Warn($"WorldResolvePage.CancelPendingOperations: {ex.Message}"); }
        CancelCandidateOperations();
    }

    /// <summary>現在の候補読込を中断する。トークンは処理完了側で解放する。</summary>
    private void CancelCandidateOperations()
    {
        var current = Interlocked.Exchange(ref candidateCts, null);
        if (current is null) return;
        try { current.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { AppLogger.Warn($"WorldResolvePage.CancelCandidateOperations: {ex.Message}"); }
    }

    /// <summary>候補読込を中断し、候補ピッカーを閉じる。</summary>
    private void CloseCandidatePicker()
    {
        CancelCandidateOperations();
        viewModel.CloseCandidatePicker();
    }

    private async void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        try { await ShutdownAsync(); }
        catch (Exception ex) { AppLogger.Warn($"WorldResolvePage.Page_Unloaded: {ex.Message}"); }
    }

    // ViewModel の状態変化を UI スレッドへ戻して画面表示に反映する。
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isUnloaded)
            return;
        DispatcherQueue?.TryEnqueue(SyncUi);
    }

    // 読み込み状態、件数、候補ピッカー表示を ViewModel の現在値へ同期する。
    private void SyncUi()
    {
        if (isUnloaded)
            return;
        LoadingOverlay.Visibility = viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SyncLoadingProgress();

        var hasItems = viewModel.Items.Count > 0;
        ItemsListView.Visibility = hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !hasItems && !viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = viewModel.IsLoading ? FormatLoadingStatus()
            : getMsg("WorldResolvePage.unresolvedPhotoCount", ("count", viewModel.Items.Count));
        ApplyCountText.Text = getMsg("WorldResolvePage.applyCount", ("count", viewModel.ApplyCount));
        ApplyConfirmedBtn.Content = getMsg("WorldResolvePage.applyConfirmed", ("count", viewModel.ApplyCount));
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
        LoadingProgressTitle.Text = getMsg(total > 0
            ? "WorldResolvePage.searchingCandidates"
            : "WorldResolvePage.checkingTargetPhotos");
        LoadingProgressText.Text = viewModel.SearchProgressText;
    }

    private string FormatLoadingStatus()
        => viewModel.SearchProgressTotal > 0
            ? getMsg(
                "WorldResolvePage.searchingCandidatesStatus",
                ("progress", viewModel.SearchProgressText))
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
            RequestClose();
    }

    // モーダル本文のタップを背景へ伝播させない。
    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    // Esc キーで候補ピッカーを閉じ、ピッカー外ではモーダル全体を閉じる。
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (viewModel.IsCandidatePickerOpen)
                CloseCandidatePicker();
            else
                RequestClose();
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
        if (!isUnloaded && sender is FrameworkElement { Tag: WorldResolveItem item })
        {
            CancelCandidateOperations();
            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            Volatile.Write(ref candidateCts, requestCts);
            try { await viewModel.OpenCandidatePickerAsync(item, requestCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Error($"WorldResolvePage.OpenCandidatePicker: {ex}"); }
            finally
            {
                Interlocked.CompareExchange(ref candidateCts, null, requestCts);
                requestCts.Dispose();
            }
        }
    }

    // 候補ピッカーで選ばれた候補を現在の未解決写真に割り当てる。
    private void CandidateItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CandidateEntry entry })
        {
            CancelCandidateOperations();
            viewModel.SelectCandidate(entry, lifetimeCts.Token);
        }
    }

    // 候補ピッカーを閉じ、未解決写真一覧へ戻る。
    private void BackFromPicker_Click(object sender, RoutedEventArgs e) => CloseCandidatePicker();

    // 選択されたワールド割り当てを保存し、ギャラリー更新後に閉じる。
    private async void ApplyConfirmed_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref applyRequestInProgress, 1, 0) != 0)
            return;

        try
        {
            var count = await viewModel.ApplyConfirmedAsync().ConfigureAwait(false);
            if (count > 0 && OnApplied is not null)
                await OnApplied().ConfigureAwait(false);

            DispatcherQueue?.TryEnqueue(RequestClose);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolvePage.ApplyConfirmed: {ex}");
        }
        finally
        {
            Volatile.Write(ref applyRequestInProgress, 0);
        }
    }

    // 閉じるボタンからモーダルの終了通知を発火する。
    private void Close_Click(object sender, RoutedEventArgs e) => RequestClose();

    private async void RequestClose()
    {
        if (viewModel.IsApplying
            || Interlocked.CompareExchange(ref closeRequestInProgress, 1, 0) != 0)
            return;

        var close = OnClose;
        try
        {
            await ShutdownAsync();
            if (close is not null)
                await close();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorldResolvePage.RequestClose: {ex}");
        }
    }

    /// <summary>画面に属する処理を停止し、すべての生成完了を待ってから UI 参照を解放する。</summary>
    public Task ShutdownAsync()
    {
        lock (shutdownGate)
            return shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        if (isUnloaded)
            return;

        isUnloaded = true;
        CancelPendingOperations();
        await viewModel.StopGenerationAsync();

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ItemsListView.ItemsSource = null;
        CandidateListView.ItemsSource = null;
        DataContext = null;
        OnClose = null;
        OnApplied = null;
        lifetimeCts.Dispose();
        Unloaded -= Page_Unloaded;
    }
}
