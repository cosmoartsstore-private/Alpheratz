using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// グループ（ワールド）ドリルダウン表示モーダル。
/// メイン PhotoGrid と同一の共有コントロールを使うことで、カード寸法・サムネイル
/// 表示・お気に入り操作・ホバーアニメをメインと揃える。
///
/// 表示は ShellStage の中位モーダルレイヤ (ModalContent) に重ねる。
/// 写真をクリックすると ShellPage が PhotoModal を最上位レイヤ (TopModalContent) に
/// 重ねて 2 段スタック表示する。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public sealed partial class GroupDrillDownPage : UserControl
{
    /// <summary>戻るボタン / 背景クリック / ESC / × ボタンで発火する閉じるコールバック。</summary>
    public Action? OnBack { get; set; }
    /// <summary>写真カードがクリックされたとき。ShellPage が PhotoModal を開く。</summary>
    public Action<PhotoThumbnailItem>? OnPhotoActivated { get; set; }
    /// <summary>お気に入り星クリック時。</summary>
    public Action<PhotoThumbnailItem>? OnFavoriteClicked { get; set; }
    /// <summary>サムネイル未生成の写真について生成を要求するためのコールバック。</summary>
    public Action<IReadOnlyList<PhotoThumbnailItem>>? OnThumbnailsNeeded { get; set; }

    private readonly UiObservableCollection<PhotoGridItem> displayItems = [];
    private IReadOnlyList<PhotoThumbnailItem> photos = [];

    public IReadOnlyList<PhotoThumbnailItem> CurrentPhotos => photos;

    // ドリルダウン用の標準グリッドを初期化し、写真クリックとお気に入り操作を中継する。
    public GroupDrillDownPage()
    {
        AppLogger.Trace("GroupDrillDownPage.ctor: enter");
        try { InitializeComponent(); }
        catch (Exception ex) { AppLogger.Error($"GroupDrillDownPage.ctor: InitializeComponent failed: {ex}"); throw; }

        try
        {
            PhotoGridControl.SetItemsSource(displayItems);
            PhotoGridControl.OnPhotoActivated = item =>
            {
                if (item?.Photo is { } p) OnPhotoActivated?.Invoke(p);
            };
            PhotoGridControl.OnFavoriteClicked = item =>
            {
                if (item?.Photo is { } p) OnFavoriteClicked?.Invoke(p);
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GroupDrillDownPage.ctor: wiring failed: {ex}");
            throw;
        }
        AppLogger.Trace("GroupDrillDownPage.ctor: exit");
    }

    // 対象グループ名と写真一覧を表示へ反映し、必要なサムネイル生成を要求する。
    public void SetGroupInfo(string groupName, IReadOnlyList<PhotoThumbnailItem> items)
    {
        AppLogger.Trace($"GroupDrillDownPage.SetGroupInfo: enter name={groupName} count={items.Count}");
        try
        {
            GroupTitle.Text = $"{groupName}  ({items.Count}枚)";
            photos = items;
            var wrapped = items.Select(p => new PhotoGridItem { Photo = p }).ToArray();
            displayItems.ReplaceAll(wrapped);
            EmptyStateControl.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PhotoGridControl.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PhotoGridControl.ScrollToTop();

            if (items.Count > 0)
                OnThumbnailsNeeded?.Invoke(items);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GroupDrillDownPage.SetGroupInfo: threw: {ex}");
        }
        AppLogger.Trace("GroupDrillDownPage.SetGroupInfo: exit");
    }

    // -----------------------------------------------------------------------
    // モーダル開閉用ハンドラ
    // -----------------------------------------------------------------------

    /// <summary>背景 (Backdrop) クリックで閉じる。内側のクリックは ModalContent_Tapped で止める。</summary>
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
    {
        OnBack?.Invoke();
    }

    /// <summary>モーダル内側のクリックが背景に伝播するのを防ぐ。</summary>
    private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>× ボタンで閉じる。</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }

    /// <summary>ESC キーで閉じる。Page 上の他要素に Esc を奪われないよう e.Handled=true。</summary>
    private void UserControl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            OnBack?.Invoke();
            e.Handled = true;
        }
    }
}
