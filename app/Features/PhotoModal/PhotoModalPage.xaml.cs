using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Shared.Animations;
using Alpheratz.Shared.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// 写真詳細を全画面モーダルで表示する Page。
/// テーマリソースを確実に更新するため、ShellPage が表示ごとに新しいインスタンスを生成する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class PhotoModalPage : Page
{
    private readonly PhotoModalViewModel viewModel;
    private UiObservableCollection<string>? masterTags;
    private PhotoThumbnailItem? subscribedPhoto;
    private Button? activePhotoEdgeButton;
    private readonly HashSet<string> pendingTagSelections = new(StringComparer.OrdinalIgnoreCase);

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
    /// <summary>エクスプローラーで写真ファイルの場所を開く。</summary>
    public Func<Task>? OnOpenExplorer { get; set; }
    /// <summary>投稿テンプレートに基づく画像コピーと X 投稿画面の起動。</summary>
    public Func<Task<bool>>? OnTweet { get; set; }
    /// <summary>お気に入りフラグの即時トグル。</summary>
    public Func<Task>? OnToggleFavorite { get; set; }
    /// <summary>タグ追加 (photoPath, tag)。</summary>
    public Func<string, string, Task>? OnAddTag { get; set; }
    /// <summary>複数タグ追加 (photoPath, tags)。</summary>
    public Func<string, IReadOnlyList<string>, Task>? OnAddTags { get; set; }
    /// <summary>タグ削除 (photoPath, tag)。</summary>
    public Func<string, string, Task>? OnRemoveTag { get; set; }
    /// <summary>タグマスタ画面への遷移（モーダルを閉じてから遷移する想定）。</summary>
    public Action? OnOpenTagMaster { get; set; }

    /// <summary>背面キー操作を止める PhotoModal 内側の overlay が表示されているかを返す。</summary>
    public bool HasBlockingInnerOverlayOpen => TagAddOverlay.Visibility == Visibility.Visible;

    /// <summary>タグ候補リストをコンボボックスに反映する。</summary>
    public void SetMasterTags(UiObservableCollection<string> tags)
    {
        try
        {
            masterTags = tags;
            syncEmptyTagNote();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PhotoModalPage.SetMasterTags: threw: {ex}");
        }
    }

    /// <summary>投稿テンプレートの選択状態に応じて投稿操作を有効化する。</summary>
    public void SetTweetAvailable(bool available)
    {
        TweetButton.IsEnabled = available;
        var helpText = getMsg(available
            ? "PhotoModalPage.postHelp"
            : "PhotoModalPage.postUnavailableHelp");
        ToolTipService.SetToolTip(TweetButton, helpText);
        AutomationProperties.SetHelpText(TweetButton, helpText);
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

    /// <summary>選択写真やタグの変更に合わせてモーダル表示を同期する。</summary>
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (PhotoModalPageLogic.ShouldSyncForPropertyChanged(e.PropertyName))
        {
            syncSelectedPhotoSubscription();
            syncWorldName();
            syncWorldAction();
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
            syncWorldAction();
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
            ModalImage.Source = null;
        }
    }

    /// <summary>選択写真のワールド名表示を更新する。</summary>
    private void syncWorldName()
    {
        var photo = viewModel.state.SelectedPhoto;
        WorldNameText.Text = PhotoModalPageLogic.WorldNameText(photo?.WorldName);
    }

    /// <summary>ワールド ID が未取得の写真では、リンクボタンを明確な非活性表示にする。</summary>
    private void syncWorldAction()
    {
        var photo = viewModel.state.SelectedPhoto;
        var display = PhotoModalPageLogic.WorldAction(photo?.WorldId);
        var foreground = themeBrush(display.ForegroundKey);

        WorldActionButton.IsEnabled = display.Enabled;
        WorldActionButton.Opacity = display.Opacity;
        WorldActionButton.Background = new SolidColorBrush(Colors.Transparent);
        WorldActionIcon.Foreground = foreground;
        WorldActionLabel.Foreground = foreground;
        WorldActionLabel.Text = display.Label;
        ToolTipService.SetToolTip(WorldActionButton, display.Tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WorldActionButton, display.Tooltip);
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
        AssignedTagEmptyNote.Visibility = photo?.Tags?.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        TagAddRow.Visibility = display.HasAvailable ? Visibility.Visible : Visibility.Collapsed;
        EmptyTagNote.Visibility = display.HasAvailable ? Visibility.Collapsed : Visibility.Visible;
        if (!display.HasAvailable)
            CloseTagAddModal();
        else if (TagAddOverlay.Visibility == Visibility.Visible)
            rebuildExistingTagRows();
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
            syncWorldAction();
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
            switch (PhotoModalPageLogic.ResolveKeyAction(
                hasBlockingInnerOverlayOpen: HasBlockingInnerOverlayOpen,
                isTextInputFocused: isTextInputFocused,
                key: e.Key))
            {
                case PhotoModalPageKeyAction.Suppress:
                    e.Handled = true;
                    break;
                case PhotoModalPageKeyAction.CloseInnerOverlay:
                    e.Handled = true;
                    CloseTagAddModal();
                    break;
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

    /// <summary>選択写真をエクスプローラーで表示する。</summary>
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
                var succeeded = await OnTweet().ConfigureAwait(false);
                if (succeeded)
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

    /// <summary>選択した既存タグを選択写真へまとめて追加する。</summary>
    private async void AddExistingTag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var photoPath = viewModel.state.SelectedPhoto?.PhotoPath;
            var tags = pendingTagSelections.ToArray();
            if (!string.IsNullOrWhiteSpace(photoPath) && tags.Length > 0)
            {
                if (OnAddTags is not null)
                {
                    await OnAddTags(photoPath, tags);
                }
                else if (OnAddTag is not null)
                {
                    foreach (var tag in tags)
                        await OnAddTag(photoPath, tag);
                }
                CloseTagAddModal();
                syncEmptyTagNote();
            }
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.AddExistingTag_Click: {ex}"); }
    }

    /// <summary>タグ追加用の小モーダルを開き、追加候補の選択へフォーカスを移す。</summary>
    private void OpenTagAddModal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            pendingTagSelections.Clear();
            rebuildExistingTagRows();
            TagAddOverlay.Visibility = Visibility.Visible;
            TagAddConfirmButton.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.OpenTagAddModal_Click: {ex}"); }
    }

    private void CloseTagAddModal_Click(object sender, RoutedEventArgs e)
    {
        try { CloseTagAddModal(); }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.CloseTagAddModal_Click: {ex}"); }
    }

    private void TagAddOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            CloseTagAddModal();
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.TagAddOverlay_Tapped: {ex}"); }
    }

    private void TagAddModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>タグ追加モーダルを閉じ、未確定の選択を破棄する。</summary>
    private void CloseTagAddModal()
    {
        TagAddOverlay.Visibility = Visibility.Collapsed;
        pendingTagSelections.Clear();
        ExistingTagList.Children.Clear();
        TagAddConfirmButton.IsEnabled = false;
    }

    /// <summary>現在の写真へ未付与のタグだけを、複数選択行として再描画する。</summary>
    private void rebuildExistingTagRows()
    {
        ExistingTagList.Children.Clear();
        var currentTags = viewModel.state.SelectedPhoto?.Tags ?? [];
        var currentSet = currentTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = (masterTags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrEmpty(tag) && !currentSet.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.Create(new System.Globalization.CultureInfo("ja-JP"), false))
            .ToArray();

        if (choices.Length == 0)
        {
            ExistingTagList.Children.Add(new TextBlock
            {
                Text = getMsg("PhotoModalPage.noAvailableTags"),
                Foreground = themeBrush("ATextFaint"),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(8, 6, 8, 6),
            });
            TagAddConfirmButton.IsEnabled = false;
            return;
        }

        foreach (var tag in choices)
            ExistingTagList.Children.Add(createExistingTagChoiceButton(tag));

        TagAddConfirmButton.IsEnabled = pendingTagSelections.Count > 0;
    }

    /// <summary>写真詳細のタグ追加リストで使う選択行を作成する。</summary>
    private Button createExistingTagChoiceButton(string tag)
    {
        var selected = pendingTagSelections.Contains(tag);
        var checkBox = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(7),
            Background = selected ? themeBrush("APrimary") : themeBrush("ASurface"),
            BorderBrush = selected ? themeBrush("APrimary") : themeBrush("ABorder"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (selected)
        {
            checkBox.Child = new Alpheratz.Shared.Controls.AppIcon
            {
                IconName = "check",
                IconSize = 12,
                Foreground = themeBrush("ATextOnPrimary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var row = new Grid
        {
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(checkBox, 0);
        row.Children.Add(checkBox);

        var label = new TextBlock
        {
            Text = tag,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = selected ? themeBrush("APrimary") : themeBrush("ATextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        var button = new Button
        {
            Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Child = row,
            },
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(
            button,
            getMsg(
                selected
                    ? "PhotoModalPage.tagChoiceDeselectAutomation"
                    : "PhotoModalPage.tagChoiceSelectAutomation",
                ("tag", tag)));
        AutomationProperties.SetHelpText(button, getMsg("PhotoModalPage.tagChoiceHelp"));
        button.Click += (_, _) =>
        {
            if (!pendingTagSelections.Add(tag))
                pendingTagSelections.Remove(tag);
            rebuildExistingTagRows();
        };
        return button;
    }

    private Brush themeBrush(string key)
        => ThemeHelper.Brush(this, key) ?? new SolidColorBrush(Colors.Transparent);

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
            if (sender is Button button)
                ShowPhotoEdgeVisual(button);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PhotoEdgeButton_PointerEntered: {ex}"); }
    }

    /// <summary>透明な端ホットゾーン上の移動でも、対応する端フェードとシェブロンを表示する。</summary>
    private void PhotoEdgeButton_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button button)
                ShowPhotoEdgeVisual(button);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PhotoEdgeButton_PointerMoved: {ex}"); }
    }

    private void PhotoEdgeButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Button button)
                HidePhotoEdgeVisual(button);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.PhotoEdgeButton_PointerExited: {ex}"); }
    }

    /// <summary>写真表示ボックスの左右端に入ったときだけ、端フェードとシェブロンを出す。</summary>
    private void ImagePanel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(ImagePanel).Position;
            const double edgeWidth = 96;

            if (point.X <= edgeWidth)
            {
                ShowPhotoEdgeVisual(PrevPhotoButton);
                return;
            }

            if (point.X >= ImagePanel.ActualWidth - edgeWidth)
            {
                ShowPhotoEdgeVisual(NextPhotoButton);
                return;
            }

            HidePhotoEdgeVisual(PrevPhotoButton);
            HidePhotoEdgeVisual(NextPhotoButton);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ImagePanel_PointerMoved: {ex}"); }
    }

    /// <summary>写真表示セクションから外れたとき、左右の端フェードとシェブロンを消す。</summary>
    private void ImagePanel_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            HidePhotoEdgeVisual(PrevPhotoButton);
            HidePhotoEdgeVisual(NextPhotoButton);
        }
        catch (Exception ex) { AppLogger.Error($"PhotoModalPage.ImagePanel_PointerExited: {ex}"); }
    }

    /// <summary>写真の差し替えや状態変更時に、端フェード表示を初期状態へ戻す。</summary>
    private void syncPhotoEdgeButtons()
    {
        activePhotoEdgeButton = null;
        HidePhotoEdgeVisual(PrevPhotoButton);
        HidePhotoEdgeVisual(NextPhotoButton);
    }

    private void ShowPhotoEdgeVisual(Button button)
    {
        if (ReferenceEquals(activePhotoEdgeButton, button)) return;

        if (ReferenceEquals(button, PrevPhotoButton))
        {
            activePhotoEdgeButton = button;
            NextPhotoEdgeVisual.Opacity = 0;
            PrevPhotoEdgeVisual.Opacity = 1;
            return;
        }

        if (ReferenceEquals(button, NextPhotoButton))
        {
            activePhotoEdgeButton = button;
            PrevPhotoEdgeVisual.Opacity = 0;
            NextPhotoEdgeVisual.Opacity = 1;
        }
    }

    private void HidePhotoEdgeVisual(Button button)
    {
        if (ReferenceEquals(activePhotoEdgeButton, button))
            activePhotoEdgeButton = null;

        if (ReferenceEquals(button, PrevPhotoButton))
        {
            PrevPhotoEdgeVisual.Opacity = 0;
        }
        else if (ReferenceEquals(button, NextPhotoButton))
        {
            NextPhotoEdgeVisual.Opacity = 0;
        }
    }
}
