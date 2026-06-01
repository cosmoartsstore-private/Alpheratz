using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;
using Windows.System;

namespace Alpheratz.Features.WorldResolve;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IUIElementOverrides")]
[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePageWinRTTypeDetails))]
public sealed class WorldResolvePage : Page, IComponentConnector
{
	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private interface IWorldResolvePage_Bindings
	{
		void Initialize();

		void Update();

		void StopTracking();

		void DisconnectUnloadedObject(int connectionId);
	}

	private interface IWorldResolvePage_BindingsScopeConnector
	{
		WeakReference Parent { get; set; }

		bool ContainsElement(int connectionId);

		void RegisterForElementConnection(int connectionId, IComponentConnector connector);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	private static class XamlBindingSetters
	{
		public static void Set_Microsoft_UI_Xaml_FrameworkElement_Tag(FrameworkElement obj, object value, string targetNullValue)
		{
			if (value == null && targetNullValue != null)
			{
				value = XamlBindingHelper.ConvertValue(typeof(object), targetNullValue);
			}
			obj.Tag = value;
		}

		public static void Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(TextBlock obj, string value, string targetNullValue)
		{
			if (value == null && targetNullValue != null)
			{
				value = targetNullValue;
			}
			obj.Text = value ?? string.Empty;
		}

		public static void Set_Microsoft_UI_Xaml_Controls_Image_Source(Image obj, ImageSource value, string targetNullValue)
		{
			if (value == null && targetNullValue != null)
			{
				value = (ImageSource)XamlBindingHelper.ConvertValue(typeof(ImageSource), targetNullValue);
			}
			obj.Source = value;
		}

		public static void Set_Microsoft_UI_Xaml_Controls_Primitives_ToggleButton_IsChecked(ToggleButton obj, bool? value, string targetNullValue)
		{
			if (!value.HasValue && targetNullValue != null)
			{
				value = (bool)XamlBindingHelper.ConvertValue(typeof(bool), targetNullValue);
			}
			obj.IsChecked = value;
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	[WinRTRuntimeClassName("Microsoft.UI.Xaml.IDataTemplateExtension")]
	[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePage_WorldResolvePage_obj9_BindingsWinRTTypeDetails))]
	private class WorldResolvePage_obj9_Bindings : IDataTemplateExtension, IDataTemplateComponent, IComponentConnector, IWorldResolvePage_Bindings
	{
		private CandidateEntry dataRoot;

		private bool initialized;

		private const int NOT_PHASED = int.MinValue;

		private const int DATA_CHANGED = 1073741824;

		private ResourceDictionary localResources;

		private WeakReference<FrameworkElement> converterLookupRoot;

		private bool removedDataContextHandler;

		private WeakReference obj9;

		private TextBlock obj10;

		private TextBlock obj11;

		private TextBlock obj12;

		private Image obj13;

		public void Connect(int connectionId, object target)
		{
			switch (connectionId)
			{
			case 9:
				obj9 = new WeakReference(target.As<Border>());
				break;
			case 10:
				obj10 = target.As<TextBlock>();
				break;
			case 11:
				obj11 = target.As<TextBlock>();
				break;
			case 12:
				obj12 = target.As<TextBlock>();
				break;
			case 13:
				obj13 = target.As<Image>();
				break;
			}
		}

