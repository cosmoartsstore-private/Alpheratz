using System;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// 写真詳細を全画面モーダルで表示する Page。
/// ShellPage によってインスタンスがキャッシュ・再利用されるため、UpdateViewModel で
/// 中身を差し替えて生成コストを抑える。
/// </summary>
public sealed partial class PhotoModalPage : Page
{
    private PhotoModalViewModel viewModel;
    private UiObservableCollection<string>? masterTags;

    /// <summary>モーダルを閉じる操作のフック (× ボタン、背景クリック、ESC キー)。</summary>
    public Action? OnClose { get; set; }
    /// <summary>「戻る」操作 (履歴スタックを一段戻る)。</summary>
    public Action? OnGoBack { get; set; }
    /// <summary>前の写真へ移動。</summary>
    public Action? OnGoPrev { get; set; }
    /// <summary>次の写真へ移動。</summary>
    public Action? OnGoNext { get; set; }
    /// <summary>ワールドリンクを既定ブラウザで開く。</summary>
    public Func<Task>? OnOpenWorld { get; set; }
    /// <summary>エクスプローラで写真フォルダを開く。</summary>
    public Func<Task>? OnOpenExplorer { get; set; }
    /// <summary>ツイート投稿テンプレートのクリップボードコピー＋ X 起動。</summary>
    public Func<Task>? OnTweet { get; set; }
    /// <summary>お気に入りフラグの即時トグル。</summary>
    public Func<Task>? OnToggleFavorite { get; set; }
    /// <summary>タグ追加 (photoPath, tag)。</summary>
    public Func<string, string, Task>? OnAddTag { get; set; }
    /// <summary>タグ削除 (photoPath, tag)。</summary>
    public Func<string, string, Task>? OnRemoveTag { get; set; }
    /// <summary>タグマスタ画面への遷移（モーダルを閉じてから遷移する想定）。</summary>
    public Action? OnOpenTagMaster { get; set; }

    /// <summary>タグ候補リストをコンボボックスに反映する。</summary>
    public void SetMasterTags(UiObservableCollection<string> tags)
    {
        try
        {
            masterTags = tags;
            ExistingTagCombo.ItemsSource = tags;
            syncEmptyTagNote();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalPage.SetMasterTags: threw: {ex}");
        }
    }

    public PhotoModalPage(PhotoModalViewModel viewModel)
    {
        AppLogger.Trace("PhotoModalPage.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalPage.ctor: InitializeComponent failed: {ex}");
            throw;
        }
        this.viewModel = viewModel;
        DataContext = viewModel;

        viewModel.state.PropertyChanged += OnStatePropertyChanged;

