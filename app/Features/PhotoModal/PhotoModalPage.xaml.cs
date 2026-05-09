using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Shared.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Alpheratz.Features.Gallery;
using System.ComponentModel;

namespace Alpheratz.Features.PhotoModal;

public sealed partial class PhotoModalPage : Page
{
    private PhotoModalViewModel viewModel;
    private UiObservableCollection<string>? masterTags;

    public Action? OnClose { get; set; }
    public Action? OnGoBack { get; set; }
    public Action? OnGoPrev { get; set; }
    public Action? OnGoNext { get; set; }
    public Func<Task>? OnSaveMemo { get; set; }
    public Func<Task>? OnOpenWorld { get; set; }
    public Func<Task>? OnOpenExplorer { get; set; }
    public Func<Task>? OnTweet { get; set; }
    public Func<Task>? OnToggleFavorite { get; set; }
    public Func<object?, Task>? OnApplySimilarWorldCandidate { get; set; }
    public Func<string, string, Task>? OnAddTag { get; set; }
    public Func<string, string, Task>? OnRemoveTag { get; set; }
    public Action? OnOpenTagMaster { get; set; }

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
        viewModel.similarWorldCandidates.CollectionChanged += OnSimilarCandidatesChanged;

        AppLogger.Trace("PhotoModalPage.ctor: exit");
    }

    public void UpdateViewModel(PhotoModalViewModel next)
    {
        viewModel.state.PropertyChanged -= OnStatePropertyChanged;
        viewModel.similarWorldCandidates.CollectionChanged -= OnSimilarCandidatesChanged;
        viewModel = next;
        DataContext = next;
        next.state.PropertyChanged += OnStatePropertyChanged;
        next.similarWorldCandidates.CollectionChanged += OnSimilarCandidatesChanged;
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
            ModalImage.Source = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(path, UriKind.Absolute) };
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


    private async void SaveMemo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnSaveMemo is not null)
                await OnSaveMemo().ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.SaveMemo_Click: {ex}"); }
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

    private async void ApplySimilarWorld_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OnApplySimilarWorldCandidate is null) return;
            var candidate = (sender as FrameworkElement)?.DataContext;
            await OnApplySimilarWorldCandidate(candidate).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ApplySimilarWorld_Click: {ex}"); }
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
                    await OnAddTag(photoPath, tag).ConfigureAwait(false);
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
            await OnRemoveTag(photoPath, tag).ConfigureAwait(false);
            syncEmptyTagNote();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.RemoveTag_Click: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // Similar photos strip slide-up animation
    // -----------------------------------------------------------------------

    private void OnSimilarCandidatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            var candidates = viewModel.similarWorldCandidates;
            if (candidates.Count > 0)
            {
                SimilarPhotosHint.Text = $"類似写真 ({candidates.Count}枚)";
                SimilarPhotosList.Children.Clear();

                foreach (var item in candidates)
                {
                    var thumb = new Button
                    {
                        Width = 64,
                        Height = 64,
                        Padding = new Thickness(0),
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        DataContext = item,
                    };
                    thumb.Click += ApplySimilarWorld_Click;

                    var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
                    {
                        CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
                        DecodePixelWidth = 64,
                        DecodePixelHeight = 64,
                        UriSource = new Uri(item.Photo.EffectiveDisplayPath ?? item.Photo.PhotoPath),
                    };
                    var img = new Microsoft.UI.Xaml.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    };
                    thumb.Content = img;
                    SimilarPhotosList.Children.Add(thumb);
                }

                if (SimilarPhotosStrip.Visibility != Visibility.Visible)
                {
                    SimilarPhotosStrip.Visibility = Visibility.Visible;
                    AnimationHelper.SlideUpFadeIn(SimilarPhotosStrip, fromY: 10f, durationMs: 250);
                }
            }
            else
            {
                SimilarPhotosStrip.Visibility = Visibility.Collapsed;
                SimilarPhotosList.Children.Clear();
                AnimationHelper.ResetVisual(SimilarPhotosStrip);
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OnSimilarCandidatesChanged: {ex}"); }
    }

    // -----------------------------------------------------------------------
    // Bottom action button hover effects
    // -----------------------------------------------------------------------

    private void BottomAction_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn)
                btn.Background = (Brush)Application.Current.Resources["ASurfaceSoft"];
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