		[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
		[DebuggerNonUserCode]
		public IComponentConnector GetBindingConnector(int connectionId, object target)
		{
			return null;
		}

		public void DataContextChangedHandler(FrameworkElement sender, DataContextChangedEventArgs args)
		{
			if (SetDataRoot(args.NewValue))
			{
				Update();
			}
		}

		public bool ProcessBinding(uint phase)
		{
			throw new NotImplementedException();
		}

		public int ProcessBindings(ContainerContentChangingEventArgs args)
		{
			int nextPhase = -1;
			ProcessBindings(args.Item, args.ItemIndex, (int)args.Phase, out nextPhase);
			return nextPhase;
		}

		public void ResetTemplate()
		{
			Recycle();
		}

		public void ProcessBindings(object item, int itemIndex, int phase, out int nextPhase)
		{
			nextPhase = -1;
			if (phase == 0)
			{
				nextPhase = -1;
				SetDataRoot(item);
				if (!removedDataContextHandler)
				{
					removedDataContextHandler = true;
					Border border = obj9.Target as Border;
					if (border != null)
					{
						border.DataContextChanged -= DataContextChangedHandler;
					}
				}
				initialized = true;
			}
			Update_(item.As<CandidateEntry>(), 1 << phase);
		}

		public void Recycle()
		{
		}

		public void Initialize()
		{
			if (!initialized)
			{
				Update();
			}
		}

		public void Update()
		{
			Update_(dataRoot, int.MinValue);
			initialized = true;
		}

		public void StopTracking()
		{
		}

		public void DisconnectUnloadedObject(int connectionId)
		{
			throw new ArgumentException("No unloadable elements to disconnect.");
		}

		public bool SetDataRoot(object newDataRoot)
		{
			if (newDataRoot != null)
			{
				dataRoot = newDataRoot.As<CandidateEntry>();
				return true;
			}
			return false;
		}

		public void SetConverterLookupRoot(FrameworkElement rootElement)
		{
			converterLookupRoot = new WeakReference<FrameworkElement>(rootElement);
		}

		public IValueConverter LookupConverter(string key)
		{
			if (localResources == null)
			{
				converterLookupRoot.TryGetTarget(out var target);
				localResources = target.Resources;
				converterLookupRoot = null;
			}
			return (IValueConverter)(localResources.ContainsKey(key) ? localResources[key] : Application.Current.Resources[key]);
		}

		private void Update_(CandidateEntry obj, int phase)
		{
			if (obj != null && (phase & -2147483647) != 0)
			{
				Update_Distance(obj.Distance, phase);
				Update_WorldName(obj.WorldName, phase);
				Update_PhotoFilename(obj.PhotoFilename, phase);
				Update_PhotoPath(obj.PhotoPath, phase);
			}
			if ((phase & -2147483647) != 0 && obj9.Target as Border != null)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_FrameworkElement_Tag(obj9.Target as Border, obj, null);
			}
		}

		private void Update_Distance(int obj, int phase)
		{
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj10, (string)LookupConverter("MatchPercentConverter").Convert(obj, typeof(string), null, null), null);
			}
		}