        AppLogger.Trace("PhotoModalPage.ctor: exit");
    }

    public void UpdateViewModel(PhotoModalViewModel next)
    {
        viewModel.state.PropertyChanged -= OnStatePropertyChanged;
        viewModel = next;
        DataContext = next;
        next.state.PropertyChanged += OnStatePropertyChanged;
        syncWorldName();
        syncMatchSource();
        syncEmptyTagNote();
        syncModalImage();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoModalState.SelectedPhoto))
        {
            syncWorldName();
            syncMatchSource();
            syncEmptyTagNote();
            syncModalImage();
        }
    }

    private void syncModalImage()
    {
        try
        {
            var photo = viewModel.state.SelectedPhoto;
            var path = photo?.EffectiveDisplayPath;
            if (string.IsNullOrEmpty(path))
            {
                ModalImage.Source = null;
                return;
            }
            // DB の正規化済みパスは forward-slash を含む可能性があるため、
            // BitmapImage に渡す前にネイティブのディレクトリセパレータへ変換する
            // (ThumbnailService.GenerateThumbnailAsync と同じ正規化)。
            path = path.Replace('/', System.IO.Path.DirectorySeparatorChar);
            // DecodePixelWidth を Modal の最大表示幅 (1920px) で頭打ちにする。
            // 設定しないと 4K 写真が約 50MB のメモリにフルデコードされ、Modal の開閉だけで
            // 数百 MB の一時メモリを使う。Modal レイアウト上はこれ以上のピクセルを使い切らない。
            ModalImage.Source = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = 1920,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(path, UriKind.Absolute),
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalPage.syncModalImage: threw: {ex}");
        }
    }

    private void syncWorldName()
    {
        var photo = viewModel.state.SelectedPhoto;
        var worldName = photo?.WorldName;
        WorldNameText.Text = string.IsNullOrEmpty(worldName) ? "ワールド不明" : worldName;
    }

    private void syncMatchSource()
    {
        var photo = viewModel.state.SelectedPhoto;
        var source = photo?.MatchSource;

        string? label = source switch
        {
            "polaris_archive" => "archive ログから補完",
            "phash" => "類似写真から推測",
            _ => null,
        };

        if (label is not null)
        {
            MatchSourcePanel.Visibility = Visibility.Visible;
            MatchSourceLabel.Text = label;
        }
        else
        {
            MatchSourcePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void syncEmptyTagNote()
    {
        var photo = viewModel.state.SelectedPhoto;
        var currentTags = photo?.Tags;
        var availableCount = 0;

        if (masterTags is not null && currentTags is not null)
        {
            foreach (var tag in masterTags)
            {
                if (!currentTags.Contains(tag))
                    availableCount++;
            }
        }
        else if (masterTags is not null)
        {
            availableCount = masterTags.Count;
        }

        var hasAvailable = availableCount > 0;
        TagAddRow.Visibility = hasAvailable ? Visibility.Visible : Visibility.Collapsed;
        EmptyTagNote.Visibility = hasAvailable ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnClose?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.CloseButton_Click: {ex}"); }
    }

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try { OnClose?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Backdrop_Tapped: {ex}"); }
    }

    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        viewModel.state.PropertyChanged -= OnStatePropertyChanged;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Focus(FocusState.Programmatic);
            syncWorldName();
            syncMatchSource();
            syncEmptyTagNote();
            syncModalImage();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Page_Loaded: {ex}"); }
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is TextBox)
                return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    OnClose?.Invoke();
                    break;
                case Windows.System.VirtualKey.Left:
                    e.Handled = true;
                    OnGoPrev?.Invoke();
                    break;
                case Windows.System.VirtualKey.Right:
                    e.Handled = true;
                    OnGoNext?.Invoke();
                    break;
                case Windows.System.VirtualKey.Back:
                    e.Handled = true;
                    OnGoBack?.Invoke();
                    break;
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Page_KeyDown: {ex}"); }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnGoBack?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BackButton_Click: {ex}"); }
    }


    private async void OpenWorld_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnOpenWorld is not null)
                await OnOpenWorld().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenWorld_Click: {ex}"); }
    }

    private async void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnOpenExplorer is not null)
                await OnOpenExplorer().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenExplorer_Click: {ex}"); }
    }

    private async void Tweet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnTweet is not null)
            {
                await OnTweet().ConfigureAwait(false);
                DispatcherQueue?.TryEnqueue(ShowClipboardOverlay);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Tweet_Click: {ex}"); }
    }

    private void ShowClipboardOverlay()
    {
        try
        {
            AnimationHelper.FadeInOut(ClipboardOverlay, fadeInMs: 200, holdMs: 1200, fadeOutMs: 400);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ShowClipboardOverlay: {ex}"); }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnToggleFavorite is not null)
                await OnToggleFavorite().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Favorite_Click: {ex}"); }
    }

    private async void AddExistingTag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ExistingTagCombo.SelectedItem is string tag && !string.IsNullOrEmpty(tag))
            {
                var photoPath = viewModel.state.SelectedPhoto?.PhotoPath;
                if (photoPath is not null && OnAddTag is not null)
                {
                    await OnAddTag(photoPath, tag);
                    ExistingTagCombo.SelectedIndex = -1;
                    syncEmptyTagNote();
                }
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.AddExistingTag_Click: {ex}"); }
    }

    private void OpenTagMaster_Click(object sender, RoutedEventArgs e)
    {
        try { OnOpenTagMaster?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenTagMaster_Click: {ex}"); }
    }

    public void ReleaseImage()
    {
        try
        {
            ModalImage.Source = null;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ReleaseImage: {ex}"); }
    }


    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            var photoPath = viewModel.state.SelectedPhoto?.PhotoPath;
            if (string.IsNullOrEmpty(tag) || photoPath is null || OnRemoveTag is null) return;
            await OnRemoveTag(photoPath, tag);
            syncEmptyTagNote();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.RemoveTag_Click: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // Bottom action button hover effects
    // -----------------------------------------------------------------------

    private void BottomAction_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && ThemeHelper.Brush(btn, "ASurfaceHover") is { } hover)
                btn.Background = hover;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BottomAction_PointerEntered: {ex}"); }
    }

    private void BottomAction_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BottomAction_PointerExited: {ex}"); }
    }
}