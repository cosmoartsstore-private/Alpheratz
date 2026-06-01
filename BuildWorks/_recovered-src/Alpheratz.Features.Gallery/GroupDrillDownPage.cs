using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Alpheratz.Core;
using Alpheratz.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.System;

namespace Alpheratz.Features.Gallery;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_GroupDrillDownPageWinRTTypeDetails))]
public sealed class GroupDrillDownPage : UserControl, IComponentConnector
{
	private readonly UiObservableCollection<PhotoGridItem> displayItems = new UiObservableCollection<PhotoGridItem>();

	private IReadOnlyList<PhotoThumbnailItem> photos = Array.Empty<PhotoThumbnailItem>();

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private PhotoGrid PhotoGridControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private EmptyState EmptyStateControl;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock GroupTitle;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button CloseButton;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	public Action? OnBack { get; set; }

	public Action<PhotoThumbnailItem>? OnPhotoActivated { get; set; }

	public Action<PhotoThumbnailItem>? OnFavoriteClicked { get; set; }

	public Action<IReadOnlyList<PhotoThumbnailItem>>? OnThumbnailsNeeded { get; set; }

	public IReadOnlyList<PhotoThumbnailItem> CurrentPhotos => photos;

	public GroupDrillDownPage()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"GroupDrillDownPage.ctor: InitializeComponent failed: {value}");
			throw;
		}
		try
		{
			PhotoGridControl.SetItemsSource(displayItems);
			PhotoGridControl.OnPhotoActivated = delegate(PhotoGridItem item)
			{
				PhotoThumbnailItem photoThumbnailItem = item?.Photo;
				if (photoThumbnailItem != null)
				{
					OnPhotoActivated?.Invoke(photoThumbnailItem);
				}
			};
			PhotoGridControl.OnFavoriteClicked = delegate(PhotoGridItem item)
			{
				PhotoThumbnailItem photoThumbnailItem = item?.Photo;
				if (photoThumbnailItem != null)
				{
					OnFavoriteClicked?.Invoke(photoThumbnailItem);
				}
			};
		}
		catch (Exception value2)
		{
			AppLogger.Error($"GroupDrillDownPage.ctor: wiring failed: {value2}");
			throw;
		}
	}

	public void SetGroupInfo(string groupName, IReadOnlyList<PhotoThumbnailItem> items)
	{
		try
		{
			GroupTitle.Text = $"{groupName}  ({items.Count}枚)";
			photos = items;
			PhotoGridItem[] items2 = items.Select((PhotoThumbnailItem p) => new PhotoGridItem
			{
				Photo = p
			}).ToArray();
			displayItems.ReplaceAll(items2);
			EmptyStateControl.Visibility = ((items.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			PhotoGridControl.Visibility = ((items.Count == 0) ? Visibility.Collapsed : Visibility.Visible);
			PhotoGridControl.ScrollToTop();
			if (items.Count > 0)
			{
				OnThumbnailsNeeded?.Invoke(items);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"GroupDrillDownPage.SetGroupInfo: threw: {value}");
		}
	}

	private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		OnBack?.Invoke();
	}

	private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		OnBack?.Invoke();
	}

	private void UserControl_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Escape)
		{
			OnBack?.Invoke();
			e.Handled = true;
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/Gallery/GroupDrillDownPage.xaml");
			Application.LoadComponent(this, resourceLocator, ComponentResourceLocation.Application);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			target.As<UserControl>().KeyDown += UserControl_KeyDown;
			break;
		case 2:
			target.As<Grid>().Tapped += Backdrop_Tapped;
			break;
		case 3:
			target.As<Border>().Tapped += ModalContent_Tapped;
			break;
		case 4:
			PhotoGridControl = target.As<PhotoGrid>();
			break;
		case 5:
			EmptyStateControl = target.As<EmptyState>();
			break;
		case 6:
			GroupTitle = target.As<TextBlock>();
			break;
		case 7:
			CloseButton = target.As<Button>();
			CloseButton.Click += CloseButton_Click;
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		return null;
	}
}