		private void Update_WorldName(string obj, int phase)
		{
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj11, obj, null);
			}
		}

		private void Update_PhotoFilename(string obj, int phase)
		{
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj12, obj, null);
			}
		}

		private void Update_PhotoPath(string obj, int phase)
		{
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_Image_Source(obj13, (ImageSource)LookupConverter("PickerThumbConverter").Convert(obj, typeof(ImageSource), null, null), null);
			}
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	[WinRTRuntimeClassName("Microsoft.UI.Xaml.IDataTemplateExtension")]
	[WinRTExposedType(typeof(Alpheratz_Features_WorldResolve_WorldResolvePage_WorldResolvePage_obj9_BindingsWinRTTypeDetails))]
	private class WorldResolvePage_obj25_Bindings : IDataTemplateExtension, IDataTemplateComponent, IComponentConnector, IWorldResolvePage_Bindings
	{
		[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
		[DebuggerNonUserCode]
		private class WorldResolvePage_obj25_BindingsTracking
		{
			private WeakReference<WorldResolvePage_obj25_Bindings> weakRefToBindingObj;

			public WorldResolvePage_obj25_BindingsTracking(WorldResolvePage_obj25_Bindings obj)
			{
				weakRefToBindingObj = new WeakReference<WorldResolvePage_obj25_Bindings>(obj);
			}

			public WorldResolvePage_obj25_Bindings TryGetBindingObject()
			{
				WorldResolvePage_obj25_Bindings target = null;
				if (weakRefToBindingObj != null)
				{
					weakRefToBindingObj.TryGetTarget(out target);
					if (target == null)
					{
						weakRefToBindingObj = null;
						ReleaseAllListeners();
					}
				}
				return target;
			}

			public void ReleaseAllListeners()
			{
				UpdateChildListeners_(null);
			}

			public void PropertyChanged_(object sender, PropertyChangedEventArgs e)
			{
				WorldResolvePage_obj25_Bindings worldResolvePage_obj25_Bindings = TryGetBindingObject();
				if (worldResolvePage_obj25_Bindings == null)
				{
					return;
				}
				string propertyName = e.PropertyName;
				WorldResolveItem worldResolveItem = sender as WorldResolveItem;
				if (string.IsNullOrEmpty(propertyName))
				{
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_IsApplied(worldResolveItem.IsApplied, 1073741824);
						worldResolvePage_obj25_Bindings.Update_MatchThumbPath(worldResolveItem.MatchThumbPath, 1073741824);
						worldResolvePage_obj25_Bindings.Update_MatchWorldName(worldResolveItem.MatchWorldName, 1073741824);
						worldResolvePage_obj25_Bindings.Update_MatchDistance(worldResolveItem.MatchDistance, 1073741824);
						worldResolvePage_obj25_Bindings.Update_TargetThumbPath(worldResolveItem.TargetThumbPath, 1073741824);
					}
					return;
				}
				switch (propertyName)
				{
				case "IsApplied":
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_IsApplied(worldResolveItem.IsApplied, 1073741824);
					}
					break;
				case "MatchThumbPath":
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_MatchThumbPath(worldResolveItem.MatchThumbPath, 1073741824);
					}
					break;
				case "MatchWorldName":
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_MatchWorldName(worldResolveItem.MatchWorldName, 1073741824);
					}
					break;
				case "MatchDistance":
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_MatchDistance(worldResolveItem.MatchDistance, 1073741824);
					}
					break;
				case "TargetThumbPath":
					if (worldResolveItem != null)
					{
						worldResolvePage_obj25_Bindings.Update_TargetThumbPath(worldResolveItem.TargetThumbPath, 1073741824);
					}
					break;
				}
			}

			public void UpdateChildListeners_(WorldResolveItem obj)
			{
				WorldResolvePage_obj25_Bindings worldResolvePage_obj25_Bindings = TryGetBindingObject();
				if (worldResolvePage_obj25_Bindings != null)
				{
					if (worldResolvePage_obj25_Bindings.dataRoot != null)
					{
						((INotifyPropertyChanged)worldResolvePage_obj25_Bindings.dataRoot).PropertyChanged -= PropertyChanged_;
					}
					if (obj != null)
					{
						worldResolvePage_obj25_Bindings.dataRoot = obj;
						((INotifyPropertyChanged)obj).PropertyChanged += PropertyChanged_;
					}
				}
			}
		}

		private WorldResolveItem dataRoot;

		private bool initialized;

		private const int NOT_PHASED = int.MinValue;

		private const int DATA_CHANGED = 1073741824;

		private ResourceDictionary localResources;

		private WeakReference<FrameworkElement> converterLookupRoot;

		private bool removedDataContextHandler;

		private WeakReference obj25;

		private ToggleButton obj26;

		private HyperlinkButton obj27;

		private Image obj28;

		private TextBlock obj29;

		private TextBlock obj30;

		private TextBlock obj31;

		private Image obj32;

		private WorldResolvePage_obj25_BindingsTracking bindingsTracking;

		public WorldResolvePage_obj25_Bindings()
		{
			bindingsTracking = new WorldResolvePage_obj25_BindingsTracking(this);
		}

		public void Connect(int connectionId, object target)
		{
			switch (connectionId)
			{
			case 25:
				obj25 = new WeakReference(target.As<Border>());
				break;
			case 26:
				obj26 = target.As<ToggleButton>();
				break;
			case 27:
				obj27 = target.As<HyperlinkButton>();
				break;
			case 28:
				obj28 = target.As<Image>();
				break;
			case 29:
				obj29 = target.As<TextBlock>();
				break;
			case 30:
				obj30 = target.As<TextBlock>();
				break;
			case 31:
				obj31 = target.As<TextBlock>();
				break;
			case 32:
				obj32 = target.As<Image>();
				break;
			}
		}

		[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
		[DebuggerNonUserCode]
		public IComponentConnector GetBindingConnector(int connectionId, object target)
		{
			return null;
		}

		public void DataContextChangedHandler(FrameworkElement sender, DataContextChangedEventArgs args)
		{
			if (SetDataRoot(args.NewValue))
			{
				Update();
			}
		}

		public bool ProcessBinding(uint phase)
		{
			throw new NotImplementedException();
		}

		public int ProcessBindings(ContainerContentChangingEventArgs args)
		{
			int nextPhase = -1;
			ProcessBindings(args.Item, args.ItemIndex, (int)args.Phase, out nextPhase);
			return nextPhase;
		}

		public void ResetTemplate()
		{
			Recycle();
		}

		public void ProcessBindings(object item, int itemIndex, int phase, out int nextPhase)
		{
			nextPhase = -1;
			if (phase == 0)
			{
				nextPhase = -1;
				SetDataRoot(item);
				if (!removedDataContextHandler)
				{
					removedDataContextHandler = true;
					Border border = obj25.Target as Border;
					if (border != null)
					{
						border.DataContextChanged -= DataContextChangedHandler;
					}
				}
				initialized = true;
			}
			Update_(item.As<WorldResolveItem>(), 1 << phase);
		}

		public void Recycle()
		{
			bindingsTracking.ReleaseAllListeners();
		}

		public void Initialize()
		{
			if (!initialized)
			{
				Update();
			}
		}

		public void Update()
		{
			Update_(dataRoot, int.MinValue);
			initialized = true;
		}

		public void StopTracking()
		{
			bindingsTracking.ReleaseAllListeners();
			initialized = false;
		}

		public void DisconnectUnloadedObject(int connectionId)
		{
			throw new ArgumentException("No unloadable elements to disconnect.");
		}

		public bool SetDataRoot(object newDataRoot)
		{
			bindingsTracking.ReleaseAllListeners();
			if (newDataRoot != null)
			{
				dataRoot = newDataRoot.As<WorldResolveItem>();
				return true;
			}
			return false;
		}

		public void SetConverterLookupRoot(FrameworkElement rootElement)
		{
			converterLookupRoot = new WeakReference<FrameworkElement>(rootElement);
		}

		public IValueConverter LookupConverter(string key)
		{
			if (localResources == null)
			{
				converterLookupRoot.TryGetTarget(out var target);
				localResources = target.Resources;
				converterLookupRoot = null;
			}
			return (IValueConverter)(localResources.ContainsKey(key) ? localResources[key] : Application.Current.Resources[key]);
		}

		private void Update_(WorldResolveItem obj, int phase)
		{
			bindingsTracking.UpdateChildListeners_(obj);
			if (obj != null)
			{
				if ((phase & -1073741823) != 0)
				{
					Update_IsApplied(obj.IsApplied, phase);
					Update_MatchThumbPath(obj.MatchThumbPath, phase);
					Update_MatchWorldName(obj.MatchWorldName, phase);
					Update_MatchDistance(obj.MatchDistance, phase);
				}
				if ((phase & -2147483647) != 0)
				{
					Update_TargetPhotoFilename(obj.TargetPhotoFilename, phase);
				}
				if ((phase & -1073741823) != 0)
				{
					Update_TargetThumbPath(obj.TargetThumbPath, phase);
				}
			}
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_FrameworkElement_Tag(obj26, obj, null);
				XamlBindingSetters.Set_Microsoft_UI_Xaml_FrameworkElement_Tag(obj27, obj, null);
			}
		}

		private void Update_IsApplied(bool obj, int phase)
		{
			if ((phase & -1073741823) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_Primitives_ToggleButton_IsChecked(obj26, obj, null);
			}
		}

		private void Update_MatchThumbPath(string obj, int phase)
		{
			if ((phase & -1073741823) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_Image_Source(obj28, (ImageSource)LookupConverter("ThumbConverter").Convert(obj, typeof(ImageSource), null, null), null);
			}
		}

		private void Update_MatchWorldName(string obj, int phase)
		{
			if ((phase & -1073741823) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj29, obj, null);
			}
		}

		private void Update_MatchDistance(int? obj, int phase)
		{
			if ((phase & -1073741823) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj30, (string)LookupConverter("MatchPercentConverter").Convert(obj, typeof(string), null, null), null);
			}
		}

		private void Update_TargetPhotoFilename(string obj, int phase)
		{
			if ((phase & -2147483647) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_TextBlock_Text(obj31, obj, null);
			}
		}

		private void Update_TargetThumbPath(string obj, int phase)
		{
			if ((phase & -1073741823) != 0)
			{
				XamlBindingSetters.Set_Microsoft_UI_Xaml_Controls_Image_Source(obj32, (ImageSource)LookupConverter("ThumbConverter").Convert(obj, typeof(ImageSource), null, null), null);
			}
		}
	}

	private readonly WorldResolveViewModel viewModel;

	private CancellationTokenSource? cts;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid MainView;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid CandidatePickerView;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ListView CandidateListView;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid CandidateLoadingOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock PickerTargetFilename;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Image PickerTargetImage;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private ListView ItemsListView;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private StackPanel EmptyState;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Grid LoadingOverlay;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock LoadingText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock ApplyCountText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private Button ApplyConfirmedBtn;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private TextBlock StatusText;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private IWorldResolvePage_Bindings Bindings;

	public Action? OnClose { get; set; }

	public Func<Task>? OnApplied { get; set; }

	public WorldResolvePage(WorldResolveViewModel viewModel)
	{
		InitializeComponent();
		this.viewModel = viewModel;
		base.DataContext = viewModel;
		ItemsListView.ItemsSource = viewModel.Items;
		CandidateListView.ItemsSource = viewModel.CandidateList;
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
		SyncUi();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		base.DispatcherQueue?.TryEnqueue(SyncUi);
	}

	private void SyncUi()
	{
		LoadingOverlay.Visibility = ((!viewModel.IsLoading) ? Visibility.Collapsed : Visibility.Visible);
		LoadingText.Text = viewModel.LoadingText;
		bool flag = viewModel.Items.Count > 0;
		ItemsListView.Visibility = ((!flag || viewModel.IsLoading) ? Visibility.Collapsed : Visibility.Visible);
		EmptyState.Visibility = ((flag || viewModel.IsLoading) ? Visibility.Collapsed : Visibility.Visible);
		StatusText.Text = (viewModel.IsLoading ? "検索中..." : $"{viewModel.Items.Count} 件の未解決写真");
		ApplyCountText.Text = $"{viewModel.ApplyCount} 件を適用予定";
		ApplyConfirmedBtn.Content = $"確認した {viewModel.ApplyCount} 件を適用";
		ApplyConfirmedBtn.IsEnabled = viewModel.ApplyCount > 0 && !viewModel.IsApplying;
		if (viewModel.IsCandidatePickerOpen)
		{
			MainView.Visibility = Visibility.Collapsed;
			CandidatePickerView.Visibility = Visibility.Visible;
			CandidateLoadingOverlay.Visibility = ((!viewModel.IsCandidateLoading) ? Visibility.Collapsed : Visibility.Visible);
			WorldResolveItem activePickerItem = viewModel.ActivePickerItem;
			if (activePickerItem != null)
			{
				PickerTargetFilename.Text = activePickerItem.TargetPhotoFilename;
				SetPickerTargetImage(activePickerItem.TargetThumbPath);
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
			UriSource = new Uri(path, UriKind.Absolute)
		};
	}

	private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e)
	{
		if (!viewModel.IsCandidatePickerOpen)
		{
			OnClose?.Invoke();
		}
	}

	private void ModalContent_Tapped(object sender, TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}

	private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Escape)
		{
			if (viewModel.IsCandidatePickerOpen)
			{
				viewModel.CloseCandidatePicker();
			}
			else
			{
				OnClose?.Invoke();
			}
			e.Handled = true;
		}
	}

	private void ApplyAll_Click(object sender, RoutedEventArgs e)
	{
		viewModel.ApplyAll();
	}

	private void SkipAll_Click(object sender, RoutedEventArgs e)
	{
		viewModel.SkipAll();
	}

	private void ToggleApply_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: WorldResolveItem tag })
		{
			viewModel.ToggleApply(tag);
		}
	}

	private async void OpenCandidatePicker_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: WorldResolveItem tag })
		{
			cts?.Cancel();
			cts = new CancellationTokenSource();
			try
			{
				await viewModel.OpenCandidatePickerAsync(tag, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception value)
			{
				AppLogger.Error($"WorldResolvePage.OpenCandidatePicker: {value}");
			}
		}
	}

	private void CandidateItem_Tapped(object sender, TappedRoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: CandidateEntry tag })
		{
			viewModel.SelectCandidate(tag);
		}
	}

	private void BackFromPicker_Click(object sender, RoutedEventArgs e)
	{
		viewModel.CloseCandidatePicker();
	}

	private async void ApplyConfirmed_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			if (await viewModel.ApplyConfirmedAsync().ConfigureAwait(continueOnCapturedContext: false) > 0 && OnApplied != null)
			{
				await OnApplied().ConfigureAwait(continueOnCapturedContext: false);
			}
			base.DispatcherQueue?.TryEnqueue(delegate
			{
				OnClose?.Invoke();
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldResolvePage.ApplyConfirmed: {value}");
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		OnClose?.Invoke();
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///Features/WorldResolve/WorldResolvePage.xaml");
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
			target.As<Page>().KeyDown += Page_KeyDown;
			break;
		case 2:
			target.As<Grid>().Tapped += Backdrop_Tapped;
			break;
		case 3:
			target.As<Border>().Tapped += ModalContent_Tapped;
			break;
		case 4:
			MainView = target.As<Grid>();
			break;
		case 5:
			CandidatePickerView = target.As<Grid>();
			break;
		case 6:
			CandidateListView = target.As<ListView>();
			break;
		case 7:
			CandidateLoadingOverlay = target.As<Grid>();
			break;
		case 9:
			target.As<Border>().Tapped += CandidateItem_Tapped;
			break;
		case 14:
			target.As<Button>().Click += BackFromPicker_Click;
			break;
		case 15:
			PickerTargetFilename = target.As<TextBlock>();
			break;
		case 16:
			PickerTargetImage = target.As<Image>();
			break;
		case 17:
			ItemsListView = target.As<ListView>();
			break;
		case 18:
			EmptyState = target.As<StackPanel>();
			break;
		case 19:
			LoadingOverlay = target.As<Grid>();
			break;
		case 20:
			LoadingText = target.As<TextBlock>();
			break;
		case 21:
			ApplyCountText = target.As<TextBlock>();
			break;
		case 22:
			ApplyConfirmedBtn = target.As<Button>();
			ApplyConfirmedBtn.Click += ApplyConfirmed_Click;
			break;
		case 23:
			target.As<Button>().Click += Close_Click;
			break;
		case 26:
			target.As<ToggleButton>().Click += ToggleApply_Click;
			break;
		case 27:
			target.As<HyperlinkButton>().Click += OpenCandidatePicker_Click;
			break;
		case 33:
			target.As<Button>().Click += ApplyAll_Click;
			break;
		case 34:
			target.As<Button>().Click += SkipAll_Click;
			break;
		case 35:
			StatusText = target.As<TextBlock>();
			break;
		}
		_contentLoaded = true;
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IComponentConnector GetBindingConnector(int connectionId, object target)
	{
		IComponentConnector result = null;
		switch (connectionId)
		{
		case 9:
		{
			Border border2 = (Border)target;
			WorldResolvePage_obj9_Bindings worldResolvePage_obj9_Bindings = new WorldResolvePage_obj9_Bindings();
			result = worldResolvePage_obj9_Bindings;
			worldResolvePage_obj9_Bindings.SetDataRoot(border2.DataContext);
			worldResolvePage_obj9_Bindings.SetConverterLookupRoot(this);
			border2.DataContextChanged += worldResolvePage_obj9_Bindings.DataContextChangedHandler;
			DataTemplate.SetExtensionInstance(border2, worldResolvePage_obj9_Bindings);
			XamlBindingHelper.SetDataTemplateComponent(border2, worldResolvePage_obj9_Bindings);
			break;
		}
		case 25:
		{
			Border border = (Border)target;
			WorldResolvePage_obj25_Bindings worldResolvePage_obj25_Bindings = new WorldResolvePage_obj25_Bindings();
			result = worldResolvePage_obj25_Bindings;
			worldResolvePage_obj25_Bindings.SetDataRoot(border.DataContext);
			worldResolvePage_obj25_Bindings.SetConverterLookupRoot(this);
			border.DataContextChanged += worldResolvePage_obj25_Bindings.DataContextChangedHandler;
			DataTemplate.SetExtensionInstance(border, worldResolvePage_obj25_Bindings);
			XamlBindingHelper.SetDataTemplateComponent(border, worldResolvePage_obj25_Bindings);
			break;
		}
		}
		return result;
	}
}
