using System;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// 写真詳細を全画面モーダルで表示する Page。
/// ShellPage によってインスタンスがキャッシュ・再利用されるため、UpdateViewModel で
/// 中身を差し替えて生成コストを抑える。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class PhotoModalPage : Page
{
    private PhotoModalViewModel viewModel;
    private UiObservableCollection<string>? masterTags;
    private PhotoThumbnailItem? subscribedPhoto;

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

    /// <summary>初期 ViewModel を受け取り、状態変更購読と DataContext を設定する。</summary>
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

    /// <summary>モーダル表示中に ViewModel を差し替え、状態変更の購読も付け替える。</summary>
    public void UpdateViewModel(PhotoModalViewModel next)
    {
        viewModel.state.PropertyChanged -= OnStatePropertyChanged;
        detachSelectedPhotoSubscription();
        viewModel = next;
        DataContext = next;
        next.state.PropertyChanged += OnStatePropertyChanged;
        syncSelectedPhotoSubscription();
        syncWorldName();
        syncMatchSource();
        syncEmptyTagNote();
        syncModalImage();
        syncPhotoEdgeButtons();
    }

    /// <summary>選択写真やタグの変更に合わせてモーダル表示を同期する。</summary>
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (PhotoModalPageLogic.ShouldSyncForPropertyChanged(e.PropertyName))
        {
            syncSelectedPhotoSubscription();
            syncWorldName();
            syncMatchSource();
            syncEmptyTagNote();
            syncModalImage();
        }

        if (e.PropertyName is nameof(PhotoModalState.CanGoPrev) or nameof(PhotoModalState.CanGoNext))
        {
            syncPhotoEdgeButtons();
        }
    }

    /// <summary>現在の SelectedPhoto へ PropertyChanged 購読を張り替える。</summary>
    private void syncSelectedPhotoSubscription()
    {
        var nextPhoto = viewModel.state.SelectedPhoto;
        if (ReferenceEquals(subscribedPhoto, nextPhoto)) return;

        detachSelectedPhotoSubscription();
        subscribedPhoto = nextPhoto;
        if (subscribedPhoto is not null)
        {
            subscribedPhoto.PropertyChanged += OnSelectedPhotoPropertyChanged;
        }
    }

    /// <summary>現在購読している SelectedPhoto から PropertyChanged を解除する。</summary>
    private void detachSelectedPhotoSubscription()
    {
        if (subscribedPhoto is not null)
        {
            subscribedPhoto.PropertyChanged -= OnSelectedPhotoPropertyChanged;
            subscribedPhoto = null;
        }
    }

    /// <summary>選択写真のタグ・ワールド・画像パス変更に合わせて派生 UI を再同期する。</summary>
    private void OnSelectedPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!PhotoModalPageLogic.ShouldSyncForSelectedPhotoProperty(e.PropertyName)) return;

        DispatcherQueue?.TryEnqueue(() =>
        {
            syncWorldName();
            syncMatchSource();
            syncEmptyTagNote();
            if (e.PropertyName == nameof(PhotoThumbnailItem.EffectiveDisplayPath))
            {
                syncModalImage();
            }
        });
    }

    /// <summary>選択写真に合わせて表示画像の source とサイズ表記を更新する。</summary>
    private void syncModalImage()
    {
        try
        {
            var photo = viewModel.state.SelectedPhoto;
            var request = PhotoModalPageLogic.ModalImageRequest(photo?.EffectiveDisplayPath, Path.DirectorySeparatorChar);
            if (request is null)
            {
                ModalImage.Source = null;
                return;
            }
            // DecodePixelWidth を Modal の最大表示幅 (1920px) で頭打ちにする。
            // 設定しないと 4K 写真が約 50MB のメモリにフルデコードされ、Modal の開閉だけで
            // 数百 MB の一時メモリを使う。Modal レイアウト上はこれ以上のピクセルを使い切らない。
            ModalImage.Source = null;
            ModalImage.Source = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = request.DecodePixelWidth,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(request.NormalizedPath, UriKind.Absolute),
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalPage.syncModalImage: threw: {ex}");
        }
    }

    /// <summary>選択写真のワールド名表示を更新する。</summary>
    private void syncWorldName()
    {
        var photo = viewModel.state.SelectedPhoto;
        WorldNameText.Text = PhotoModalPageLogic.WorldNameText(photo?.WorldName);
    }

    /// <summary>ワールド情報の取得元表示を現在の match_source に合わせる。</summary>
    private void syncMatchSource()
    {
        var photo = viewModel.state.SelectedPhoto;
        var display = PhotoModalPageLogic.MatchSource(photo?.MatchSource);

        if (display.Visible)
        {
            MatchSourceLabel.Visibility = Visibility.Visible;
            MatchSourceLabel.Text = display.Label;
        }
        else
        {
            MatchSourceLabel.Visibility = Visibility.Collapsed;
            MatchSourceLabel.Text = string.Empty;
        }
    }

    /// <summary>タグ未設定時の補足表示を選択写真のタグ数に合わせる。</summary>
    private void syncEmptyTagNote()
    {
        var photo = viewModel.state.SelectedPhoto;
        var display = PhotoModalPageLogic.TagAddDisplay(masterTags, photo?.Tags);
        TagAddRow.Visibility = display.HasAvailable ? Visibility.Visible : Visibility.Collapsed;
        EmptyTagNote.Visibility = display.HasAvailable ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>閉じるボタンからモーダルのクローズ要求を発行する。</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnClose?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.CloseButton_Click: {ex}"); }
    }

    /// <summary>背景タップでモーダルのクローズ要求を発行する。</summary>
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try { OnClose?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Backdrop_Tapped: {ex}"); }
    }

    /// <summary>モーダル本体のタップが背景タップ扱いにならないよう伝播を止める。</summary>
    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>ページ破棄時に購読を解除し、保持している画像参照を解放する。</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        viewModel.state.PropertyChanged -= OnStatePropertyChanged;
        detachSelectedPhotoSubscription();
    }

    /// <summary>ページ表示時に初期フォーカスと表示同期を行う。</summary>
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Focus(FocusState.Programmatic);
            viewModel.state.PropertyChanged -= OnStatePropertyChanged;
            viewModel.state.PropertyChanged += OnStatePropertyChanged;
            syncSelectedPhotoSubscription();
            syncWorldName();
            syncMatchSource();
            syncEmptyTagNote();
            syncModalImage();
            syncPhotoEdgeButtons();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Page_Loaded: {ex}"); }
    }

    /// <summary>Escape や左右キーで閉じる/前後移動/戻る操作を実行する。</summary>
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            var isTextInputFocused = FocusManager.GetFocusedElement(XamlRoot) is TextBox;
            switch (PhotoModalPageLogic.ResolveKeyAction(isTextInputFocused, e.Key))
            {
                case PhotoModalPageKeyAction.Close:
                    e.Handled = true;
                    OnClose?.Invoke();
                    break;
                case PhotoModalPageKeyAction.GoPrevious:
                    e.Handled = true;
                    OnGoPrev?.Invoke();
                    break;
                case PhotoModalPageKeyAction.GoNext:
                    e.Handled = true;
                    OnGoNext?.Invoke();
                    break;
                case PhotoModalPageKeyAction.GoBack:
                    e.Handled = true;
                    OnGoBack?.Invoke();
                    break;
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Page_KeyDown: {ex}"); }
    }

    /// <summary>類似検索履歴から前の写真へ戻る。</summary>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        try { OnGoBack?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BackButton_Click: {ex}"); }
    }


    /// <summary>選択写真の VRChat ワールドページを開く。</summary>
    private void PrevPhoto_Click(object sender, RoutedEventArgs e)
    {
        try { OnGoPrev?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PrevPhoto_Click: {ex}"); }
    }

    private void NextPhoto_Click(object sender, RoutedEventArgs e)
    {
        try { OnGoNext?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.NextPhoto_Click: {ex}"); }
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

    /// <summary>選択写真を Explorer 上で表示する。</summary>
    private async void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnOpenExplorer is not null)
                await OnOpenExplorer().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenExplorer_Click: {ex}"); }
    }

    /// <summary>選択写真とアクティブテンプレートから投稿 intent を開く。</summary>
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

    /// <summary>画像をクリップボードへ置いたことを示す一時オーバーレイを表示する。</summary>
    private void ShowClipboardOverlay()
    {
        try
        {
            AnimationHelper.FadeInOut(ClipboardOverlay, fadeInMs: 200, holdMs: 1200, fadeOutMs: 400);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ShowClipboardOverlay: {ex}"); }
    }

    /// <summary>選択写真のお気に入り状態を切り替える。</summary>
    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnToggleFavorite is not null)
                await OnToggleFavorite().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.Favorite_Click: {ex}"); }
    }

    /// <summary>既存タグボタンから選択写真へタグを追加する。</summary>
    private async void AddExistingTag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = PhotoModalPageLogic.AddExistingTagRequest(
                ExistingTagCombo.SelectedItem,
                viewModel.state.SelectedPhoto?.PhotoPath,
                OnAddTag is not null);
            if (request is not null)
            {
                await OnAddTag!(request.PhotoPath, request.Tag);
                ExistingTagCombo.SelectedIndex = -1;
                syncEmptyTagNote();
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.AddExistingTag_Click: {ex}"); }
    }

    /// <summary>タグマスタ画面の表示を要求する。</summary>
    private void OpenTagMaster_Click(object sender, RoutedEventArgs e)
    {
        try { OnOpenTagMaster?.Invoke(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenTagMaster_Click: {ex}"); }
    }

    /// <summary>モーダル画像の BitmapImage 参照を解放し、閉じた後のメモリ保持を避ける。</summary>
    public void ReleaseImage()
    {
        try
        {
            ModalImage.Source = null;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ReleaseImage: {ex}"); }
    }


    /// <summary>選択写真からクリックされたタグを削除する。</summary>
    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = PhotoModalPageLogic.RemoveTagRequest(
                (sender as FrameworkElement)?.Tag,
                viewModel.state.SelectedPhoto?.PhotoPath,
                OnRemoveTag is not null);
            if (request is null) return;
            await OnRemoveTag!(request.PhotoPath, request.Tag);
            syncEmptyTagNote();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.RemoveTag_Click: {ex}"); }
    }

    /// <summary>下部アクションボタンにポインタが乗ったときに hover 背景へ切り替える。</summary>
    private void BottomAction_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && ThemeHelper.Brush(btn, PhotoModalPageLogic.BottomActionHoverBrushKey) is { } hover)
                btn.Background = hover;
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BottomAction_PointerEntered: {ex}"); }
    }

    /// <summary>下部アクションボタンからポインタが外れたときに pressed 表示を解除する。</summary>
    private void BottomAction_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.BottomAction_PointerExited: {ex}"); }
    }

    private void PhotoEdgeButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button || !button.IsEnabled) return;

            if (ReferenceEquals(button, PrevPhotoButton))
            {
                ShowPhotoEdgeButton(button, previous: true);
                return;
            }

            if (ReferenceEquals(button, NextPhotoButton))
            {
                ShowPhotoEdgeButton(button, previous: false);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PhotoEdgeButton_PointerEntered: {ex}"); }
    }

    private void PhotoEdgeButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button button)
                ResetPhotoEdgeButton(button);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PhotoEdgeButton_PointerExited: {ex}"); }
    }

    private void ImagePanel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(ImagePanel).Position;
            var edgeWidth = Math.Max(96, Math.Min(PrevPhotoButton.ActualWidth, ImagePanel.ActualWidth * 0.14));

            if (point.X <= edgeWidth && PrevPhotoButton.IsEnabled)
            {
                ShowPhotoEdgeButton(PrevPhotoButton, previous: true);
                ResetPhotoEdgeButton(NextPhotoButton);
                return;
            }

            if (point.X >= ImagePanel.ActualWidth - edgeWidth && NextPhotoButton.IsEnabled)
            {
                ResetPhotoEdgeButton(PrevPhotoButton);
                ShowPhotoEdgeButton(NextPhotoButton, previous: false);
                return;
            }

            syncPhotoEdgeButtons();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ImagePanel_PointerMoved: {ex}"); }
    }

    private void ImagePanel_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try { syncPhotoEdgeButtons(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ImagePanel_PointerExited: {ex}"); }
    }

    private void syncPhotoEdgeButtons()
    {
        ResetPhotoEdgeButton(PrevPhotoButton);
        ResetPhotoEdgeButton(NextPhotoButton);
    }

    private void ShowPhotoEdgeButton(Button button, bool previous)
    {
        button.Background = BuildPhotoEdgeGradient(previous);
        if (ReferenceEquals(button, PrevPhotoButton))
            PrevPhotoChevron.Opacity = 1;
        else if (ReferenceEquals(button, NextPhotoButton))
            NextPhotoChevron.Opacity = 1;
    }

    private void ResetPhotoEdgeButton(Button button)
    {
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (ReferenceEquals(button, PrevPhotoButton))
            PrevPhotoChevron.Opacity = 0;
        else if (ReferenceEquals(button, NextPhotoButton))
            NextPhotoChevron.Opacity = 0;
    }

    private static LinearGradientBrush BuildPhotoEdgeGradient(bool previous)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = previous ? new Point(0, 0.5) : new Point(1, 0.5),
            EndPoint = previous ? new Point(1, 0.5) : new Point(0, 0.5),
        };
        brush.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(87, 0, 0, 0),
            Offset = 0,
        });
        brush.GradientStops.Add(new GradientStop
        {
            Color = Microsoft.UI.Colors.Transparent,
            Offset = 0.78,
        });
        return brush;
    }
}
