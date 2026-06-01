using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Gallery.Controls;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.Shell.Controls;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Shared.Controls;
using Alpheratz.Shared.Converters;
using Alpheratz.Shared.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.XamlTypeInfo;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Alpheratz.Alpheratz_Frontend_XamlTypeInfo;

[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
[DebuggerNonUserCode]
internal class XamlTypeInfoProvider
{
	private Dictionary<string, IXamlType> _xamlTypeCacheByName = new Dictionary<string, IXamlType>();

	private Dictionary<Type, IXamlType> _xamlTypeCacheByType = new Dictionary<Type, IXamlType>();

	private Dictionary<string, IXamlMember> _xamlMembers = new Dictionary<string, IXamlMember>();

	private string[] _typeNameTable;

	private Type[] _typeTable;

	private List<IXamlMetadataProvider> _otherProviders;

	private List<IXamlMetadataProvider> OtherProviders
	{
		get
		{
			if (_otherProviders == null)
			{
				List<IXamlMetadataProvider> list = new List<IXamlMetadataProvider>();
				IXamlMetadataProvider item = new XamlControlsXamlMetaDataProvider();
				list.Add(item);
				_otherProviders = list;
			}
			return _otherProviders;
		}
	}

	public IXamlType GetXamlTypeByType(Type type)
	{
		IXamlType value;
		lock (_xamlTypeCacheByType)
		{
			if (_xamlTypeCacheByType.TryGetValue(type, out value))
			{
				return value;
			}
			int num = LookupTypeIndexByType(type);
			if (num != -1)
			{
				value = CreateXamlType(num);
			}
			XamlUserType xamlUserType = value as XamlUserType;
			if (value == null || (xamlUserType != null && xamlUserType.IsReturnTypeStub && !xamlUserType.IsLocalType))
			{
				IXamlType xamlType = CheckOtherMetadataProvidersForType(type);
				if (xamlType != null && (xamlType.IsConstructible || value == null))
				{
					value = xamlType;
				}
			}
			if (value != null)
			{
				_xamlTypeCacheByName.Add(value.FullName, value);
				_xamlTypeCacheByType.Add(value.UnderlyingType, value);
			}
		}
		return value;
	}

	public IXamlType GetXamlTypeByName(string typeName)
	{
		if (string.IsNullOrEmpty(typeName))
		{
			return null;
		}
		IXamlType value;
		lock (_xamlTypeCacheByType)
		{
			if (_xamlTypeCacheByName.TryGetValue(typeName, out value))
			{
				return value;
			}
			int num = LookupTypeIndexByName(typeName);
			if (num != -1)
			{
				value = CreateXamlType(num);
			}
			XamlUserType xamlUserType = value as XamlUserType;
			if (value == null || (xamlUserType != null && xamlUserType.IsReturnTypeStub && !xamlUserType.IsLocalType))
			{
				IXamlType xamlType = CheckOtherMetadataProvidersForName(typeName);
				if (xamlType != null && (xamlType.IsConstructible || value == null))
				{
					value = xamlType;
				}
			}
			if (value != null)
			{
				_xamlTypeCacheByName.Add(value.FullName, value);
				_xamlTypeCacheByType.Add(value.UnderlyingType, value);
			}
		}
		return value;
	}

	public IXamlMember GetMemberByLongName(string longMemberName)
	{
		if (string.IsNullOrEmpty(longMemberName))
		{
			return null;
		}
		IXamlMember value;
		lock (_xamlMembers)
		{
			if (_xamlMembers.TryGetValue(longMemberName, out value))
			{
				return value;
			}
			value = CreateXamlMember(longMemberName);
			if (value != null)
			{
				_xamlMembers.Add(longMemberName, value);
			}
		}
		return value;
	}

	private void InitTypeTables()
	{
		_typeNameTable = new string[87];
		_typeNameTable[0] = "Microsoft.UI.Xaml.Controls.XamlControlsResources";
		_typeNameTable[1] = "Microsoft.UI.Xaml.ResourceDictionary";
		_typeNameTable[2] = "Object";
		_typeNameTable[3] = "Boolean";
		_typeNameTable[4] = "Alpheratz.Shared.Controls.AppIcon";
		_typeNameTable[5] = "Microsoft.UI.Xaml.Controls.UserControl";
		_typeNameTable[6] = "String";
		_typeNameTable[7] = "Double";
		_typeNameTable[8] = "Microsoft.UI.Xaml.Media.Brush";
		_typeNameTable[9] = "Alpheratz.Features.Bootstrap.BootstrapPage";
		_typeNameTable[10] = "Microsoft.UI.Xaml.Controls.Page";
		_typeNameTable[11] = "Alpheratz.Shared.Controls.AnimatedFavoriteStar";
		_typeNameTable[12] = "System.Action";
		_typeNameTable[13] = "System.MulticastDelegate";
		_typeNameTable[14] = "System.Delegate";
		_typeNameTable[15] = "Alpheratz.Shared.Controls.WrapPanel";
		_typeNameTable[16] = "Microsoft.UI.Xaml.Controls.Panel";
		_typeNameTable[17] = "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel";
		_typeNameTable[18] = "System.Action`1<String>";
		_typeNameTable[19] = "System.Action`1<Alpheratz.Shared.Models.SortMode>";
		_typeNameTable[20] = "System.Action`1<Alpheratz.Shared.Models.DisplayFolderMode>";
		_typeNameTable[21] = "System.Action`1<Alpheratz.Shared.Models.GroupingMode>";
		_typeNameTable[22] = "Alpheratz.Features.Gallery.Controls.GalleryFilterSidebar";
		_typeNameTable[23] = "Alpheratz.Features.Gallery.Controls.MonthNav";
		_typeNameTable[24] = "System.Action`1<Alpheratz.Features.Gallery.GalleryMonthGroup>";
		_typeNameTable[25] = "Alpheratz.Shared.Controls.PhotoGrid";
		_typeNameTable[26] = "System.Func`1<System.Threading.Tasks.Task>";
		_typeNameTable[27] = "System.Action`1<Double>";
		_typeNameTable[28] = "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>";
		_typeNameTable[29] = "System.Action`1<Int32>";
		_typeNameTable[30] = "Alpheratz.Features.Gallery.Controls.GalleryMasonryView";
		_typeNameTable[31] = "System.Action`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>";
		_typeNameTable[32] = "System.Action`1<System.Collections.Generic.IReadOnlyList`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>>";
		_typeNameTable[33] = "Alpheratz.Shared.Controls.EmptyState";
		_typeNameTable[34] = "Alpheratz.Features.Gallery.Controls.GalleryGridStage";
		_typeNameTable[35] = "System.Action`1<Alpheratz.Features.Gallery.Controls.GalleryMasonryView>";
		_typeNameTable[36] = "Microsoft.UI.Xaml.Visibility";
		_typeNameTable[37] = "Alpheratz.Features.Gallery.GalleryPage";
		_typeNameTable[38] = "System.Func`1<System.Threading.Tasks.Task`1<String>>";
		_typeNameTable[39] = "Alpheratz.Features.Gallery.GroupDrillDownPage";
		_typeNameTable[40] = "System.Collections.Generic.IReadOnlyList`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>";
		_typeNameTable[41] = "Alpheratz.Features.PhotoModal.PhotoModalPage";
		_typeNameTable[42] = "System.Func`3<String, String, System.Threading.Tasks.Task>";
		_typeNameTable[43] = "Alpheratz.Features.Settings.SettingsPage";
		_typeNameTable[44] = "System.Func`2<Int32, System.Threading.Tasks.Task>";
		_typeNameTable[45] = "System.Func`2<Boolean, System.Threading.Tasks.Task>";
		_typeNameTable[46] = "System.Func`2<String, System.Threading.Tasks.Task>";
		_typeNameTable[47] = "Alpheratz.Shared.Converters.IntToVisibilityConverter";
		_typeNameTable[48] = "Microsoft.UI.Xaml.Controls.ProgressRing";
		_typeNameTable[49] = "Microsoft.UI.Xaml.Controls.Control";
		_typeNameTable[50] = "Microsoft.UI.Xaml.Controls.ProgressRingTemplateSettings";
		_typeNameTable[51] = "Microsoft.UI.Xaml.DependencyObject";
		_typeNameTable[52] = "Alpheratz.Features.Shell.Controls.ShellHeaderBar";
		_typeNameTable[53] = "Alpheratz.Shared.Controls.ScanningOverlay";
		_typeNameTable[54] = "Alpheratz.Shared.Controls.ToastHost";
		_typeNameTable[55] = "Alpheratz.Features.Shell.Controls.ShellStage";
		_typeNameTable[56] = "Alpheratz.Features.Shell.ShellPage";
		_typeNameTable[57] = "Alpheratz.Shared.Converters.ThumbnailSourceConverter";
		_typeNameTable[58] = "Int32";
		_typeNameTable[59] = "Alpheratz.Shared.Converters.MatchPercentConverter";
		_typeNameTable[60] = "Alpheratz.Features.WorldResolve.WorldResolvePage";
		_typeNameTable[61] = "Alpheratz.MainWindow";
		_typeNameTable[62] = "Microsoft.UI.Xaml.Window";
		_typeNameTable[63] = "Microsoft.UI.Xaml.Media.RadialGradientBrush";
		_typeNameTable[64] = "Microsoft.UI.Xaml.Media.XamlCompositionBrushBase";
		_typeNameTable[65] = "Windows.Foundation.Collections.IObservableVector`1<Microsoft.UI.Xaml.Media.GradientStop>";
		_typeNameTable[66] = "Microsoft.UI.Xaml.Media.GradientStop";
		_typeNameTable[67] = "Windows.Foundation.Point";
		_typeNameTable[68] = "Microsoft.UI.Composition.CompositionColorSpace";
		_typeNameTable[69] = "System.Enum";
		_typeNameTable[70] = "System.ValueType";
		_typeNameTable[71] = "Microsoft.UI.Xaml.Media.BrushMappingMode";
		_typeNameTable[72] = "Microsoft.UI.Xaml.Media.GradientSpreadMethod";
		_typeNameTable[73] = "Alpheratz.Shared.Controls.CustomScrollbar";
		_typeNameTable[74] = "Alpheratz.Shared.Controls.PhotoGridItemsView";
		_typeNameTable[75] = "Microsoft.UI.Xaml.Controls.ScrollViewer";
		_typeNameTable[76] = "Alpheratz.Shared.Converters.BoolToVisibilityConverter";
		_typeNameTable[77] = "Microsoft.UI.Xaml.Controls.ProgressBar";
		_typeNameTable[78] = "Microsoft.UI.Xaml.Controls.Primitives.RangeBase";
		_typeNameTable[79] = "Microsoft.UI.Xaml.Controls.ProgressBarTemplateSettings";
		_typeNameTable[80] = "Alpheratz.Shared.Converters.ToastAccentBrushConverter";
		_typeNameTable[81] = "Alpheratz.Shared.Converters.ToastIconConverter";
		_typeNameTable[82] = "Microsoft.UI.Xaml.Controls.Button";
		_typeNameTable[83] = "Microsoft.UI.Xaml.Thickness";
		_typeNameTable[84] = "Microsoft.UI.Xaml.CornerRadius";
		_typeNameTable[85] = "Microsoft.UI.Xaml.Controls.TreeViewNode";
		_typeNameTable[86] = "System.Collections.Generic.IList`1<Microsoft.UI.Xaml.Controls.TreeViewNode>";
		_typeTable = new Type[87];
		_typeTable[0] = typeof(XamlControlsResources);
		_typeTable[1] = typeof(ResourceDictionary);
		_typeTable[2] = typeof(object);
		_typeTable[3] = typeof(bool);
		_typeTable[4] = typeof(AppIcon);
		_typeTable[5] = typeof(UserControl);
		_typeTable[6] = typeof(string);
		_typeTable[7] = typeof(double);
		_typeTable[8] = typeof(Brush);
		_typeTable[9] = typeof(BootstrapPage);
		_typeTable[10] = typeof(Page);
		_typeTable[11] = typeof(AnimatedFavoriteStar);
		_typeTable[12] = typeof(Action);
		_typeTable[13] = typeof(MulticastDelegate);
		_typeTable[14] = typeof(Delegate);
		_typeTable[15] = typeof(WrapPanel);
		_typeTable[16] = typeof(Panel);
		_typeTable[17] = typeof(GalleryFilterPanel);
		_typeTable[18] = typeof(Action<string>);
		_typeTable[19] = typeof(Action<SortMode>);
		_typeTable[20] = typeof(Action<DisplayFolderMode>);
		_typeTable[21] = typeof(Action<GroupingMode>);
		_typeTable[22] = typeof(GalleryFilterSidebar);
		_typeTable[23] = typeof(MonthNav);
		_typeTable[24] = typeof(Action<GalleryMonthGroup>);
		_typeTable[25] = typeof(PhotoGrid);
		_typeTable[26] = typeof(Func<Task>);
		_typeTable[27] = typeof(Action<double>);
		_typeTable[28] = typeof(Action<PhotoGridItem>);
		_typeTable[29] = typeof(Action<int>);
		_typeTable[30] = typeof(GalleryMasonryView);
		_typeTable[31] = typeof(Action<PhotoThumbnailItem>);
		_typeTable[32] = typeof(Action<IReadOnlyList<PhotoThumbnailItem>>);
		_typeTable[33] = typeof(EmptyState);
		_typeTable[34] = typeof(GalleryGridStage);
		_typeTable[35] = typeof(Action<GalleryMasonryView>);
		_typeTable[36] = typeof(Visibility);
		_typeTable[37] = typeof(GalleryPage);
		_typeTable[38] = typeof(Func<Task<string>>);
		_typeTable[39] = typeof(GroupDrillDownPage);
		_typeTable[40] = typeof(IReadOnlyList<PhotoThumbnailItem>);
		_typeTable[41] = typeof(PhotoModalPage);
		_typeTable[42] = typeof(Func<string, string, Task>);
		_typeTable[43] = typeof(SettingsPage);
		_typeTable[44] = typeof(Func<int, Task>);
		_typeTable[45] = typeof(Func<bool, Task>);
		_typeTable[46] = typeof(Func<string, Task>);
		_typeTable[47] = typeof(IntToVisibilityConverter);
		_typeTable[48] = typeof(ProgressRing);
		_typeTable[49] = typeof(Control);
		_typeTable[50] = typeof(ProgressRingTemplateSettings);
		_typeTable[51] = typeof(DependencyObject);
		_typeTable[52] = typeof(ShellHeaderBar);
		_typeTable[53] = typeof(ScanningOverlay);
		_typeTable[54] = typeof(ToastHost);
		_typeTable[55] = typeof(ShellStage);
		_typeTable[56] = typeof(ShellPage);
		_typeTable[57] = typeof(ThumbnailSourceConverter);
		_typeTable[58] = typeof(int);
		_typeTable[59] = typeof(MatchPercentConverter);
		_typeTable[60] = typeof(WorldResolvePage);
		_typeTable[61] = typeof(MainWindow);
		_typeTable[62] = typeof(Window);
		_typeTable[63] = typeof(RadialGradientBrush);
		_typeTable[64] = typeof(XamlCompositionBrushBase);
		_typeTable[65] = typeof(IObservableVector<GradientStop>);
		_typeTable[66] = typeof(GradientStop);
		_typeTable[67] = typeof(Point);
		_typeTable[68] = typeof(CompositionColorSpace);
		_typeTable[69] = typeof(Enum);
		_typeTable[70] = typeof(ValueType);
		_typeTable[71] = typeof(BrushMappingMode);
		_typeTable[72] = typeof(GradientSpreadMethod);
		_typeTable[73] = typeof(CustomScrollbar);
		_typeTable[74] = typeof(PhotoGridItemsView);
		_typeTable[75] = typeof(ScrollViewer);
		_typeTable[76] = typeof(BoolToVisibilityConverter);
		_typeTable[77] = typeof(ProgressBar);
		_typeTable[78] = typeof(RangeBase);
		_typeTable[79] = typeof(ProgressBarTemplateSettings);
		_typeTable[80] = typeof(ToastAccentBrushConverter);
		_typeTable[81] = typeof(ToastIconConverter);
		_typeTable[82] = typeof(Button);
		_typeTable[83] = typeof(Thickness);
		_typeTable[84] = typeof(CornerRadius);
		_typeTable[85] = typeof(TreeViewNode);
		_typeTable[86] = typeof(IList<TreeViewNode>);
	}

	private int LookupTypeIndexByName(string typeName)
	{
		if (_typeNameTable == null)
		{
			InitTypeTables();
		}
		for (int i = 0; i < _typeNameTable.Length; i++)
		{
			if (string.CompareOrdinal(_typeNameTable[i], typeName) == 0)
			{
				return i;
			}
		}
		return -1;
	}

	private int LookupTypeIndexByType(Type type)
	{
		if (_typeTable == null)
		{
			InitTypeTables();
		}
		for (int i = 0; i < _typeTable.Length; i++)
		{
			if (type == _typeTable[i])
			{
				return i;
			}
		}
		return -1;
	}

	private object Activate_0_XamlControlsResources()
	{
		return new XamlControlsResources();
	}

	private object Activate_4_AppIcon()
	{
		return new AppIcon();
	}

	private object Activate_9_BootstrapPage()
	{
		return new BootstrapPage();
	}

	private object Activate_11_AnimatedFavoriteStar()
	{
		return new AnimatedFavoriteStar();
	}

	private object Activate_15_WrapPanel()
	{
		return new WrapPanel();
	}

	private object Activate_17_GalleryFilterPanel()
	{
		return new GalleryFilterPanel();
	}

	private object Activate_22_GalleryFilterSidebar()
	{
		return new GalleryFilterSidebar();
	}

	private object Activate_23_MonthNav()
	{
		return new MonthNav();
	}

	private object Activate_25_PhotoGrid()
	{
		return new PhotoGrid();
	}

	private object Activate_30_GalleryMasonryView()
	{
		return new GalleryMasonryView();
	}

	private object Activate_33_EmptyState()
	{
		return new EmptyState();
	}

	private object Activate_34_GalleryGridStage()
	{
		return new GalleryGridStage();
	}

	private object Activate_39_GroupDrillDownPage()
	{
		return new GroupDrillDownPage();
	}

	private object Activate_47_IntToVisibilityConverter()
	{
		return new IntToVisibilityConverter();
	}

	private object Activate_48_ProgressRing()
	{
		return new ProgressRing();
	}

	private object Activate_52_ShellHeaderBar()
	{
		return new ShellHeaderBar();
	}

	private object Activate_53_ScanningOverlay()
	{
		return new ScanningOverlay();
	}

	private object Activate_54_ToastHost()
	{
		return new ToastHost();
	}

	private object Activate_55_ShellStage()
	{
		return new ShellStage();
	}

	private object Activate_57_ThumbnailSourceConverter()
	{
		return new ThumbnailSourceConverter();
	}

	private object Activate_59_MatchPercentConverter()
	{
		return new MatchPercentConverter();
	}

	private object Activate_61_MainWindow()
	{
		return new MainWindow();
	}

	private object Activate_63_RadialGradientBrush()
	{
		return new RadialGradientBrush();
	}

	private object Activate_73_CustomScrollbar()
	{
		return new CustomScrollbar();
	}

	private object Activate_74_PhotoGridItemsView()
	{
		return new PhotoGridItemsView();
	}

	private object Activate_76_BoolToVisibilityConverter()
	{
		return new BoolToVisibilityConverter();
	}

	private object Activate_77_ProgressBar()
	{
		return new ProgressBar();
	}

	private object Activate_80_ToastAccentBrushConverter()
	{
		return new ToastAccentBrushConverter();
	}

	private object Activate_81_ToastIconConverter()
	{
		return new ToastIconConverter();
	}

	private object Activate_85_TreeViewNode()
	{
		return new TreeViewNode();
	}

	private void StaticInitializer_0_XamlControlsResources()
	{
		RuntimeHelpers.RunClassConstructor(typeof(XamlControlsResources).TypeHandle);
	}

	private void StaticInitializer_4_AppIcon()
	{
		RuntimeHelpers.RunClassConstructor(typeof(AppIcon).TypeHandle);
	}

	private void StaticInitializer_9_BootstrapPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(BootstrapPage).TypeHandle);
	}

	private void StaticInitializer_11_AnimatedFavoriteStar()
	{
		RuntimeHelpers.RunClassConstructor(typeof(AnimatedFavoriteStar).TypeHandle);
	}

	private void StaticInitializer_12_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action).TypeHandle);
	}

	private void StaticInitializer_13_MulticastDelegate()
	{
		RuntimeHelpers.RunClassConstructor(typeof(MulticastDelegate).TypeHandle);
	}

	private void StaticInitializer_14_Delegate()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Delegate).TypeHandle);
	}

	private void StaticInitializer_15_WrapPanel()
	{
		RuntimeHelpers.RunClassConstructor(typeof(WrapPanel).TypeHandle);
	}

	private void StaticInitializer_17_GalleryFilterPanel()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GalleryFilterPanel).TypeHandle);
	}

	private void StaticInitializer_18_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<string>).TypeHandle);
	}

	private void StaticInitializer_19_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<SortMode>).TypeHandle);
	}

	private void StaticInitializer_20_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<DisplayFolderMode>).TypeHandle);
	}

	private void StaticInitializer_21_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<GroupingMode>).TypeHandle);
	}

	private void StaticInitializer_22_GalleryFilterSidebar()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GalleryFilterSidebar).TypeHandle);
	}

	private void StaticInitializer_23_MonthNav()
	{
		RuntimeHelpers.RunClassConstructor(typeof(MonthNav).TypeHandle);
	}

	private void StaticInitializer_24_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<GalleryMonthGroup>).TypeHandle);
	}

	private void StaticInitializer_25_PhotoGrid()
	{
		RuntimeHelpers.RunClassConstructor(typeof(PhotoGrid).TypeHandle);
	}

	private void StaticInitializer_26_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<Task>).TypeHandle);
	}

	private void StaticInitializer_27_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<double>).TypeHandle);
	}

	private void StaticInitializer_28_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<PhotoGridItem>).TypeHandle);
	}

	private void StaticInitializer_29_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<int>).TypeHandle);
	}

	private void StaticInitializer_30_GalleryMasonryView()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GalleryMasonryView).TypeHandle);
	}

	private void StaticInitializer_31_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<PhotoThumbnailItem>).TypeHandle);
	}

	private void StaticInitializer_32_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<IReadOnlyList<PhotoThumbnailItem>>).TypeHandle);
	}

	private void StaticInitializer_33_EmptyState()
	{
		RuntimeHelpers.RunClassConstructor(typeof(EmptyState).TypeHandle);
	}

	private void StaticInitializer_34_GalleryGridStage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GalleryGridStage).TypeHandle);
	}

	private void StaticInitializer_35_Action()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Action<GalleryMasonryView>).TypeHandle);
	}

	private void StaticInitializer_37_GalleryPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GalleryPage).TypeHandle);
	}

	private void StaticInitializer_38_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<Task<string>>).TypeHandle);
	}

	private void StaticInitializer_39_GroupDrillDownPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(GroupDrillDownPage).TypeHandle);
	}

	private void StaticInitializer_40_IReadOnlyList()
	{
		RuntimeHelpers.RunClassConstructor(typeof(IReadOnlyList<PhotoThumbnailItem>).TypeHandle);
	}

	private void StaticInitializer_41_PhotoModalPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(PhotoModalPage).TypeHandle);
	}

	private void StaticInitializer_42_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<string, string, Task>).TypeHandle);
	}

	private void StaticInitializer_43_SettingsPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(SettingsPage).TypeHandle);
	}

	private void StaticInitializer_44_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<int, Task>).TypeHandle);
	}

	private void StaticInitializer_45_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<bool, Task>).TypeHandle);
	}

	private void StaticInitializer_46_Func()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Func<string, Task>).TypeHandle);
	}

	private void StaticInitializer_47_IntToVisibilityConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(IntToVisibilityConverter).TypeHandle);
	}

	private void StaticInitializer_48_ProgressRing()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ProgressRing).TypeHandle);
	}

	private void StaticInitializer_50_ProgressRingTemplateSettings()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ProgressRingTemplateSettings).TypeHandle);
	}

	private void StaticInitializer_52_ShellHeaderBar()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ShellHeaderBar).TypeHandle);
	}

	private void StaticInitializer_53_ScanningOverlay()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ScanningOverlay).TypeHandle);
	}

	private void StaticInitializer_54_ToastHost()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ToastHost).TypeHandle);
	}

	private void StaticInitializer_55_ShellStage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ShellStage).TypeHandle);
	}

	private void StaticInitializer_56_ShellPage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ShellPage).TypeHandle);
	}

	private void StaticInitializer_57_ThumbnailSourceConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ThumbnailSourceConverter).TypeHandle);
	}

	private void StaticInitializer_59_MatchPercentConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(MatchPercentConverter).TypeHandle);
	}

	private void StaticInitializer_60_WorldResolvePage()
	{
		RuntimeHelpers.RunClassConstructor(typeof(WorldResolvePage).TypeHandle);
	}

	private void StaticInitializer_61_MainWindow()
	{
		RuntimeHelpers.RunClassConstructor(typeof(MainWindow).TypeHandle);
	}

	private void StaticInitializer_63_RadialGradientBrush()
	{
		RuntimeHelpers.RunClassConstructor(typeof(RadialGradientBrush).TypeHandle);
	}

	private void StaticInitializer_65_IObservableVector()
	{
		RuntimeHelpers.RunClassConstructor(typeof(IObservableVector<GradientStop>).TypeHandle);
	}

	private void StaticInitializer_68_CompositionColorSpace()
	{
		RuntimeHelpers.RunClassConstructor(typeof(CompositionColorSpace).TypeHandle);
	}

	private void StaticInitializer_69_Enum()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Enum).TypeHandle);
	}

	private void StaticInitializer_70_ValueType()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ValueType).TypeHandle);
	}

	private void StaticInitializer_73_CustomScrollbar()
	{
		RuntimeHelpers.RunClassConstructor(typeof(CustomScrollbar).TypeHandle);
	}

	private void StaticInitializer_74_PhotoGridItemsView()
	{
		RuntimeHelpers.RunClassConstructor(typeof(PhotoGridItemsView).TypeHandle);
	}

	private void StaticInitializer_76_BoolToVisibilityConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(BoolToVisibilityConverter).TypeHandle);
	}

	private void StaticInitializer_77_ProgressBar()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ProgressBar).TypeHandle);
	}

	private void StaticInitializer_79_ProgressBarTemplateSettings()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ProgressBarTemplateSettings).TypeHandle);
	}

	private void StaticInitializer_80_ToastAccentBrushConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ToastAccentBrushConverter).TypeHandle);
	}

	private void StaticInitializer_81_ToastIconConverter()
	{
		RuntimeHelpers.RunClassConstructor(typeof(ToastIconConverter).TypeHandle);
	}

	private void StaticInitializer_83_Thickness()
	{
		RuntimeHelpers.RunClassConstructor(typeof(Thickness).TypeHandle);
	}

	private void StaticInitializer_84_CornerRadius()
	{
		RuntimeHelpers.RunClassConstructor(typeof(CornerRadius).TypeHandle);
	}

	private void StaticInitializer_85_TreeViewNode()
	{
		RuntimeHelpers.RunClassConstructor(typeof(TreeViewNode).TypeHandle);
	}

	private void StaticInitializer_86_IList()
	{
		RuntimeHelpers.RunClassConstructor(typeof(IList<TreeViewNode>).TypeHandle);
	}

	private void MapAdd_0_XamlControlsResources(object instance, object key, object item)
	{
		((IDictionary<object, object>)instance).Add(key, item);
	}

	private void VectorAdd_65_IObservableVector(object instance, object item)
	{
		ICollection<GradientStop> obj = (ICollection<GradientStop>)instance;
		GradientStop item2 = (GradientStop)item;
		obj.Add(item2);
	}

	private void VectorAdd_86_IList(object instance, object item)
	{
		ICollection<TreeViewNode> obj = (ICollection<TreeViewNode>)instance;
		TreeViewNode item2 = (TreeViewNode)item;
		obj.Add(item2);
	}

	private IXamlType CreateXamlType(int typeIndex)
	{
		XamlSystemBaseType result = null;
		string fullName = _typeNameTable[typeIndex];
		Type type = _typeTable[typeIndex];
		switch (typeIndex)
		{
		case 0:
		{
			XamlUserType xamlUserType61 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.ResourceDictionary"));
			xamlUserType61.Activator = Activate_0_XamlControlsResources;
			xamlUserType61.StaticInitializer = StaticInitializer_0_XamlControlsResources;
			xamlUserType61.DictionaryAdd = MapAdd_0_XamlControlsResources;
			xamlUserType61.AddMemberName("UseCompactResources");
			result = xamlUserType61;
			break;
		}
		case 1:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 2:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 3:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 4:
		{
			XamlUserType xamlUserType60 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType60.Activator = Activate_4_AppIcon;
			xamlUserType60.StaticInitializer = StaticInitializer_4_AppIcon;
			xamlUserType60.AddMemberName("IconName");
			xamlUserType60.AddMemberName("IconSize");
			xamlUserType60.AddMemberName("Foreground");
			xamlUserType60.SetIsLocalType();
			result = xamlUserType60;
			break;
		}
		case 5:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 6:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 7:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 8:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 9:
		{
			XamlUserType xamlUserType59 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType59.Activator = Activate_9_BootstrapPage;
			xamlUserType59.StaticInitializer = StaticInitializer_9_BootstrapPage;
			xamlUserType59.SetIsLocalType();
			result = xamlUserType59;
			break;
		}
		case 10:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 11:
		{
			XamlUserType xamlUserType58 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType58.Activator = Activate_11_AnimatedFavoriteStar;
			xamlUserType58.StaticInitializer = StaticInitializer_11_AnimatedFavoriteStar;
			xamlUserType58.AddMemberName("Liked");
			xamlUserType58.AddMemberName("Interactive");
			xamlUserType58.AddMemberName("StarFill");
			xamlUserType58.AddMemberName("StarStroke");
			xamlUserType58.AddMemberName("OnClick");
			xamlUserType58.SetIsLocalType();
			result = xamlUserType58;
			break;
		}
		case 12:
		{
			XamlUserType xamlUserType57 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType57.StaticInitializer = StaticInitializer_12_Action;
			xamlUserType57.SetIsReturnTypeStub();
			result = xamlUserType57;
			break;
		}
		case 13:
			result = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.Delegate"))
			{
				StaticInitializer = StaticInitializer_13_MulticastDelegate
			};
			break;
		case 14:
			result = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"))
			{
				StaticInitializer = StaticInitializer_14_Delegate
			};
			break;
		case 15:
		{
			XamlUserType xamlUserType56 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Panel"));
			xamlUserType56.Activator = Activate_15_WrapPanel;
			xamlUserType56.StaticInitializer = StaticInitializer_15_WrapPanel;
			xamlUserType56.AddMemberName("HorizontalSpacing");
			xamlUserType56.AddMemberName("VerticalSpacing");
			xamlUserType56.SetIsLocalType();
			result = xamlUserType56;
			break;
		}
		case 16:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 17:
		{
			XamlUserType xamlUserType55 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType55.Activator = Activate_17_GalleryFilterPanel;
			xamlUserType55.StaticInitializer = StaticInitializer_17_GalleryFilterPanel;
			xamlUserType55.AddMemberName("OnResetFilters");
			xamlUserType55.AddMemberName("OnDatePresetSelect");
			xamlUserType55.AddMemberName("OnOrientationSelect");
			xamlUserType55.AddMemberName("OnSortSelect");
			xamlUserType55.AddMemberName("OnDisplayFolderSelect");
			xamlUserType55.AddMemberName("OnGroupingSelect");
			xamlUserType55.AddMemberName("OnWorldFilterAdd");
			xamlUserType55.AddMemberName("OnWorldFilterRemove");
			xamlUserType55.AddMemberName("OnTagFilterAdd");
			xamlUserType55.AddMemberName("OnTagFilterRemove");
			xamlUserType55.SetIsLocalType();
			result = xamlUserType55;
			break;
		}
		case 18:
		{
			XamlUserType xamlUserType54 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType54.StaticInitializer = StaticInitializer_18_Action;
			xamlUserType54.SetIsReturnTypeStub();
			result = xamlUserType54;
			break;
		}
		case 19:
		{
			XamlUserType xamlUserType53 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType53.StaticInitializer = StaticInitializer_19_Action;
			xamlUserType53.SetIsReturnTypeStub();
			result = xamlUserType53;
			break;
		}
		case 20:
		{
			XamlUserType xamlUserType52 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType52.StaticInitializer = StaticInitializer_20_Action;
			xamlUserType52.SetIsReturnTypeStub();
			result = xamlUserType52;
			break;
		}
		case 21:
		{
			XamlUserType xamlUserType51 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType51.StaticInitializer = StaticInitializer_21_Action;
			xamlUserType51.SetIsReturnTypeStub();
			result = xamlUserType51;
			break;
		}
		case 22:
		{
			XamlUserType xamlUserType50 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType50.Activator = Activate_22_GalleryFilterSidebar;
			xamlUserType50.StaticInitializer = StaticInitializer_22_GalleryFilterSidebar;
			xamlUserType50.AddMemberName("OnResetFilters");
			xamlUserType50.AddMemberName("OnDatePresetSelect");
			xamlUserType50.SetIsLocalType();
			result = xamlUserType50;
			break;
		}
		case 23:
		{
			XamlUserType xamlUserType49 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType49.Activator = Activate_23_MonthNav;
			xamlUserType49.StaticInitializer = StaticInitializer_23_MonthNav;
			xamlUserType49.AddMemberName("OnJumpToMonth");
			xamlUserType49.SetIsLocalType();
			result = xamlUserType49;
			break;
		}
		case 24:
		{
			XamlUserType xamlUserType48 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType48.StaticInitializer = StaticInitializer_24_Action;
			xamlUserType48.SetIsReturnTypeStub();
			result = xamlUserType48;
			break;
		}
		case 25:
		{
			XamlUserType xamlUserType47 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType47.Activator = Activate_25_PhotoGrid;
			xamlUserType47.StaticInitializer = StaticInitializer_25_PhotoGrid;
			xamlUserType47.AddMemberName("OnGoToPrevPage");
			xamlUserType47.AddMemberName("OnGoToNextPage");
			xamlUserType47.AddMemberName("OnLoadMorePhotos");
			xamlUserType47.AddMemberName("OnRightPanelMeasured");
			xamlUserType47.AddMemberName("OnGridWrapperMeasured");
			xamlUserType47.AddMemberName("OnPhotoActivated");
			xamlUserType47.AddMemberName("OnFavoriteClicked");
			xamlUserType47.AddMemberName("OnGridScroll");
			xamlUserType47.AddMemberName("OnGridWheel");
			xamlUserType47.AddMemberName("OnFirstVisibleIndexChanged");
			xamlUserType47.AddMemberName("OnScrollbarTrackClick");
			xamlUserType47.AddMemberName("OnScrollbarDrag");
			xamlUserType47.SetIsLocalType();
			result = xamlUserType47;
			break;
		}
		case 26:
		{
			XamlUserType xamlUserType46 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType46.StaticInitializer = StaticInitializer_26_Func;
			xamlUserType46.SetIsReturnTypeStub();
			result = xamlUserType46;
			break;
		}
		case 27:
		{
			XamlUserType xamlUserType45 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType45.StaticInitializer = StaticInitializer_27_Action;
			xamlUserType45.SetIsReturnTypeStub();
			result = xamlUserType45;
			break;
		}
		case 28:
		{
			XamlUserType xamlUserType44 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType44.StaticInitializer = StaticInitializer_28_Action;
			xamlUserType44.SetIsReturnTypeStub();
			result = xamlUserType44;
			break;
		}
		case 29:
		{
			XamlUserType xamlUserType43 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType43.StaticInitializer = StaticInitializer_29_Action;
			xamlUserType43.SetIsReturnTypeStub();
			result = xamlUserType43;
			break;
		}
		case 30:
		{
			XamlUserType xamlUserType42 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType42.Activator = Activate_30_GalleryMasonryView;
			xamlUserType42.StaticInitializer = StaticInitializer_30_GalleryMasonryView;
			xamlUserType42.AddMemberName("OnPhotoTapped");
			xamlUserType42.AddMemberName("OnThumbnailsNeeded");
			xamlUserType42.AddMemberName("OnFirstVisibleIndexChanged");
			xamlUserType42.SetIsLocalType();
			result = xamlUserType42;
			break;
		}
		case 31:
		{
			XamlUserType xamlUserType41 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType41.StaticInitializer = StaticInitializer_31_Action;
			xamlUserType41.SetIsReturnTypeStub();
			result = xamlUserType41;
			break;
		}
		case 32:
		{
			XamlUserType xamlUserType40 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType40.StaticInitializer = StaticInitializer_32_Action;
			xamlUserType40.SetIsReturnTypeStub();
			result = xamlUserType40;
			break;
		}
		case 33:
		{
			XamlUserType xamlUserType39 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType39.Activator = Activate_33_EmptyState;
			xamlUserType39.StaticInitializer = StaticInitializer_33_EmptyState;
			xamlUserType39.SetIsLocalType();
			result = xamlUserType39;
			break;
		}
		case 34:
		{
			XamlUserType xamlUserType38 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType38.Activator = Activate_34_GalleryGridStage;
			xamlUserType38.StaticInitializer = StaticInitializer_34_GalleryGridStage;
			xamlUserType38.AddMemberName("OnMasonryRealized");
			xamlUserType38.AddMemberName("GridDataContext");
			xamlUserType38.AddMemberName("PhotoGridControlRef");
			xamlUserType38.AddMemberName("MasonryViewControlRef");
			xamlUserType38.AddMemberName("MonthNavControlRef");
			xamlUserType38.AddMemberName("EmptyStateVisibility");
			xamlUserType38.SetIsLocalType();
			result = xamlUserType38;
			break;
		}
		case 35:
		{
			XamlUserType xamlUserType37 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType37.StaticInitializer = StaticInitializer_35_Action;
			xamlUserType37.SetIsReturnTypeStub();
			result = xamlUserType37;
			break;
		}
		case 36:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 37:
		{
			XamlUserType xamlUserType36 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType36.StaticInitializer = StaticInitializer_37_GalleryPage;
			xamlUserType36.AddMemberName("OnResetFilters");
			xamlUserType36.AddMemberName("OnDatePresetSelect");
			xamlUserType36.AddMemberName("OnSelectPhoto");
			xamlUserType36.AddMemberName("OnDrillIntoGroup");
			xamlUserType36.AddMemberName("OnChooseFolder");
			xamlUserType36.AddMemberName("OnOpenSettings");
			xamlUserType36.SetIsLocalType();
			result = xamlUserType36;
			break;
		}
		case 38:
		{
			XamlUserType xamlUserType35 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType35.StaticInitializer = StaticInitializer_38_Func;
			xamlUserType35.SetIsReturnTypeStub();
			result = xamlUserType35;
			break;
		}
		case 39:
		{
			XamlUserType xamlUserType34 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType34.Activator = Activate_39_GroupDrillDownPage;
			xamlUserType34.StaticInitializer = StaticInitializer_39_GroupDrillDownPage;
			xamlUserType34.AddMemberName("OnBack");
			xamlUserType34.AddMemberName("OnPhotoActivated");
			xamlUserType34.AddMemberName("OnFavoriteClicked");
			xamlUserType34.AddMemberName("OnThumbnailsNeeded");
			xamlUserType34.AddMemberName("CurrentPhotos");
			xamlUserType34.SetIsLocalType();
			result = xamlUserType34;
			break;
		}
		case 40:
		{
			XamlUserType xamlUserType33 = new XamlUserType(this, fullName, type, null);
			xamlUserType33.StaticInitializer = StaticInitializer_40_IReadOnlyList;
			xamlUserType33.SetIsReturnTypeStub();
			result = xamlUserType33;
			break;
		}
		case 41:
		{
			XamlUserType xamlUserType32 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType32.StaticInitializer = StaticInitializer_41_PhotoModalPage;
			xamlUserType32.AddMemberName("OnClose");
			xamlUserType32.AddMemberName("OnGoBack");
			xamlUserType32.AddMemberName("OnGoPrev");
			xamlUserType32.AddMemberName("OnGoNext");
			xamlUserType32.AddMemberName("OnOpenWorld");
			xamlUserType32.AddMemberName("OnOpenExplorer");
			xamlUserType32.AddMemberName("OnTweet");
			xamlUserType32.AddMemberName("OnToggleFavorite");
			xamlUserType32.AddMemberName("OnAddTag");
			xamlUserType32.AddMemberName("OnRemoveTag");
			xamlUserType32.AddMemberName("OnOpenTagMaster");
			xamlUserType32.SetIsLocalType();
			result = xamlUserType32;
			break;
		}
		case 42:
		{
			XamlUserType xamlUserType31 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType31.StaticInitializer = StaticInitializer_42_Func;
			xamlUserType31.SetIsReturnTypeStub();
			result = xamlUserType31;
			break;
		}
		case 43:
		{
			XamlUserType xamlUserType30 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType30.StaticInitializer = StaticInitializer_43_SettingsPage;
			xamlUserType30.AddMemberName("OnClose");
			xamlUserType30.AddMemberName("OnChooseFolder");
			xamlUserType30.AddMemberName("OnResetFolder");
			xamlUserType30.AddMemberName("OnStartupPreferenceChanged");
			xamlUserType30.AddMemberName("OnThemeChanged");
			xamlUserType30.AddMemberName("OnStartWorldAnalysis");
			xamlUserType30.AddMemberName("OnCreateTag");
			xamlUserType30.AddMemberName("OnDeleteTag");
			xamlUserType30.AddMemberName("OnCancelEdit");
			xamlUserType30.AddMemberName("OnSaveTemplate");
			xamlUserType30.AddMemberName("OnStartEdit");
			xamlUserType30.AddMemberName("OnDeleteTemplate");
			xamlUserType30.AddMemberName("OnSelectTemplate");
			xamlUserType30.SetIsLocalType();
			result = xamlUserType30;
			break;
		}
		case 44:
		{
			XamlUserType xamlUserType29 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType29.StaticInitializer = StaticInitializer_44_Func;
			xamlUserType29.SetIsReturnTypeStub();
			result = xamlUserType29;
			break;
		}
		case 45:
		{
			XamlUserType xamlUserType28 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType28.StaticInitializer = StaticInitializer_45_Func;
			xamlUserType28.SetIsReturnTypeStub();
			result = xamlUserType28;
			break;
		}
		case 46:
		{
			XamlUserType xamlUserType27 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.MulticastDelegate"));
			xamlUserType27.StaticInitializer = StaticInitializer_46_Func;
			xamlUserType27.SetIsReturnTypeStub();
			result = xamlUserType27;
			break;
		}
		case 47:
		{
			XamlUserType xamlUserType26 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType26.Activator = Activate_47_IntToVisibilityConverter;
			xamlUserType26.StaticInitializer = StaticInitializer_47_IntToVisibilityConverter;
			xamlUserType26.SetIsLocalType();
			result = xamlUserType26;
			break;
		}
		case 48:
		{
			XamlUserType xamlUserType25 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Control"));
			xamlUserType25.Activator = Activate_48_ProgressRing;
			xamlUserType25.StaticInitializer = StaticInitializer_48_ProgressRing;
			xamlUserType25.AddMemberName("IsActive");
			xamlUserType25.AddMemberName("IsIndeterminate");
			xamlUserType25.AddMemberName("Maximum");
			xamlUserType25.AddMemberName("Minimum");
			xamlUserType25.AddMemberName("TemplateSettings");
			xamlUserType25.AddMemberName("Value");
			result = xamlUserType25;
			break;
		}
		case 49:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 50:
		{
			XamlUserType xamlUserType24 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.DependencyObject"));
			xamlUserType24.StaticInitializer = StaticInitializer_50_ProgressRingTemplateSettings;
			xamlUserType24.SetIsReturnTypeStub();
			result = xamlUserType24;
			break;
		}
		case 51:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 52:
		{
			XamlUserType xamlUserType23 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType23.Activator = Activate_52_ShellHeaderBar;
			xamlUserType23.StaticInitializer = StaticInitializer_52_ShellHeaderBar;
			xamlUserType23.AddMemberName("OnToggleFilter");
			xamlUserType23.AddMemberName("OnShowSettings");
			xamlUserType23.AddMemberName("OnToggleMultiSelect");
			xamlUserType23.AddMemberName("OnGroupingChange");
			xamlUserType23.AddMemberName("OnViewModeChange");
			xamlUserType23.AddMemberName("OnSearchSubmit");
			xamlUserType23.SetIsLocalType();
			result = xamlUserType23;
			break;
		}
		case 53:
		{
			XamlUserType xamlUserType22 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType22.Activator = Activate_53_ScanningOverlay;
			xamlUserType22.StaticInitializer = StaticInitializer_53_ScanningOverlay;
			xamlUserType22.AddMemberName("OnCancelScan");
			xamlUserType22.SetIsLocalType();
			result = xamlUserType22;
			break;
		}
		case 54:
		{
			XamlUserType xamlUserType21 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType21.Activator = Activate_54_ToastHost;
			xamlUserType21.StaticInitializer = StaticInitializer_54_ToastHost;
			xamlUserType21.SetIsLocalType();
			result = xamlUserType21;
			break;
		}
		case 55:
		{
			XamlUserType xamlUserType20 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType20.Activator = Activate_55_ShellStage;
			xamlUserType20.StaticInitializer = StaticInitializer_55_ShellStage;
			xamlUserType20.AddMemberName("MainContent");
			xamlUserType20.AddMemberName("ModalContent");
			xamlUserType20.AddMemberName("ModalVisibility");
			xamlUserType20.AddMemberName("TopModalContent");
			xamlUserType20.AddMemberName("TopModalVisibility");
			xamlUserType20.AddMemberName("ScanningOverlayVisibility");
			xamlUserType20.AddMemberName("ScanningOverlayDataContext");
			xamlUserType20.AddMemberName("ToastDataContext");
			xamlUserType20.AddMemberName("ScanningOverlayControlRef");
			xamlUserType20.SetIsLocalType();
			result = xamlUserType20;
			break;
		}
		case 56:
		{
			XamlUserType xamlUserType19 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType19.StaticInitializer = StaticInitializer_56_ShellPage;
			xamlUserType19.SetIsLocalType();
			result = xamlUserType19;
			break;
		}
		case 57:
		{
			XamlUserType xamlUserType18 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType18.Activator = Activate_57_ThumbnailSourceConverter;
			xamlUserType18.StaticInitializer = StaticInitializer_57_ThumbnailSourceConverter;
			xamlUserType18.AddMemberName("DecodePixelWidth");
			xamlUserType18.SetIsLocalType();
			result = xamlUserType18;
			break;
		}
		case 58:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 59:
		{
			XamlUserType xamlUserType17 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType17.Activator = Activate_59_MatchPercentConverter;
			xamlUserType17.StaticInitializer = StaticInitializer_59_MatchPercentConverter;
			xamlUserType17.SetIsLocalType();
			result = xamlUserType17;
			break;
		}
		case 60:
		{
			XamlUserType xamlUserType16 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Page"));
			xamlUserType16.StaticInitializer = StaticInitializer_60_WorldResolvePage;
			xamlUserType16.AddMemberName("OnClose");
			xamlUserType16.AddMemberName("OnApplied");
			xamlUserType16.SetIsLocalType();
			result = xamlUserType16;
			break;
		}
		case 61:
		{
			XamlUserType xamlUserType15 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Window"));
			xamlUserType15.Activator = Activate_61_MainWindow;
			xamlUserType15.StaticInitializer = StaticInitializer_61_MainWindow;
			xamlUserType15.SetIsLocalType();
			result = xamlUserType15;
			break;
		}
		case 62:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 63:
		{
			XamlUserType xamlUserType14 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Media.XamlCompositionBrushBase"));
			xamlUserType14.Activator = Activate_63_RadialGradientBrush;
			xamlUserType14.StaticInitializer = StaticInitializer_63_RadialGradientBrush;
			xamlUserType14.SetContentPropertyName("Microsoft.UI.Xaml.Media.RadialGradientBrush.GradientStops");
			xamlUserType14.AddMemberName("GradientStops");
			xamlUserType14.AddMemberName("Center");
			xamlUserType14.AddMemberName("GradientOrigin");
			xamlUserType14.AddMemberName("InterpolationSpace");
			xamlUserType14.AddMemberName("MappingMode");
			xamlUserType14.AddMemberName("RadiusX");
			xamlUserType14.AddMemberName("RadiusY");
			xamlUserType14.AddMemberName("SpreadMethod");
			result = xamlUserType14;
			break;
		}
		case 64:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 65:
		{
			XamlUserType xamlUserType13 = new XamlUserType(this, fullName, type, null);
			xamlUserType13.StaticInitializer = StaticInitializer_65_IObservableVector;
			xamlUserType13.CollectionAdd = VectorAdd_65_IObservableVector;
			xamlUserType13.SetIsReturnTypeStub();
			result = xamlUserType13;
			break;
		}
		case 66:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 67:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 68:
		{
			XamlUserType xamlUserType12 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.Enum"));
			xamlUserType12.StaticInitializer = StaticInitializer_68_CompositionColorSpace;
			xamlUserType12.AddEnumValue("Auto", CompositionColorSpace.Auto);
			xamlUserType12.AddEnumValue("Hsl", CompositionColorSpace.Hsl);
			xamlUserType12.AddEnumValue("Rgb", CompositionColorSpace.Rgb);
			xamlUserType12.AddEnumValue("HslLinear", CompositionColorSpace.HslLinear);
			xamlUserType12.AddEnumValue("RgbLinear", CompositionColorSpace.RgbLinear);
			result = xamlUserType12;
			break;
		}
		case 69:
			result = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.ValueType"))
			{
				StaticInitializer = StaticInitializer_69_Enum
			};
			break;
		case 70:
			result = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"))
			{
				StaticInitializer = StaticInitializer_70_ValueType
			};
			break;
		case 71:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 72:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 73:
		{
			XamlUserType xamlUserType11 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType11.Activator = Activate_73_CustomScrollbar;
			xamlUserType11.StaticInitializer = StaticInitializer_73_CustomScrollbar;
			xamlUserType11.AddMemberName("ThumbTop");
			xamlUserType11.AddMemberName("ThumbHeight");
			xamlUserType11.AddMemberName("OnTrackClick");
			xamlUserType11.AddMemberName("OnDrag");
			xamlUserType11.SetIsLocalType();
			result = xamlUserType11;
			break;
		}
		case 74:
		{
			XamlUserType xamlUserType10 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.UserControl"));
			xamlUserType10.Activator = Activate_74_PhotoGridItemsView;
			xamlUserType10.StaticInitializer = StaticInitializer_74_PhotoGridItemsView;
			xamlUserType10.AddMemberName("OnPhotoActivated");
			xamlUserType10.AddMemberName("OnFavoriteClicked");
			xamlUserType10.AddMemberName("OnGridScroll");
			xamlUserType10.AddMemberName("OnGridWheel");
			xamlUserType10.AddMemberName("OnNearBottomReached");
			xamlUserType10.AddMemberName("OnFirstVisibleIndexChanged");
			xamlUserType10.AddMemberName("GridScrollViewerRef");
			xamlUserType10.SetIsLocalType();
			result = xamlUserType10;
			break;
		}
		case 75:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 76:
		{
			XamlUserType xamlUserType9 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType9.Activator = Activate_76_BoolToVisibilityConverter;
			xamlUserType9.StaticInitializer = StaticInitializer_76_BoolToVisibilityConverter;
			xamlUserType9.SetIsLocalType();
			result = xamlUserType9;
			break;
		}
		case 77:
		{
			XamlUserType xamlUserType8 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.Controls.Primitives.RangeBase"));
			xamlUserType8.Activator = Activate_77_ProgressBar;
			xamlUserType8.StaticInitializer = StaticInitializer_77_ProgressBar;
			xamlUserType8.AddMemberName("IsIndeterminate");
			xamlUserType8.AddMemberName("ShowError");
			xamlUserType8.AddMemberName("ShowPaused");
			xamlUserType8.AddMemberName("TemplateSettings");
			result = xamlUserType8;
			break;
		}
		case 78:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 79:
		{
			XamlUserType xamlUserType7 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.DependencyObject"));
			xamlUserType7.StaticInitializer = StaticInitializer_79_ProgressBarTemplateSettings;
			xamlUserType7.SetIsReturnTypeStub();
			result = xamlUserType7;
			break;
		}
		case 80:
		{
			XamlUserType xamlUserType6 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType6.Activator = Activate_80_ToastAccentBrushConverter;
			xamlUserType6.StaticInitializer = StaticInitializer_80_ToastAccentBrushConverter;
			xamlUserType6.SetIsLocalType();
			result = xamlUserType6;
			break;
		}
		case 81:
		{
			XamlUserType xamlUserType5 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Object"));
			xamlUserType5.Activator = Activate_81_ToastIconConverter;
			xamlUserType5.StaticInitializer = StaticInitializer_81_ToastIconConverter;
			xamlUserType5.SetIsLocalType();
			result = xamlUserType5;
			break;
		}
		case 82:
			result = new XamlSystemBaseType(fullName, type);
			break;
		case 83:
		{
			XamlUserType xamlUserType4 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.ValueType"));
			xamlUserType4.StaticInitializer = StaticInitializer_83_Thickness;
			xamlUserType4.AddMemberName("Left");
			xamlUserType4.AddMemberName("Top");
			xamlUserType4.AddMemberName("Right");
			xamlUserType4.AddMemberName("Bottom");
			result = xamlUserType4;
			break;
		}
		case 84:
		{
			XamlUserType xamlUserType3 = new XamlUserType(this, fullName, type, GetXamlTypeByName("System.ValueType"));
			xamlUserType3.StaticInitializer = StaticInitializer_84_CornerRadius;
			xamlUserType3.AddMemberName("TopLeft");
			xamlUserType3.AddMemberName("TopRight");
			xamlUserType3.AddMemberName("BottomRight");
			xamlUserType3.AddMemberName("BottomLeft");
			result = xamlUserType3;
			break;
		}
		case 85:
		{
			XamlUserType xamlUserType2 = new XamlUserType(this, fullName, type, GetXamlTypeByName("Microsoft.UI.Xaml.DependencyObject"));
			xamlUserType2.Activator = Activate_85_TreeViewNode;
			xamlUserType2.StaticInitializer = StaticInitializer_85_TreeViewNode;
			xamlUserType2.AddMemberName("Children");
			xamlUserType2.AddMemberName("Content");
			xamlUserType2.AddMemberName("Depth");
			xamlUserType2.AddMemberName("HasChildren");
			xamlUserType2.AddMemberName("HasUnrealizedChildren");
			xamlUserType2.AddMemberName("IsExpanded");
			xamlUserType2.AddMemberName("Parent");
			xamlUserType2.SetIsBindable();
			result = xamlUserType2;
			break;
		}
		case 86:
		{
			XamlUserType xamlUserType = new XamlUserType(this, fullName, type, null);
			xamlUserType.StaticInitializer = StaticInitializer_86_IList;
			xamlUserType.CollectionAdd = VectorAdd_86_IList;
			xamlUserType.SetIsReturnTypeStub();
			result = xamlUserType;
			break;
		}
		}
		return result;
	}

	private IXamlType CheckOtherMetadataProvidersForName(string typeName)
	{
		IXamlType xamlType = null;
		IXamlType result = null;
		foreach (IXamlMetadataProvider otherProvider in OtherProviders)
		{
			xamlType = otherProvider.GetXamlType(typeName);
			if (xamlType != null)
			{
				if (xamlType.IsConstructible)
				{
					return xamlType;
				}
				result = xamlType;
			}
		}
		return result;
	}

	private IXamlType CheckOtherMetadataProvidersForType(Type type)
	{
		IXamlType xamlType = null;
		IXamlType result = null;
		foreach (IXamlMetadataProvider otherProvider in OtherProviders)
		{
			xamlType = otherProvider.GetXamlType(type);
			if (xamlType != null)
			{
				if (xamlType.IsConstructible)
				{
					return xamlType;
				}
				result = xamlType;
			}
		}
		return result;
	}

	private object get_0_XamlControlsResources_UseCompactResources(object instance)
	{
		return ((XamlControlsResources)instance).UseCompactResources;
	}

	private void set_0_XamlControlsResources_UseCompactResources(object instance, object Value)
	{
		((XamlControlsResources)instance).UseCompactResources = (bool)Value;
	}

	private object get_1_AppIcon_IconName(object instance)
	{
		return ((AppIcon)instance).IconName;
	}

	private void set_1_AppIcon_IconName(object instance, object Value)
	{
		((AppIcon)instance).IconName = (string)Value;
	}

	private object get_2_AppIcon_IconSize(object instance)
	{
		return ((AppIcon)instance).IconSize;
	}

	private void set_2_AppIcon_IconSize(object instance, object Value)
	{
		((AppIcon)instance).IconSize = (double)Value;
	}

	private object get_3_AppIcon_Foreground(object instance)
	{
		return ((AppIcon)instance).Foreground;
	}

	private void set_3_AppIcon_Foreground(object instance, object Value)
	{
		((AppIcon)instance).Foreground = (Brush)Value;
	}

	private object get_4_AnimatedFavoriteStar_Liked(object instance)
	{
		return ((AnimatedFavoriteStar)instance).Liked;
	}

	private void set_4_AnimatedFavoriteStar_Liked(object instance, object Value)
	{
		((AnimatedFavoriteStar)instance).Liked = (bool)Value;
	}

	private object get_5_AnimatedFavoriteStar_Interactive(object instance)
	{
		return ((AnimatedFavoriteStar)instance).Interactive;
	}

	private void set_5_AnimatedFavoriteStar_Interactive(object instance, object Value)
	{
		((AnimatedFavoriteStar)instance).Interactive = (bool)Value;
	}

	private object get_6_AnimatedFavoriteStar_StarFill(object instance)
	{
		return ((AnimatedFavoriteStar)instance).StarFill;
	}

	private void set_6_AnimatedFavoriteStar_StarFill(object instance, object Value)
	{
		((AnimatedFavoriteStar)instance).StarFill = (Brush)Value;
	}

	private object get_7_AnimatedFavoriteStar_StarStroke(object instance)
	{
		return ((AnimatedFavoriteStar)instance).StarStroke;
	}

	private void set_7_AnimatedFavoriteStar_StarStroke(object instance, object Value)
	{
		((AnimatedFavoriteStar)instance).StarStroke = (Brush)Value;
	}

	private object get_8_AnimatedFavoriteStar_OnClick(object instance)
	{
		return ((AnimatedFavoriteStar)instance).OnClick;
	}

	private void set_8_AnimatedFavoriteStar_OnClick(object instance, object Value)
	{
		((AnimatedFavoriteStar)instance).OnClick = (Action)Value;
	}

	private object get_9_WrapPanel_HorizontalSpacing(object instance)
	{
		return ((WrapPanel)instance).HorizontalSpacing;
	}

	private void set_9_WrapPanel_HorizontalSpacing(object instance, object Value)
	{
		((WrapPanel)instance).HorizontalSpacing = (double)Value;
	}

	private object get_10_WrapPanel_VerticalSpacing(object instance)
	{
		return ((WrapPanel)instance).VerticalSpacing;
	}

	private void set_10_WrapPanel_VerticalSpacing(object instance, object Value)
	{
		((WrapPanel)instance).VerticalSpacing = (double)Value;
	}

	private object get_11_GalleryFilterPanel_OnResetFilters(object instance)
	{
		return ((GalleryFilterPanel)instance).OnResetFilters;
	}

	private void set_11_GalleryFilterPanel_OnResetFilters(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnResetFilters = (Action)Value;
	}

	private object get_12_GalleryFilterPanel_OnDatePresetSelect(object instance)
	{
		return ((GalleryFilterPanel)instance).OnDatePresetSelect;
	}

	private void set_12_GalleryFilterPanel_OnDatePresetSelect(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnDatePresetSelect = (Action<string>)Value;
	}

	private object get_13_GalleryFilterPanel_OnOrientationSelect(object instance)
	{
		return ((GalleryFilterPanel)instance).OnOrientationSelect;
	}

	private void set_13_GalleryFilterPanel_OnOrientationSelect(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnOrientationSelect = (Action<string>)Value;
	}

	private object get_14_GalleryFilterPanel_OnSortSelect(object instance)
	{
		return ((GalleryFilterPanel)instance).OnSortSelect;
	}

	private void set_14_GalleryFilterPanel_OnSortSelect(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnSortSelect = (Action<SortMode>)Value;
	}

	private object get_15_GalleryFilterPanel_OnDisplayFolderSelect(object instance)
	{
		return ((GalleryFilterPanel)instance).OnDisplayFolderSelect;
	}

	private void set_15_GalleryFilterPanel_OnDisplayFolderSelect(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnDisplayFolderSelect = (Action<DisplayFolderMode>)Value;
	}

	private object get_16_GalleryFilterPanel_OnGroupingSelect(object instance)
	{
		return ((GalleryFilterPanel)instance).OnGroupingSelect;
	}

	private void set_16_GalleryFilterPanel_OnGroupingSelect(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnGroupingSelect = (Action<GroupingMode>)Value;
	}

	private object get_17_GalleryFilterPanel_OnWorldFilterAdd(object instance)
	{
		return ((GalleryFilterPanel)instance).OnWorldFilterAdd;
	}

	private void set_17_GalleryFilterPanel_OnWorldFilterAdd(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnWorldFilterAdd = (Action<string>)Value;
	}

	private object get_18_GalleryFilterPanel_OnWorldFilterRemove(object instance)
	{
		return ((GalleryFilterPanel)instance).OnWorldFilterRemove;
	}

	private void set_18_GalleryFilterPanel_OnWorldFilterRemove(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnWorldFilterRemove = (Action<string>)Value;
	}

	private object get_19_GalleryFilterPanel_OnTagFilterAdd(object instance)
	{
		return ((GalleryFilterPanel)instance).OnTagFilterAdd;
	}

	private void set_19_GalleryFilterPanel_OnTagFilterAdd(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnTagFilterAdd = (Action<string>)Value;
	}

	private object get_20_GalleryFilterPanel_OnTagFilterRemove(object instance)
	{
		return ((GalleryFilterPanel)instance).OnTagFilterRemove;
	}

	private void set_20_GalleryFilterPanel_OnTagFilterRemove(object instance, object Value)
	{
		((GalleryFilterPanel)instance).OnTagFilterRemove = (Action<string>)Value;
	}

	private object get_21_GalleryFilterSidebar_OnResetFilters(object instance)
	{
		return ((GalleryFilterSidebar)instance).OnResetFilters;
	}

	private void set_21_GalleryFilterSidebar_OnResetFilters(object instance, object Value)
	{
		((GalleryFilterSidebar)instance).OnResetFilters = (Action)Value;
	}

	private object get_22_GalleryFilterSidebar_OnDatePresetSelect(object instance)
	{
		return ((GalleryFilterSidebar)instance).OnDatePresetSelect;
	}

	private void set_22_GalleryFilterSidebar_OnDatePresetSelect(object instance, object Value)
	{
		((GalleryFilterSidebar)instance).OnDatePresetSelect = (Action<string>)Value;
	}

	private object get_23_MonthNav_OnJumpToMonth(object instance)
	{
		return ((MonthNav)instance).OnJumpToMonth;
	}

	private void set_23_MonthNav_OnJumpToMonth(object instance, object Value)
	{
		((MonthNav)instance).OnJumpToMonth = (Action<GalleryMonthGroup>)Value;
	}

	private object get_24_PhotoGrid_OnGoToPrevPage(object instance)
	{
		return ((PhotoGrid)instance).OnGoToPrevPage;
	}

	private void set_24_PhotoGrid_OnGoToPrevPage(object instance, object Value)
	{
		((PhotoGrid)instance).OnGoToPrevPage = (Func<Task>)Value;
	}

	private object get_25_PhotoGrid_OnGoToNextPage(object instance)
	{
		return ((PhotoGrid)instance).OnGoToNextPage;
	}

	private void set_25_PhotoGrid_OnGoToNextPage(object instance, object Value)
	{
		((PhotoGrid)instance).OnGoToNextPage = (Func<Task>)Value;
	}

	private object get_26_PhotoGrid_OnLoadMorePhotos(object instance)
	{
		return ((PhotoGrid)instance).OnLoadMorePhotos;
	}

	private void set_26_PhotoGrid_OnLoadMorePhotos(object instance, object Value)
	{
		((PhotoGrid)instance).OnLoadMorePhotos = (Func<Task>)Value;
	}

	private object get_27_PhotoGrid_OnRightPanelMeasured(object instance)
	{
		return ((PhotoGrid)instance).OnRightPanelMeasured;
	}

	private void set_27_PhotoGrid_OnRightPanelMeasured(object instance, object Value)
	{
		((PhotoGrid)instance).OnRightPanelMeasured = (Action<double>)Value;
	}

	private object get_28_PhotoGrid_OnGridWrapperMeasured(object instance)
	{
		return ((PhotoGrid)instance).OnGridWrapperMeasured;
	}

	private void set_28_PhotoGrid_OnGridWrapperMeasured(object instance, object Value)
	{
		((PhotoGrid)instance).OnGridWrapperMeasured = (Action<double>)Value;
	}

	private object get_29_PhotoGrid_OnPhotoActivated(object instance)
	{
		return ((PhotoGrid)instance).OnPhotoActivated;
	}

	private void set_29_PhotoGrid_OnPhotoActivated(object instance, object Value)
	{
		((PhotoGrid)instance).OnPhotoActivated = (Action<PhotoGridItem>)Value;
	}

	private object get_30_PhotoGrid_OnFavoriteClicked(object instance)
	{
		return ((PhotoGrid)instance).OnFavoriteClicked;
	}

	private void set_30_PhotoGrid_OnFavoriteClicked(object instance, object Value)
	{
		((PhotoGrid)instance).OnFavoriteClicked = (Action<PhotoGridItem>)Value;
	}

	private object get_31_PhotoGrid_OnGridScroll(object instance)
	{
		return ((PhotoGrid)instance).OnGridScroll;
	}

	private void set_31_PhotoGrid_OnGridScroll(object instance, object Value)
	{
		((PhotoGrid)instance).OnGridScroll = (Action<double>)Value;
	}

	private object get_32_PhotoGrid_OnGridWheel(object instance)
	{
		return ((PhotoGrid)instance).OnGridWheel;
	}

	private void set_32_PhotoGrid_OnGridWheel(object instance, object Value)
	{
		((PhotoGrid)instance).OnGridWheel = (Action<int>)Value;
	}

	private object get_33_PhotoGrid_OnFirstVisibleIndexChanged(object instance)
	{
		return ((PhotoGrid)instance).OnFirstVisibleIndexChanged;
	}

	private void set_33_PhotoGrid_OnFirstVisibleIndexChanged(object instance, object Value)
	{
		((PhotoGrid)instance).OnFirstVisibleIndexChanged = (Action<int>)Value;
	}

	private object get_34_PhotoGrid_OnScrollbarTrackClick(object instance)
	{
		return ((PhotoGrid)instance).OnScrollbarTrackClick;
	}

	private void set_34_PhotoGrid_OnScrollbarTrackClick(object instance, object Value)
	{
		((PhotoGrid)instance).OnScrollbarTrackClick = (Action<double>)Value;
	}

	private object get_35_PhotoGrid_OnScrollbarDrag(object instance)
	{
		return ((PhotoGrid)instance).OnScrollbarDrag;
	}

	private void set_35_PhotoGrid_OnScrollbarDrag(object instance, object Value)
	{
		((PhotoGrid)instance).OnScrollbarDrag = (Action<double>)Value;
	}

	private object get_36_GalleryMasonryView_OnPhotoTapped(object instance)
	{
		return ((GalleryMasonryView)instance).OnPhotoTapped;
	}

	private void set_36_GalleryMasonryView_OnPhotoTapped(object instance, object Value)
	{
		((GalleryMasonryView)instance).OnPhotoTapped = (Action<PhotoThumbnailItem>)Value;
	}

	private object get_37_GalleryMasonryView_OnThumbnailsNeeded(object instance)
	{
		return ((GalleryMasonryView)instance).OnThumbnailsNeeded;
	}

	private void set_37_GalleryMasonryView_OnThumbnailsNeeded(object instance, object Value)
	{
		((GalleryMasonryView)instance).OnThumbnailsNeeded = (Action<IReadOnlyList<PhotoThumbnailItem>>)Value;
	}

	private object get_38_GalleryMasonryView_OnFirstVisibleIndexChanged(object instance)
	{
		return ((GalleryMasonryView)instance).OnFirstVisibleIndexChanged;
	}

	private void set_38_GalleryMasonryView_OnFirstVisibleIndexChanged(object instance, object Value)
	{
		((GalleryMasonryView)instance).OnFirstVisibleIndexChanged = (Action<int>)Value;
	}

	private object get_39_GalleryGridStage_OnMasonryRealized(object instance)
	{
		return ((GalleryGridStage)instance).OnMasonryRealized;
	}

	private void set_39_GalleryGridStage_OnMasonryRealized(object instance, object Value)
	{
		((GalleryGridStage)instance).OnMasonryRealized = (Action<GalleryMasonryView>)Value;
	}

	private object get_40_GalleryGridStage_GridDataContext(object instance)
	{
		return ((GalleryGridStage)instance).GridDataContext;
	}

	private void set_40_GalleryGridStage_GridDataContext(object instance, object Value)
	{
		((GalleryGridStage)instance).GridDataContext = Value;
	}

	private object get_41_GalleryGridStage_PhotoGridControlRef(object instance)
	{
		return ((GalleryGridStage)instance).PhotoGridControlRef;
	}

	private object get_42_GalleryGridStage_MasonryViewControlRef(object instance)
	{
		return ((GalleryGridStage)instance).MasonryViewControlRef;
	}

	private object get_43_GalleryGridStage_MonthNavControlRef(object instance)
	{
		return ((GalleryGridStage)instance).MonthNavControlRef;
	}

	private object get_44_GalleryGridStage_EmptyStateVisibility(object instance)
	{
		return ((GalleryGridStage)instance).EmptyStateVisibility;
	}

	private void set_44_GalleryGridStage_EmptyStateVisibility(object instance, object Value)
	{
		((GalleryGridStage)instance).EmptyStateVisibility = (Visibility)Value;
	}

	private object get_45_GalleryPage_OnResetFilters(object instance)
	{
		return ((GalleryPage)instance).OnResetFilters;
	}

	private void set_45_GalleryPage_OnResetFilters(object instance, object Value)
	{
		((GalleryPage)instance).OnResetFilters = (Action)Value;
	}

	private object get_46_GalleryPage_OnDatePresetSelect(object instance)
	{
		return ((GalleryPage)instance).OnDatePresetSelect;
	}

	private void set_46_GalleryPage_OnDatePresetSelect(object instance, object Value)
	{
		((GalleryPage)instance).OnDatePresetSelect = (Action<string>)Value;
	}

	private object get_47_GalleryPage_OnSelectPhoto(object instance)
	{
		return ((GalleryPage)instance).OnSelectPhoto;
	}

	private void set_47_GalleryPage_OnSelectPhoto(object instance, object Value)
	{
		((GalleryPage)instance).OnSelectPhoto = (Action<PhotoThumbnailItem>)Value;
	}

	private object get_48_GalleryPage_OnDrillIntoGroup(object instance)
	{
		return ((GalleryPage)instance).OnDrillIntoGroup;
	}

	private void set_48_GalleryPage_OnDrillIntoGroup(object instance, object Value)
	{
		((GalleryPage)instance).OnDrillIntoGroup = (Action<PhotoGridItem>)Value;
	}

	private object get_49_GalleryPage_OnChooseFolder(object instance)
	{
		return ((GalleryPage)instance).OnChooseFolder;
	}

	private void set_49_GalleryPage_OnChooseFolder(object instance, object Value)
	{
		((GalleryPage)instance).OnChooseFolder = (Func<Task<string>>)Value;
	}

	private object get_50_GalleryPage_OnOpenSettings(object instance)
	{
		return ((GalleryPage)instance).OnOpenSettings;
	}

	private void set_50_GalleryPage_OnOpenSettings(object instance, object Value)
	{
		((GalleryPage)instance).OnOpenSettings = (Action)Value;
	}

	private object get_51_GroupDrillDownPage_OnBack(object instance)
	{
		return ((GroupDrillDownPage)instance).OnBack;
	}

	private void set_51_GroupDrillDownPage_OnBack(object instance, object Value)
	{
		((GroupDrillDownPage)instance).OnBack = (Action)Value;
	}

	private object get_52_GroupDrillDownPage_OnPhotoActivated(object instance)
	{
		return ((GroupDrillDownPage)instance).OnPhotoActivated;
	}

	private void set_52_GroupDrillDownPage_OnPhotoActivated(object instance, object Value)
	{
		((GroupDrillDownPage)instance).OnPhotoActivated = (Action<PhotoThumbnailItem>)Value;
	}

	private object get_53_GroupDrillDownPage_OnFavoriteClicked(object instance)
	{
		return ((GroupDrillDownPage)instance).OnFavoriteClicked;
	}

	private void set_53_GroupDrillDownPage_OnFavoriteClicked(object instance, object Value)
	{
		((GroupDrillDownPage)instance).OnFavoriteClicked = (Action<PhotoThumbnailItem>)Value;
	}

	private object get_54_GroupDrillDownPage_OnThumbnailsNeeded(object instance)
	{
		return ((GroupDrillDownPage)instance).OnThumbnailsNeeded;
	}

	private void set_54_GroupDrillDownPage_OnThumbnailsNeeded(object instance, object Value)
	{
		((GroupDrillDownPage)instance).OnThumbnailsNeeded = (Action<IReadOnlyList<PhotoThumbnailItem>>)Value;
	}

	private object get_55_GroupDrillDownPage_CurrentPhotos(object instance)
	{
		return ((GroupDrillDownPage)instance).CurrentPhotos;
	}

	private object get_56_PhotoModalPage_OnClose(object instance)
	{
		return ((PhotoModalPage)instance).OnClose;
	}

	private void set_56_PhotoModalPage_OnClose(object instance, object Value)
	{
		((PhotoModalPage)instance).OnClose = (Action)Value;
	}

	private object get_57_PhotoModalPage_OnGoBack(object instance)
	{
		return ((PhotoModalPage)instance).OnGoBack;
	}

	private void set_57_PhotoModalPage_OnGoBack(object instance, object Value)
	{
		((PhotoModalPage)instance).OnGoBack = (Action)Value;
	}

	private object get_58_PhotoModalPage_OnGoPrev(object instance)
	{
		return ((PhotoModalPage)instance).OnGoPrev;
	}

	private void set_58_PhotoModalPage_OnGoPrev(object instance, object Value)
	{
		((PhotoModalPage)instance).OnGoPrev = (Action)Value;
	}

	private object get_59_PhotoModalPage_OnGoNext(object instance)
	{
		return ((PhotoModalPage)instance).OnGoNext;
	}

	private void set_59_PhotoModalPage_OnGoNext(object instance, object Value)
	{
		((PhotoModalPage)instance).OnGoNext = (Action)Value;
	}

	private object get_60_PhotoModalPage_OnOpenWorld(object instance)
	{
		return ((PhotoModalPage)instance).OnOpenWorld;
	}

	private void set_60_PhotoModalPage_OnOpenWorld(object instance, object Value)
	{
		((PhotoModalPage)instance).OnOpenWorld = (Func<Task>)Value;
	}

	private object get_61_PhotoModalPage_OnOpenExplorer(object instance)
	{
		return ((PhotoModalPage)instance).OnOpenExplorer;
	}

	private void set_61_PhotoModalPage_OnOpenExplorer(object instance, object Value)
	{
		((PhotoModalPage)instance).OnOpenExplorer = (Func<Task>)Value;
	}

	private object get_62_PhotoModalPage_OnTweet(object instance)
	{
		return ((PhotoModalPage)instance).OnTweet;
	}

	private void set_62_PhotoModalPage_OnTweet(object instance, object Value)
	{
		((PhotoModalPage)instance).OnTweet = (Func<Task>)Value;
	}

	private object get_63_PhotoModalPage_OnToggleFavorite(object instance)
	{
		return ((PhotoModalPage)instance).OnToggleFavorite;
	}

	private void set_63_PhotoModalPage_OnToggleFavorite(object instance, object Value)
	{
		((PhotoModalPage)instance).OnToggleFavorite = (Func<Task>)Value;
	}

	private object get_64_PhotoModalPage_OnAddTag(object instance)
	{
		return ((PhotoModalPage)instance).OnAddTag;
	}

	private void set_64_PhotoModalPage_OnAddTag(object instance, object Value)
	{
		((PhotoModalPage)instance).OnAddTag = (Func<string, string, Task>)Value;
	}

	private object get_65_PhotoModalPage_OnRemoveTag(object instance)
	{
		return ((PhotoModalPage)instance).OnRemoveTag;
	}

	private void set_65_PhotoModalPage_OnRemoveTag(object instance, object Value)
	{
		((PhotoModalPage)instance).OnRemoveTag = (Func<string, string, Task>)Value;
	}

	private object get_66_PhotoModalPage_OnOpenTagMaster(object instance)
	{
		return ((PhotoModalPage)instance).OnOpenTagMaster;
	}

	private void set_66_PhotoModalPage_OnOpenTagMaster(object instance, object Value)
	{
		((PhotoModalPage)instance).OnOpenTagMaster = (Action)Value;
	}

	private object get_67_SettingsPage_OnClose(object instance)
	{
		return ((SettingsPage)instance).OnClose;
	}

	private void set_67_SettingsPage_OnClose(object instance, object Value)
	{
		((SettingsPage)instance).OnClose = (Action)Value;
	}

	private object get_68_SettingsPage_OnChooseFolder(object instance)
	{
		return ((SettingsPage)instance).OnChooseFolder;
	}

	private void set_68_SettingsPage_OnChooseFolder(object instance, object Value)
	{
		((SettingsPage)instance).OnChooseFolder = (Func<int, Task>)Value;
	}

	private object get_69_SettingsPage_OnResetFolder(object instance)
	{
		return ((SettingsPage)instance).OnResetFolder;
	}

	private void set_69_SettingsPage_OnResetFolder(object instance, object Value)
	{
		((SettingsPage)instance).OnResetFolder = (Action<int>)Value;
	}

	private object get_70_SettingsPage_OnStartupPreferenceChanged(object instance)
	{
		return ((SettingsPage)instance).OnStartupPreferenceChanged;
	}

	private void set_70_SettingsPage_OnStartupPreferenceChanged(object instance, object Value)
	{
		((SettingsPage)instance).OnStartupPreferenceChanged = (Func<bool, Task>)Value;
	}

	private object get_71_SettingsPage_OnThemeChanged(object instance)
	{
		return ((SettingsPage)instance).OnThemeChanged;
	}

	private void set_71_SettingsPage_OnThemeChanged(object instance, object Value)
	{
		((SettingsPage)instance).OnThemeChanged = (Func<bool, Task>)Value;
	}

	private object get_72_SettingsPage_OnStartWorldAnalysis(object instance)
	{
		return ((SettingsPage)instance).OnStartWorldAnalysis;
	}

	private void set_72_SettingsPage_OnStartWorldAnalysis(object instance, object Value)
	{
		((SettingsPage)instance).OnStartWorldAnalysis = (Func<Task>)Value;
	}

	private object get_73_SettingsPage_OnCreateTag(object instance)
	{
		return ((SettingsPage)instance).OnCreateTag;
	}

	private void set_73_SettingsPage_OnCreateTag(object instance, object Value)
	{
		((SettingsPage)instance).OnCreateTag = (Func<Task>)Value;
	}

	private object get_74_SettingsPage_OnDeleteTag(object instance)
	{
		return ((SettingsPage)instance).OnDeleteTag;
	}

	private void set_74_SettingsPage_OnDeleteTag(object instance, object Value)
	{
		((SettingsPage)instance).OnDeleteTag = (Func<string, Task>)Value;
	}

	private object get_75_SettingsPage_OnCancelEdit(object instance)
	{
		return ((SettingsPage)instance).OnCancelEdit;
	}

	private void set_75_SettingsPage_OnCancelEdit(object instance, object Value)
	{
		((SettingsPage)instance).OnCancelEdit = (Action)Value;
	}

	private object get_76_SettingsPage_OnSaveTemplate(object instance)
	{
		return ((SettingsPage)instance).OnSaveTemplate;
	}

	private void set_76_SettingsPage_OnSaveTemplate(object instance, object Value)
	{
		((SettingsPage)instance).OnSaveTemplate = (Func<Task>)Value;
	}

	private object get_77_SettingsPage_OnStartEdit(object instance)
	{
		return ((SettingsPage)instance).OnStartEdit;
	}

	private void set_77_SettingsPage_OnStartEdit(object instance, object Value)
	{
		((SettingsPage)instance).OnStartEdit = (Action<string>)Value;
	}

	private object get_78_SettingsPage_OnDeleteTemplate(object instance)
	{
		return ((SettingsPage)instance).OnDeleteTemplate;
	}

	private void set_78_SettingsPage_OnDeleteTemplate(object instance, object Value)
	{
		((SettingsPage)instance).OnDeleteTemplate = (Func<string, Task>)Value;
	}

	private object get_79_SettingsPage_OnSelectTemplate(object instance)
	{
		return ((SettingsPage)instance).OnSelectTemplate;
	}

	private void set_79_SettingsPage_OnSelectTemplate(object instance, object Value)
	{
		((SettingsPage)instance).OnSelectTemplate = (Func<string, Task>)Value;
	}

	private object get_80_ProgressRing_IsActive(object instance)
	{
		return ((ProgressRing)instance).IsActive;
	}

	private void set_80_ProgressRing_IsActive(object instance, object Value)
	{
		((ProgressRing)instance).IsActive = (bool)Value;
	}

	private object get_81_ProgressRing_IsIndeterminate(object instance)
	{
		return ((ProgressRing)instance).IsIndeterminate;
	}

	private void set_81_ProgressRing_IsIndeterminate(object instance, object Value)
	{
		((ProgressRing)instance).IsIndeterminate = (bool)Value;
	}

	private object get_82_ProgressRing_Maximum(object instance)
	{
		return ((ProgressRing)instance).Maximum;
	}

	private void set_82_ProgressRing_Maximum(object instance, object Value)
	{
		((ProgressRing)instance).Maximum = (double)Value;
	}

	private object get_83_ProgressRing_Minimum(object instance)
	{
		return ((ProgressRing)instance).Minimum;
	}

	private void set_83_ProgressRing_Minimum(object instance, object Value)
	{
		((ProgressRing)instance).Minimum = (double)Value;
	}

	private object get_84_ProgressRing_TemplateSettings(object instance)
	{
		return ((ProgressRing)instance).TemplateSettings;
	}

	private object get_85_ProgressRing_Value(object instance)
	{
		return ((ProgressRing)instance).Value;
	}

	private void set_85_ProgressRing_Value(object instance, object Value)
	{
		((ProgressRing)instance).Value = (double)Value;
	}

	private object get_86_ShellHeaderBar_OnToggleFilter(object instance)
	{
		return ((ShellHeaderBar)instance).OnToggleFilter;
	}

	private void set_86_ShellHeaderBar_OnToggleFilter(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnToggleFilter = (Action)Value;
	}

	private object get_87_ShellHeaderBar_OnShowSettings(object instance)
	{
		return ((ShellHeaderBar)instance).OnShowSettings;
	}

	private void set_87_ShellHeaderBar_OnShowSettings(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnShowSettings = (Action)Value;
	}

	private object get_88_ShellHeaderBar_OnToggleMultiSelect(object instance)
	{
		return ((ShellHeaderBar)instance).OnToggleMultiSelect;
	}

	private void set_88_ShellHeaderBar_OnToggleMultiSelect(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnToggleMultiSelect = (Action)Value;
	}

	private object get_89_ShellHeaderBar_OnGroupingChange(object instance)
	{
		return ((ShellHeaderBar)instance).OnGroupingChange;
	}

	private void set_89_ShellHeaderBar_OnGroupingChange(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnGroupingChange = (Action<GroupingMode>)Value;
	}

	private object get_90_ShellHeaderBar_OnViewModeChange(object instance)
	{
		return ((ShellHeaderBar)instance).OnViewModeChange;
	}

	private void set_90_ShellHeaderBar_OnViewModeChange(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnViewModeChange = (Func<string, Task>)Value;
	}

	private object get_91_ShellHeaderBar_OnSearchSubmit(object instance)
	{
		return ((ShellHeaderBar)instance).OnSearchSubmit;
	}

	private void set_91_ShellHeaderBar_OnSearchSubmit(object instance, object Value)
	{
		((ShellHeaderBar)instance).OnSearchSubmit = (Action)Value;
	}

	private object get_92_ScanningOverlay_OnCancelScan(object instance)
	{
		return ((ScanningOverlay)instance).OnCancelScan;
	}

	private void set_92_ScanningOverlay_OnCancelScan(object instance, object Value)
	{
		((ScanningOverlay)instance).OnCancelScan = (Func<Task>)Value;
	}

	private object get_93_ShellStage_MainContent(object instance)
	{
		return ((ShellStage)instance).MainContent;
	}

	private void set_93_ShellStage_MainContent(object instance, object Value)
	{
		((ShellStage)instance).MainContent = Value;
	}

	private object get_94_ShellStage_ModalContent(object instance)
	{
		return ((ShellStage)instance).ModalContent;
	}

	private void set_94_ShellStage_ModalContent(object instance, object Value)
	{
		((ShellStage)instance).ModalContent = Value;
	}

	private object get_95_ShellStage_ModalVisibility(object instance)
	{
		return ((ShellStage)instance).ModalVisibility;
	}

	private void set_95_ShellStage_ModalVisibility(object instance, object Value)
	{
		((ShellStage)instance).ModalVisibility = (Visibility)Value;
	}

	private object get_96_ShellStage_TopModalContent(object instance)
	{
		return ((ShellStage)instance).TopModalContent;
	}

	private void set_96_ShellStage_TopModalContent(object instance, object Value)
	{
		((ShellStage)instance).TopModalContent = Value;
	}

	private object get_97_ShellStage_TopModalVisibility(object instance)
	{
		return ((ShellStage)instance).TopModalVisibility;
	}

	private void set_97_ShellStage_TopModalVisibility(object instance, object Value)
	{
		((ShellStage)instance).TopModalVisibility = (Visibility)Value;
	}

	private object get_98_ShellStage_ScanningOverlayVisibility(object instance)
	{
		return ((ShellStage)instance).ScanningOverlayVisibility;
	}

	private void set_98_ShellStage_ScanningOverlayVisibility(object instance, object Value)
	{
		((ShellStage)instance).ScanningOverlayVisibility = (Visibility)Value;
	}

	private object get_99_ShellStage_ScanningOverlayDataContext(object instance)
	{
		return ((ShellStage)instance).ScanningOverlayDataContext;
	}

	private void set_99_ShellStage_ScanningOverlayDataContext(object instance, object Value)
	{
		((ShellStage)instance).ScanningOverlayDataContext = Value;
	}

	private object get_100_ShellStage_ToastDataContext(object instance)
	{
		return ((ShellStage)instance).ToastDataContext;
	}

	private void set_100_ShellStage_ToastDataContext(object instance, object Value)
	{
		((ShellStage)instance).ToastDataContext = Value;
	}

	private object get_101_ShellStage_ScanningOverlayControlRef(object instance)
	{
		return ((ShellStage)instance).ScanningOverlayControlRef;
	}

	private object get_102_ThumbnailSourceConverter_DecodePixelWidth(object instance)
	{
		return ((ThumbnailSourceConverter)instance).DecodePixelWidth;
	}

	private void set_102_ThumbnailSourceConverter_DecodePixelWidth(object instance, object Value)
	{
		((ThumbnailSourceConverter)instance).DecodePixelWidth = (int)Value;
	}

	private object get_103_WorldResolvePage_OnClose(object instance)
	{
		return ((WorldResolvePage)instance).OnClose;
	}

	private void set_103_WorldResolvePage_OnClose(object instance, object Value)
	{
		((WorldResolvePage)instance).OnClose = (Action)Value;
	}

	private object get_104_WorldResolvePage_OnApplied(object instance)
	{
		return ((WorldResolvePage)instance).OnApplied;
	}

	private void set_104_WorldResolvePage_OnApplied(object instance, object Value)
	{
		((WorldResolvePage)instance).OnApplied = (Func<Task>)Value;
	}

	private object get_105_RadialGradientBrush_GradientStops(object instance)
	{
		return ((RadialGradientBrush)instance).GradientStops;
	}

	private object get_106_RadialGradientBrush_Center(object instance)
	{
		return ((RadialGradientBrush)instance).Center;
	}

	private void set_106_RadialGradientBrush_Center(object instance, object Value)
	{
		((RadialGradientBrush)instance).Center = (Point)Value;
	}

	private object get_107_RadialGradientBrush_GradientOrigin(object instance)
	{
		return ((RadialGradientBrush)instance).GradientOrigin;
	}

	private void set_107_RadialGradientBrush_GradientOrigin(object instance, object Value)
	{
		((RadialGradientBrush)instance).GradientOrigin = (Point)Value;
	}

	private object get_108_RadialGradientBrush_InterpolationSpace(object instance)
	{
		return ((RadialGradientBrush)instance).InterpolationSpace;
	}

	private void set_108_RadialGradientBrush_InterpolationSpace(object instance, object Value)
	{
		((RadialGradientBrush)instance).InterpolationSpace = (CompositionColorSpace)Value;
	}

	private object get_109_RadialGradientBrush_MappingMode(object instance)
	{
		return ((RadialGradientBrush)instance).MappingMode;
	}

	private void set_109_RadialGradientBrush_MappingMode(object instance, object Value)
	{
		((RadialGradientBrush)instance).MappingMode = (BrushMappingMode)Value;
	}

	private object get_110_RadialGradientBrush_RadiusX(object instance)
	{
		return ((RadialGradientBrush)instance).RadiusX;
	}

	private void set_110_RadialGradientBrush_RadiusX(object instance, object Value)
	{
		((RadialGradientBrush)instance).RadiusX = (double)Value;
	}

	private object get_111_RadialGradientBrush_RadiusY(object instance)
	{
		return ((RadialGradientBrush)instance).RadiusY;
	}

	private void set_111_RadialGradientBrush_RadiusY(object instance, object Value)
	{
		((RadialGradientBrush)instance).RadiusY = (double)Value;
	}

	private object get_112_RadialGradientBrush_SpreadMethod(object instance)
	{
		return ((RadialGradientBrush)instance).SpreadMethod;
	}

	private void set_112_RadialGradientBrush_SpreadMethod(object instance, object Value)
	{
		((RadialGradientBrush)instance).SpreadMethod = (GradientSpreadMethod)Value;
	}

	private object get_113_CustomScrollbar_ThumbTop(object instance)
	{
		return ((CustomScrollbar)instance).ThumbTop;
	}

	private void set_113_CustomScrollbar_ThumbTop(object instance, object Value)
	{
		((CustomScrollbar)instance).ThumbTop = (double)Value;
	}

	private object get_114_CustomScrollbar_ThumbHeight(object instance)
	{
		return ((CustomScrollbar)instance).ThumbHeight;
	}

	private void set_114_CustomScrollbar_ThumbHeight(object instance, object Value)
	{
		((CustomScrollbar)instance).ThumbHeight = (double)Value;
	}

	private object get_115_CustomScrollbar_OnTrackClick(object instance)
	{
		return ((CustomScrollbar)instance).OnTrackClick;
	}

	private void set_115_CustomScrollbar_OnTrackClick(object instance, object Value)
	{
		((CustomScrollbar)instance).OnTrackClick = (Action<double>)Value;
	}

	private object get_116_CustomScrollbar_OnDrag(object instance)
	{
		return ((CustomScrollbar)instance).OnDrag;
	}

	private void set_116_CustomScrollbar_OnDrag(object instance, object Value)
	{
		((CustomScrollbar)instance).OnDrag = (Action<double>)Value;
	}

	private object get_117_PhotoGridItemsView_OnPhotoActivated(object instance)
	{
		return ((PhotoGridItemsView)instance).OnPhotoActivated;
	}

	private void set_117_PhotoGridItemsView_OnPhotoActivated(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnPhotoActivated = (Action<PhotoGridItem>)Value;
	}

	private object get_118_PhotoGridItemsView_OnFavoriteClicked(object instance)
	{
		return ((PhotoGridItemsView)instance).OnFavoriteClicked;
	}

	private void set_118_PhotoGridItemsView_OnFavoriteClicked(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnFavoriteClicked = (Action<PhotoGridItem>)Value;
	}

	private object get_119_PhotoGridItemsView_OnGridScroll(object instance)
	{
		return ((PhotoGridItemsView)instance).OnGridScroll;
	}

	private void set_119_PhotoGridItemsView_OnGridScroll(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnGridScroll = (Action<double>)Value;
	}

	private object get_120_PhotoGridItemsView_OnGridWheel(object instance)
	{
		return ((PhotoGridItemsView)instance).OnGridWheel;
	}

	private void set_120_PhotoGridItemsView_OnGridWheel(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnGridWheel = (Action<int>)Value;
	}

	private object get_121_PhotoGridItemsView_OnNearBottomReached(object instance)
	{
		return ((PhotoGridItemsView)instance).OnNearBottomReached;
	}

	private void set_121_PhotoGridItemsView_OnNearBottomReached(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnNearBottomReached = (Action)Value;
	}

	private object get_122_PhotoGridItemsView_OnFirstVisibleIndexChanged(object instance)
	{
		return ((PhotoGridItemsView)instance).OnFirstVisibleIndexChanged;
	}

	private void set_122_PhotoGridItemsView_OnFirstVisibleIndexChanged(object instance, object Value)
	{
		((PhotoGridItemsView)instance).OnFirstVisibleIndexChanged = (Action<int>)Value;
	}

	private object get_123_PhotoGridItemsView_GridScrollViewerRef(object instance)
	{
		return ((PhotoGridItemsView)instance).GridScrollViewerRef;
	}

	private object get_124_ProgressBar_IsIndeterminate(object instance)
	{
		return ((ProgressBar)instance).IsIndeterminate;
	}

	private void set_124_ProgressBar_IsIndeterminate(object instance, object Value)
	{
		((ProgressBar)instance).IsIndeterminate = (bool)Value;
	}

	private object get_125_ProgressBar_ShowError(object instance)
	{
		return ((ProgressBar)instance).ShowError;
	}

	private void set_125_ProgressBar_ShowError(object instance, object Value)
	{
		((ProgressBar)instance).ShowError = (bool)Value;
	}

	private object get_126_ProgressBar_ShowPaused(object instance)
	{
		return ((ProgressBar)instance).ShowPaused;
	}

	private void set_126_ProgressBar_ShowPaused(object instance, object Value)
	{
		((ProgressBar)instance).ShowPaused = (bool)Value;
	}

	private object get_127_ProgressBar_TemplateSettings(object instance)
	{
		return ((ProgressBar)instance).TemplateSettings;
	}

	private object get_128_Thickness_Left(object instance)
	{
		return ((Thickness)instance).Left;
	}

	private void set_128_Thickness_Left(object instance, object Value)
	{
		Thickness thickness = (Thickness)instance;
		thickness.Left = (double)Value;
	}

	private object get_129_Thickness_Top(object instance)
	{
		return ((Thickness)instance).Top;
	}

	private void set_129_Thickness_Top(object instance, object Value)
	{
		Thickness thickness = (Thickness)instance;
		thickness.Top = (double)Value;
	}

	private object get_130_Thickness_Right(object instance)
	{
		return ((Thickness)instance).Right;
	}

	private void set_130_Thickness_Right(object instance, object Value)
	{
		Thickness thickness = (Thickness)instance;
		thickness.Right = (double)Value;
	}

	private object get_131_Thickness_Bottom(object instance)
	{
		return ((Thickness)instance).Bottom;
	}

	private void set_131_Thickness_Bottom(object instance, object Value)
	{
		Thickness thickness = (Thickness)instance;
		thickness.Bottom = (double)Value;
	}

	private object get_132_CornerRadius_TopLeft(object instance)
	{
		return ((CornerRadius)instance).TopLeft;
	}

	private void set_132_CornerRadius_TopLeft(object instance, object Value)
	{
		CornerRadius cornerRadius = (CornerRadius)instance;
		cornerRadius.TopLeft = (double)Value;
	}

	private object get_133_CornerRadius_TopRight(object instance)
	{
		return ((CornerRadius)instance).TopRight;
	}

	private void set_133_CornerRadius_TopRight(object instance, object Value)
	{
		CornerRadius cornerRadius = (CornerRadius)instance;
		cornerRadius.TopRight = (double)Value;
	}

	private object get_134_CornerRadius_BottomRight(object instance)
	{
		return ((CornerRadius)instance).BottomRight;
	}

	private void set_134_CornerRadius_BottomRight(object instance, object Value)
	{
		CornerRadius cornerRadius = (CornerRadius)instance;
		cornerRadius.BottomRight = (double)Value;
	}

	private object get_135_CornerRadius_BottomLeft(object instance)
	{
		return ((CornerRadius)instance).BottomLeft;
	}

	private void set_135_CornerRadius_BottomLeft(object instance, object Value)
	{
		CornerRadius cornerRadius = (CornerRadius)instance;
		cornerRadius.BottomLeft = (double)Value;
	}

	private object get_136_TreeViewNode_Children(object instance)
	{
		return ((TreeViewNode)instance).Children;
	}

	private object get_137_TreeViewNode_Content(object instance)
	{
		return ((TreeViewNode)instance).Content;
	}

	private void set_137_TreeViewNode_Content(object instance, object Value)
	{
		((TreeViewNode)instance).Content = Value;
	}

	private object get_138_TreeViewNode_Depth(object instance)
	{
		return ((TreeViewNode)instance).Depth;
	}

	private object get_139_TreeViewNode_HasChildren(object instance)
	{
		return ((TreeViewNode)instance).HasChildren;
	}

	private object get_140_TreeViewNode_HasUnrealizedChildren(object instance)
	{
		return ((TreeViewNode)instance).HasUnrealizedChildren;
	}

	private void set_140_TreeViewNode_HasUnrealizedChildren(object instance, object Value)
	{
		((TreeViewNode)instance).HasUnrealizedChildren = (bool)Value;
	}

	private object get_141_TreeViewNode_IsExpanded(object instance)
	{
		return ((TreeViewNode)instance).IsExpanded;
	}

	private void set_141_TreeViewNode_IsExpanded(object instance, object Value)
	{
		((TreeViewNode)instance).IsExpanded = (bool)Value;
	}

	private object get_142_TreeViewNode_Parent(object instance)
	{
		return ((TreeViewNode)instance).Parent;
	}

	private IXamlMember CreateXamlMember(string longMemberName)
	{
		XamlMember xamlMember = null;
		switch (longMemberName)
		{
		case "Microsoft.UI.Xaml.Controls.XamlControlsResources.UseCompactResources":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.XamlControlsResources");
			xamlMember = new XamlMember(this, "UseCompactResources", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_0_XamlControlsResources_UseCompactResources;
			xamlMember.Setter = set_0_XamlControlsResources_UseCompactResources;
			break;
		case "Alpheratz.Shared.Controls.AppIcon.IconName":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AppIcon");
			xamlMember = new XamlMember(this, "IconName", "String");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_1_AppIcon_IconName;
			xamlMember.Setter = set_1_AppIcon_IconName;
			break;
		case "Alpheratz.Shared.Controls.AppIcon.IconSize":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AppIcon");
			xamlMember = new XamlMember(this, "IconSize", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_2_AppIcon_IconSize;
			xamlMember.Setter = set_2_AppIcon_IconSize;
			break;
		case "Alpheratz.Shared.Controls.AppIcon.Foreground":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AppIcon");
			xamlMember = new XamlMember(this, "Foreground", "Microsoft.UI.Xaml.Media.Brush");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_3_AppIcon_Foreground;
			xamlMember.Setter = set_3_AppIcon_Foreground;
			break;
		case "Alpheratz.Shared.Controls.AnimatedFavoriteStar.Liked":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AnimatedFavoriteStar");
			xamlMember = new XamlMember(this, "Liked", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_4_AnimatedFavoriteStar_Liked;
			xamlMember.Setter = set_4_AnimatedFavoriteStar_Liked;
			break;
		case "Alpheratz.Shared.Controls.AnimatedFavoriteStar.Interactive":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AnimatedFavoriteStar");
			xamlMember = new XamlMember(this, "Interactive", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_5_AnimatedFavoriteStar_Interactive;
			xamlMember.Setter = set_5_AnimatedFavoriteStar_Interactive;
			break;
		case "Alpheratz.Shared.Controls.AnimatedFavoriteStar.StarFill":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AnimatedFavoriteStar");
			xamlMember = new XamlMember(this, "StarFill", "Microsoft.UI.Xaml.Media.Brush");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_6_AnimatedFavoriteStar_StarFill;
			xamlMember.Setter = set_6_AnimatedFavoriteStar_StarFill;
			break;
		case "Alpheratz.Shared.Controls.AnimatedFavoriteStar.StarStroke":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AnimatedFavoriteStar");
			xamlMember = new XamlMember(this, "StarStroke", "Microsoft.UI.Xaml.Media.Brush");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_7_AnimatedFavoriteStar_StarStroke;
			xamlMember.Setter = set_7_AnimatedFavoriteStar_StarStroke;
			break;
		case "Alpheratz.Shared.Controls.AnimatedFavoriteStar.OnClick":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.AnimatedFavoriteStar");
			xamlMember = new XamlMember(this, "OnClick", "System.Action");
			xamlMember.Getter = get_8_AnimatedFavoriteStar_OnClick;
			xamlMember.Setter = set_8_AnimatedFavoriteStar_OnClick;
			break;
		case "Alpheratz.Shared.Controls.WrapPanel.HorizontalSpacing":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.WrapPanel");
			xamlMember = new XamlMember(this, "HorizontalSpacing", "Double");
			xamlMember.Getter = get_9_WrapPanel_HorizontalSpacing;
			xamlMember.Setter = set_9_WrapPanel_HorizontalSpacing;
			break;
		case "Alpheratz.Shared.Controls.WrapPanel.VerticalSpacing":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.WrapPanel");
			xamlMember = new XamlMember(this, "VerticalSpacing", "Double");
			xamlMember.Getter = get_10_WrapPanel_VerticalSpacing;
			xamlMember.Setter = set_10_WrapPanel_VerticalSpacing;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnResetFilters":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnResetFilters", "System.Action");
			xamlMember.Getter = get_11_GalleryFilterPanel_OnResetFilters;
			xamlMember.Setter = set_11_GalleryFilterPanel_OnResetFilters;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnDatePresetSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnDatePresetSelect", "System.Action`1<String>");
			xamlMember.Getter = get_12_GalleryFilterPanel_OnDatePresetSelect;
			xamlMember.Setter = set_12_GalleryFilterPanel_OnDatePresetSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnOrientationSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnOrientationSelect", "System.Action`1<String>");
			xamlMember.Getter = get_13_GalleryFilterPanel_OnOrientationSelect;
			xamlMember.Setter = set_13_GalleryFilterPanel_OnOrientationSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnSortSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnSortSelect", "System.Action`1<Alpheratz.Shared.Models.SortMode>");
			xamlMember.Getter = get_14_GalleryFilterPanel_OnSortSelect;
			xamlMember.Setter = set_14_GalleryFilterPanel_OnSortSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnDisplayFolderSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnDisplayFolderSelect", "System.Action`1<Alpheratz.Shared.Models.DisplayFolderMode>");
			xamlMember.Getter = get_15_GalleryFilterPanel_OnDisplayFolderSelect;
			xamlMember.Setter = set_15_GalleryFilterPanel_OnDisplayFolderSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnGroupingSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnGroupingSelect", "System.Action`1<Alpheratz.Shared.Models.GroupingMode>");
			xamlMember.Getter = get_16_GalleryFilterPanel_OnGroupingSelect;
			xamlMember.Setter = set_16_GalleryFilterPanel_OnGroupingSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnWorldFilterAdd":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnWorldFilterAdd", "System.Action`1<String>");
			xamlMember.Getter = get_17_GalleryFilterPanel_OnWorldFilterAdd;
			xamlMember.Setter = set_17_GalleryFilterPanel_OnWorldFilterAdd;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnWorldFilterRemove":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnWorldFilterRemove", "System.Action`1<String>");
			xamlMember.Getter = get_18_GalleryFilterPanel_OnWorldFilterRemove;
			xamlMember.Setter = set_18_GalleryFilterPanel_OnWorldFilterRemove;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnTagFilterAdd":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnTagFilterAdd", "System.Action`1<String>");
			xamlMember.Getter = get_19_GalleryFilterPanel_OnTagFilterAdd;
			xamlMember.Setter = set_19_GalleryFilterPanel_OnTagFilterAdd;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterPanel.OnTagFilterRemove":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterPanel");
			xamlMember = new XamlMember(this, "OnTagFilterRemove", "System.Action`1<String>");
			xamlMember.Getter = get_20_GalleryFilterPanel_OnTagFilterRemove;
			xamlMember.Setter = set_20_GalleryFilterPanel_OnTagFilterRemove;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterSidebar.OnResetFilters":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterSidebar");
			xamlMember = new XamlMember(this, "OnResetFilters", "System.Action");
			xamlMember.Getter = get_21_GalleryFilterSidebar_OnResetFilters;
			xamlMember.Setter = set_21_GalleryFilterSidebar_OnResetFilters;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryFilterSidebar.OnDatePresetSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryFilterSidebar");
			xamlMember = new XamlMember(this, "OnDatePresetSelect", "System.Action`1<String>");
			xamlMember.Getter = get_22_GalleryFilterSidebar_OnDatePresetSelect;
			xamlMember.Setter = set_22_GalleryFilterSidebar_OnDatePresetSelect;
			break;
		case "Alpheratz.Features.Gallery.Controls.MonthNav.OnJumpToMonth":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.MonthNav");
			xamlMember = new XamlMember(this, "OnJumpToMonth", "System.Action`1<Alpheratz.Features.Gallery.GalleryMonthGroup>");
			xamlMember.Getter = get_23_MonthNav_OnJumpToMonth;
			xamlMember.Setter = set_23_MonthNav_OnJumpToMonth;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnGoToPrevPage":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnGoToPrevPage", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_24_PhotoGrid_OnGoToPrevPage;
			xamlMember.Setter = set_24_PhotoGrid_OnGoToPrevPage;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnGoToNextPage":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnGoToNextPage", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_25_PhotoGrid_OnGoToNextPage;
			xamlMember.Setter = set_25_PhotoGrid_OnGoToNextPage;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnLoadMorePhotos":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnLoadMorePhotos", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_26_PhotoGrid_OnLoadMorePhotos;
			xamlMember.Setter = set_26_PhotoGrid_OnLoadMorePhotos;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnRightPanelMeasured":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnRightPanelMeasured", "System.Action`1<Double>");
			xamlMember.Getter = get_27_PhotoGrid_OnRightPanelMeasured;
			xamlMember.Setter = set_27_PhotoGrid_OnRightPanelMeasured;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnGridWrapperMeasured":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnGridWrapperMeasured", "System.Action`1<Double>");
			xamlMember.Getter = get_28_PhotoGrid_OnGridWrapperMeasured;
			xamlMember.Setter = set_28_PhotoGrid_OnGridWrapperMeasured;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnPhotoActivated":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnPhotoActivated", "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>");
			xamlMember.Getter = get_29_PhotoGrid_OnPhotoActivated;
			xamlMember.Setter = set_29_PhotoGrid_OnPhotoActivated;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnFavoriteClicked":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnFavoriteClicked", "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>");
			xamlMember.Getter = get_30_PhotoGrid_OnFavoriteClicked;
			xamlMember.Setter = set_30_PhotoGrid_OnFavoriteClicked;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnGridScroll":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnGridScroll", "System.Action`1<Double>");
			xamlMember.Getter = get_31_PhotoGrid_OnGridScroll;
			xamlMember.Setter = set_31_PhotoGrid_OnGridScroll;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnGridWheel":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnGridWheel", "System.Action`1<Int32>");
			xamlMember.Getter = get_32_PhotoGrid_OnGridWheel;
			xamlMember.Setter = set_32_PhotoGrid_OnGridWheel;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnFirstVisibleIndexChanged":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnFirstVisibleIndexChanged", "System.Action`1<Int32>");
			xamlMember.Getter = get_33_PhotoGrid_OnFirstVisibleIndexChanged;
			xamlMember.Setter = set_33_PhotoGrid_OnFirstVisibleIndexChanged;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnScrollbarTrackClick":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnScrollbarTrackClick", "System.Action`1<Double>");
			xamlMember.Getter = get_34_PhotoGrid_OnScrollbarTrackClick;
			xamlMember.Setter = set_34_PhotoGrid_OnScrollbarTrackClick;
			break;
		case "Alpheratz.Shared.Controls.PhotoGrid.OnScrollbarDrag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember = new XamlMember(this, "OnScrollbarDrag", "System.Action`1<Double>");
			xamlMember.Getter = get_35_PhotoGrid_OnScrollbarDrag;
			xamlMember.Setter = set_35_PhotoGrid_OnScrollbarDrag;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryMasonryView.OnPhotoTapped":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryMasonryView");
			xamlMember = new XamlMember(this, "OnPhotoTapped", "System.Action`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>");
			xamlMember.Getter = get_36_GalleryMasonryView_OnPhotoTapped;
			xamlMember.Setter = set_36_GalleryMasonryView_OnPhotoTapped;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryMasonryView.OnThumbnailsNeeded":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryMasonryView");
			xamlMember = new XamlMember(this, "OnThumbnailsNeeded", "System.Action`1<System.Collections.Generic.IReadOnlyList`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>>");
			xamlMember.Getter = get_37_GalleryMasonryView_OnThumbnailsNeeded;
			xamlMember.Setter = set_37_GalleryMasonryView_OnThumbnailsNeeded;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryMasonryView.OnFirstVisibleIndexChanged":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryMasonryView");
			xamlMember = new XamlMember(this, "OnFirstVisibleIndexChanged", "System.Action`1<Int32>");
			xamlMember.Getter = get_38_GalleryMasonryView_OnFirstVisibleIndexChanged;
			xamlMember.Setter = set_38_GalleryMasonryView_OnFirstVisibleIndexChanged;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.OnMasonryRealized":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "OnMasonryRealized", "System.Action`1<Alpheratz.Features.Gallery.Controls.GalleryMasonryView>");
			xamlMember.Getter = get_39_GalleryGridStage_OnMasonryRealized;
			xamlMember.Setter = set_39_GalleryGridStage_OnMasonryRealized;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.GridDataContext":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "GridDataContext", "Object");
			xamlMember.Getter = get_40_GalleryGridStage_GridDataContext;
			xamlMember.Setter = set_40_GalleryGridStage_GridDataContext;
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.PhotoGridControlRef":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "PhotoGridControlRef", "Alpheratz.Shared.Controls.PhotoGrid");
			xamlMember.Getter = get_41_GalleryGridStage_PhotoGridControlRef;
			xamlMember.SetIsReadOnly();
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.MasonryViewControlRef":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "MasonryViewControlRef", "Alpheratz.Features.Gallery.Controls.GalleryMasonryView");
			xamlMember.Getter = get_42_GalleryGridStage_MasonryViewControlRef;
			xamlMember.SetIsReadOnly();
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.MonthNavControlRef":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "MonthNavControlRef", "Alpheratz.Features.Gallery.Controls.MonthNav");
			xamlMember.Getter = get_43_GalleryGridStage_MonthNavControlRef;
			xamlMember.SetIsReadOnly();
			break;
		case "Alpheratz.Features.Gallery.Controls.GalleryGridStage.EmptyStateVisibility":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.Controls.GalleryGridStage");
			xamlMember = new XamlMember(this, "EmptyStateVisibility", "Microsoft.UI.Xaml.Visibility");
			xamlMember.Getter = get_44_GalleryGridStage_EmptyStateVisibility;
			xamlMember.Setter = set_44_GalleryGridStage_EmptyStateVisibility;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnResetFilters":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnResetFilters", "System.Action");
			xamlMember.Getter = get_45_GalleryPage_OnResetFilters;
			xamlMember.Setter = set_45_GalleryPage_OnResetFilters;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnDatePresetSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnDatePresetSelect", "System.Action`1<String>");
			xamlMember.Getter = get_46_GalleryPage_OnDatePresetSelect;
			xamlMember.Setter = set_46_GalleryPage_OnDatePresetSelect;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnSelectPhoto":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnSelectPhoto", "System.Action`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>");
			xamlMember.Getter = get_47_GalleryPage_OnSelectPhoto;
			xamlMember.Setter = set_47_GalleryPage_OnSelectPhoto;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnDrillIntoGroup":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnDrillIntoGroup", "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>");
			xamlMember.Getter = get_48_GalleryPage_OnDrillIntoGroup;
			xamlMember.Setter = set_48_GalleryPage_OnDrillIntoGroup;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnChooseFolder":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnChooseFolder", "System.Func`1<System.Threading.Tasks.Task`1<String>>");
			xamlMember.Getter = get_49_GalleryPage_OnChooseFolder;
			xamlMember.Setter = set_49_GalleryPage_OnChooseFolder;
			break;
		case "Alpheratz.Features.Gallery.GalleryPage.OnOpenSettings":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GalleryPage");
			xamlMember = new XamlMember(this, "OnOpenSettings", "System.Action");
			xamlMember.Getter = get_50_GalleryPage_OnOpenSettings;
			xamlMember.Setter = set_50_GalleryPage_OnOpenSettings;
			break;
		case "Alpheratz.Features.Gallery.GroupDrillDownPage.OnBack":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GroupDrillDownPage");
			xamlMember = new XamlMember(this, "OnBack", "System.Action");
			xamlMember.Getter = get_51_GroupDrillDownPage_OnBack;
			xamlMember.Setter = set_51_GroupDrillDownPage_OnBack;
			break;
		case "Alpheratz.Features.Gallery.GroupDrillDownPage.OnPhotoActivated":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GroupDrillDownPage");
			xamlMember = new XamlMember(this, "OnPhotoActivated", "System.Action`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>");
			xamlMember.Getter = get_52_GroupDrillDownPage_OnPhotoActivated;
			xamlMember.Setter = set_52_GroupDrillDownPage_OnPhotoActivated;
			break;
		case "Alpheratz.Features.Gallery.GroupDrillDownPage.OnFavoriteClicked":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GroupDrillDownPage");
			xamlMember = new XamlMember(this, "OnFavoriteClicked", "System.Action`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>");
			xamlMember.Getter = get_53_GroupDrillDownPage_OnFavoriteClicked;
			xamlMember.Setter = set_53_GroupDrillDownPage_OnFavoriteClicked;
			break;
		case "Alpheratz.Features.Gallery.GroupDrillDownPage.OnThumbnailsNeeded":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GroupDrillDownPage");
			xamlMember = new XamlMember(this, "OnThumbnailsNeeded", "System.Action`1<System.Collections.Generic.IReadOnlyList`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>>");
			xamlMember.Getter = get_54_GroupDrillDownPage_OnThumbnailsNeeded;
			xamlMember.Setter = set_54_GroupDrillDownPage_OnThumbnailsNeeded;
			break;
		case "Alpheratz.Features.Gallery.GroupDrillDownPage.CurrentPhotos":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Gallery.GroupDrillDownPage");
			xamlMember = new XamlMember(this, "CurrentPhotos", "System.Collections.Generic.IReadOnlyList`1<Alpheratz.Features.Gallery.PhotoThumbnailItem>");
			xamlMember.Getter = get_55_GroupDrillDownPage_CurrentPhotos;
			xamlMember.SetIsReadOnly();
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnClose":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnClose", "System.Action");
			xamlMember.Getter = get_56_PhotoModalPage_OnClose;
			xamlMember.Setter = set_56_PhotoModalPage_OnClose;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnGoBack":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnGoBack", "System.Action");
			xamlMember.Getter = get_57_PhotoModalPage_OnGoBack;
			xamlMember.Setter = set_57_PhotoModalPage_OnGoBack;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnGoPrev":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnGoPrev", "System.Action");
			xamlMember.Getter = get_58_PhotoModalPage_OnGoPrev;
			xamlMember.Setter = set_58_PhotoModalPage_OnGoPrev;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnGoNext":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnGoNext", "System.Action");
			xamlMember.Getter = get_59_PhotoModalPage_OnGoNext;
			xamlMember.Setter = set_59_PhotoModalPage_OnGoNext;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnOpenWorld":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnOpenWorld", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_60_PhotoModalPage_OnOpenWorld;
			xamlMember.Setter = set_60_PhotoModalPage_OnOpenWorld;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnOpenExplorer":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnOpenExplorer", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_61_PhotoModalPage_OnOpenExplorer;
			xamlMember.Setter = set_61_PhotoModalPage_OnOpenExplorer;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnTweet":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnTweet", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_62_PhotoModalPage_OnTweet;
			xamlMember.Setter = set_62_PhotoModalPage_OnTweet;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnToggleFavorite":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnToggleFavorite", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_63_PhotoModalPage_OnToggleFavorite;
			xamlMember.Setter = set_63_PhotoModalPage_OnToggleFavorite;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnAddTag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnAddTag", "System.Func`3<String, String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_64_PhotoModalPage_OnAddTag;
			xamlMember.Setter = set_64_PhotoModalPage_OnAddTag;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnRemoveTag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnRemoveTag", "System.Func`3<String, String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_65_PhotoModalPage_OnRemoveTag;
			xamlMember.Setter = set_65_PhotoModalPage_OnRemoveTag;
			break;
		case "Alpheratz.Features.PhotoModal.PhotoModalPage.OnOpenTagMaster":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.PhotoModal.PhotoModalPage");
			xamlMember = new XamlMember(this, "OnOpenTagMaster", "System.Action");
			xamlMember.Getter = get_66_PhotoModalPage_OnOpenTagMaster;
			xamlMember.Setter = set_66_PhotoModalPage_OnOpenTagMaster;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnClose":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnClose", "System.Action");
			xamlMember.Getter = get_67_SettingsPage_OnClose;
			xamlMember.Setter = set_67_SettingsPage_OnClose;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnChooseFolder":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnChooseFolder", "System.Func`2<Int32, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_68_SettingsPage_OnChooseFolder;
			xamlMember.Setter = set_68_SettingsPage_OnChooseFolder;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnResetFolder":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnResetFolder", "System.Action`1<Int32>");
			xamlMember.Getter = get_69_SettingsPage_OnResetFolder;
			xamlMember.Setter = set_69_SettingsPage_OnResetFolder;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnStartupPreferenceChanged":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnStartupPreferenceChanged", "System.Func`2<Boolean, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_70_SettingsPage_OnStartupPreferenceChanged;
			xamlMember.Setter = set_70_SettingsPage_OnStartupPreferenceChanged;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnThemeChanged":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnThemeChanged", "System.Func`2<Boolean, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_71_SettingsPage_OnThemeChanged;
			xamlMember.Setter = set_71_SettingsPage_OnThemeChanged;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnStartWorldAnalysis":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnStartWorldAnalysis", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_72_SettingsPage_OnStartWorldAnalysis;
			xamlMember.Setter = set_72_SettingsPage_OnStartWorldAnalysis;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnCreateTag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnCreateTag", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_73_SettingsPage_OnCreateTag;
			xamlMember.Setter = set_73_SettingsPage_OnCreateTag;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnDeleteTag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnDeleteTag", "System.Func`2<String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_74_SettingsPage_OnDeleteTag;
			xamlMember.Setter = set_74_SettingsPage_OnDeleteTag;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnCancelEdit":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnCancelEdit", "System.Action");
			xamlMember.Getter = get_75_SettingsPage_OnCancelEdit;
			xamlMember.Setter = set_75_SettingsPage_OnCancelEdit;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnSaveTemplate":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnSaveTemplate", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_76_SettingsPage_OnSaveTemplate;
			xamlMember.Setter = set_76_SettingsPage_OnSaveTemplate;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnStartEdit":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnStartEdit", "System.Action`1<String>");
			xamlMember.Getter = get_77_SettingsPage_OnStartEdit;
			xamlMember.Setter = set_77_SettingsPage_OnStartEdit;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnDeleteTemplate":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnDeleteTemplate", "System.Func`2<String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_78_SettingsPage_OnDeleteTemplate;
			xamlMember.Setter = set_78_SettingsPage_OnDeleteTemplate;
			break;
		case "Alpheratz.Features.Settings.SettingsPage.OnSelectTemplate":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Settings.SettingsPage");
			xamlMember = new XamlMember(this, "OnSelectTemplate", "System.Func`2<String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_79_SettingsPage_OnSelectTemplate;
			xamlMember.Setter = set_79_SettingsPage_OnSelectTemplate;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.IsActive":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "IsActive", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_80_ProgressRing_IsActive;
			xamlMember.Setter = set_80_ProgressRing_IsActive;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.IsIndeterminate":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "IsIndeterminate", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_81_ProgressRing_IsIndeterminate;
			xamlMember.Setter = set_81_ProgressRing_IsIndeterminate;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.Maximum":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "Maximum", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_82_ProgressRing_Maximum;
			xamlMember.Setter = set_82_ProgressRing_Maximum;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.Minimum":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "Minimum", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_83_ProgressRing_Minimum;
			xamlMember.Setter = set_83_ProgressRing_Minimum;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.TemplateSettings":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "TemplateSettings", "Microsoft.UI.Xaml.Controls.ProgressRingTemplateSettings");
			xamlMember.Getter = get_84_ProgressRing_TemplateSettings;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressRing.Value":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressRing");
			xamlMember = new XamlMember(this, "Value", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_85_ProgressRing_Value;
			xamlMember.Setter = set_85_ProgressRing_Value;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnToggleFilter":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnToggleFilter", "System.Action");
			xamlMember.Getter = get_86_ShellHeaderBar_OnToggleFilter;
			xamlMember.Setter = set_86_ShellHeaderBar_OnToggleFilter;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnShowSettings":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnShowSettings", "System.Action");
			xamlMember.Getter = get_87_ShellHeaderBar_OnShowSettings;
			xamlMember.Setter = set_87_ShellHeaderBar_OnShowSettings;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnToggleMultiSelect":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnToggleMultiSelect", "System.Action");
			xamlMember.Getter = get_88_ShellHeaderBar_OnToggleMultiSelect;
			xamlMember.Setter = set_88_ShellHeaderBar_OnToggleMultiSelect;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnGroupingChange":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnGroupingChange", "System.Action`1<Alpheratz.Shared.Models.GroupingMode>");
			xamlMember.Getter = get_89_ShellHeaderBar_OnGroupingChange;
			xamlMember.Setter = set_89_ShellHeaderBar_OnGroupingChange;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnViewModeChange":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnViewModeChange", "System.Func`2<String, System.Threading.Tasks.Task>");
			xamlMember.Getter = get_90_ShellHeaderBar_OnViewModeChange;
			xamlMember.Setter = set_90_ShellHeaderBar_OnViewModeChange;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellHeaderBar.OnSearchSubmit":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellHeaderBar");
			xamlMember = new XamlMember(this, "OnSearchSubmit", "System.Action");
			xamlMember.Getter = get_91_ShellHeaderBar_OnSearchSubmit;
			xamlMember.Setter = set_91_ShellHeaderBar_OnSearchSubmit;
			break;
		case "Alpheratz.Shared.Controls.ScanningOverlay.OnCancelScan":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.ScanningOverlay");
			xamlMember = new XamlMember(this, "OnCancelScan", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_92_ScanningOverlay_OnCancelScan;
			xamlMember.Setter = set_92_ScanningOverlay_OnCancelScan;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.MainContent":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "MainContent", "Object");
			xamlMember.Getter = get_93_ShellStage_MainContent;
			xamlMember.Setter = set_93_ShellStage_MainContent;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ModalContent":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ModalContent", "Object");
			xamlMember.Getter = get_94_ShellStage_ModalContent;
			xamlMember.Setter = set_94_ShellStage_ModalContent;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ModalVisibility":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ModalVisibility", "Microsoft.UI.Xaml.Visibility");
			xamlMember.Getter = get_95_ShellStage_ModalVisibility;
			xamlMember.Setter = set_95_ShellStage_ModalVisibility;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.TopModalContent":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "TopModalContent", "Object");
			xamlMember.Getter = get_96_ShellStage_TopModalContent;
			xamlMember.Setter = set_96_ShellStage_TopModalContent;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.TopModalVisibility":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "TopModalVisibility", "Microsoft.UI.Xaml.Visibility");
			xamlMember.Getter = get_97_ShellStage_TopModalVisibility;
			xamlMember.Setter = set_97_ShellStage_TopModalVisibility;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ScanningOverlayVisibility":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ScanningOverlayVisibility", "Microsoft.UI.Xaml.Visibility");
			xamlMember.Getter = get_98_ShellStage_ScanningOverlayVisibility;
			xamlMember.Setter = set_98_ShellStage_ScanningOverlayVisibility;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ScanningOverlayDataContext":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ScanningOverlayDataContext", "Object");
			xamlMember.Getter = get_99_ShellStage_ScanningOverlayDataContext;
			xamlMember.Setter = set_99_ShellStage_ScanningOverlayDataContext;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ToastDataContext":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ToastDataContext", "Object");
			xamlMember.Getter = get_100_ShellStage_ToastDataContext;
			xamlMember.Setter = set_100_ShellStage_ToastDataContext;
			break;
		case "Alpheratz.Features.Shell.Controls.ShellStage.ScanningOverlayControlRef":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.Shell.Controls.ShellStage");
			xamlMember = new XamlMember(this, "ScanningOverlayControlRef", "Alpheratz.Shared.Controls.ScanningOverlay");
			xamlMember.Getter = get_101_ShellStage_ScanningOverlayControlRef;
			xamlMember.SetIsReadOnly();
			break;
		case "Alpheratz.Shared.Converters.ThumbnailSourceConverter.DecodePixelWidth":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Converters.ThumbnailSourceConverter");
			xamlMember = new XamlMember(this, "DecodePixelWidth", "Int32");
			xamlMember.Getter = get_102_ThumbnailSourceConverter_DecodePixelWidth;
			xamlMember.Setter = set_102_ThumbnailSourceConverter_DecodePixelWidth;
			break;
		case "Alpheratz.Features.WorldResolve.WorldResolvePage.OnClose":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.WorldResolve.WorldResolvePage");
			xamlMember = new XamlMember(this, "OnClose", "System.Action");
			xamlMember.Getter = get_103_WorldResolvePage_OnClose;
			xamlMember.Setter = set_103_WorldResolvePage_OnClose;
			break;
		case "Alpheratz.Features.WorldResolve.WorldResolvePage.OnApplied":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Features.WorldResolve.WorldResolvePage");
			xamlMember = new XamlMember(this, "OnApplied", "System.Func`1<System.Threading.Tasks.Task>");
			xamlMember.Getter = get_104_WorldResolvePage_OnApplied;
			xamlMember.Setter = set_104_WorldResolvePage_OnApplied;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.GradientStops":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "GradientStops", "Windows.Foundation.Collections.IObservableVector`1<Microsoft.UI.Xaml.Media.GradientStop>");
			xamlMember.Getter = get_105_RadialGradientBrush_GradientStops;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.Center":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "Center", "Windows.Foundation.Point");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_106_RadialGradientBrush_Center;
			xamlMember.Setter = set_106_RadialGradientBrush_Center;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.GradientOrigin":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "GradientOrigin", "Windows.Foundation.Point");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_107_RadialGradientBrush_GradientOrigin;
			xamlMember.Setter = set_107_RadialGradientBrush_GradientOrigin;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.InterpolationSpace":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "InterpolationSpace", "Microsoft.UI.Composition.CompositionColorSpace");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_108_RadialGradientBrush_InterpolationSpace;
			xamlMember.Setter = set_108_RadialGradientBrush_InterpolationSpace;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.MappingMode":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "MappingMode", "Microsoft.UI.Xaml.Media.BrushMappingMode");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_109_RadialGradientBrush_MappingMode;
			xamlMember.Setter = set_109_RadialGradientBrush_MappingMode;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.RadiusX":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "RadiusX", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_110_RadialGradientBrush_RadiusX;
			xamlMember.Setter = set_110_RadialGradientBrush_RadiusX;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.RadiusY":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "RadiusY", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_111_RadialGradientBrush_RadiusY;
			xamlMember.Setter = set_111_RadialGradientBrush_RadiusY;
			break;
		case "Microsoft.UI.Xaml.Media.RadialGradientBrush.SpreadMethod":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Media.RadialGradientBrush");
			xamlMember = new XamlMember(this, "SpreadMethod", "Microsoft.UI.Xaml.Media.GradientSpreadMethod");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_112_RadialGradientBrush_SpreadMethod;
			xamlMember.Setter = set_112_RadialGradientBrush_SpreadMethod;
			break;
		case "Alpheratz.Shared.Controls.CustomScrollbar.ThumbTop":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.CustomScrollbar");
			xamlMember = new XamlMember(this, "ThumbTop", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_113_CustomScrollbar_ThumbTop;
			xamlMember.Setter = set_113_CustomScrollbar_ThumbTop;
			break;
		case "Alpheratz.Shared.Controls.CustomScrollbar.ThumbHeight":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.CustomScrollbar");
			xamlMember = new XamlMember(this, "ThumbHeight", "Double");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_114_CustomScrollbar_ThumbHeight;
			xamlMember.Setter = set_114_CustomScrollbar_ThumbHeight;
			break;
		case "Alpheratz.Shared.Controls.CustomScrollbar.OnTrackClick":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.CustomScrollbar");
			xamlMember = new XamlMember(this, "OnTrackClick", "System.Action`1<Double>");
			xamlMember.Getter = get_115_CustomScrollbar_OnTrackClick;
			xamlMember.Setter = set_115_CustomScrollbar_OnTrackClick;
			break;
		case "Alpheratz.Shared.Controls.CustomScrollbar.OnDrag":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.CustomScrollbar");
			xamlMember = new XamlMember(this, "OnDrag", "System.Action`1<Double>");
			xamlMember.Getter = get_116_CustomScrollbar_OnDrag;
			xamlMember.Setter = set_116_CustomScrollbar_OnDrag;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnPhotoActivated":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnPhotoActivated", "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>");
			xamlMember.Getter = get_117_PhotoGridItemsView_OnPhotoActivated;
			xamlMember.Setter = set_117_PhotoGridItemsView_OnPhotoActivated;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnFavoriteClicked":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnFavoriteClicked", "System.Action`1<Alpheratz.Features.Gallery.PhotoGridItem>");
			xamlMember.Getter = get_118_PhotoGridItemsView_OnFavoriteClicked;
			xamlMember.Setter = set_118_PhotoGridItemsView_OnFavoriteClicked;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnGridScroll":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnGridScroll", "System.Action`1<Double>");
			xamlMember.Getter = get_119_PhotoGridItemsView_OnGridScroll;
			xamlMember.Setter = set_119_PhotoGridItemsView_OnGridScroll;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnGridWheel":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnGridWheel", "System.Action`1<Int32>");
			xamlMember.Getter = get_120_PhotoGridItemsView_OnGridWheel;
			xamlMember.Setter = set_120_PhotoGridItemsView_OnGridWheel;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnNearBottomReached":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnNearBottomReached", "System.Action");
			xamlMember.Getter = get_121_PhotoGridItemsView_OnNearBottomReached;
			xamlMember.Setter = set_121_PhotoGridItemsView_OnNearBottomReached;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.OnFirstVisibleIndexChanged":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "OnFirstVisibleIndexChanged", "System.Action`1<Int32>");
			xamlMember.Getter = get_122_PhotoGridItemsView_OnFirstVisibleIndexChanged;
			xamlMember.Setter = set_122_PhotoGridItemsView_OnFirstVisibleIndexChanged;
			break;
		case "Alpheratz.Shared.Controls.PhotoGridItemsView.GridScrollViewerRef":
			_ = (XamlUserType)GetXamlTypeByName("Alpheratz.Shared.Controls.PhotoGridItemsView");
			xamlMember = new XamlMember(this, "GridScrollViewerRef", "Microsoft.UI.Xaml.Controls.ScrollViewer");
			xamlMember.Getter = get_123_PhotoGridItemsView_GridScrollViewerRef;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressBar.IsIndeterminate":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressBar");
			xamlMember = new XamlMember(this, "IsIndeterminate", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_124_ProgressBar_IsIndeterminate;
			xamlMember.Setter = set_124_ProgressBar_IsIndeterminate;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressBar.ShowError":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressBar");
			xamlMember = new XamlMember(this, "ShowError", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_125_ProgressBar_ShowError;
			xamlMember.Setter = set_125_ProgressBar_ShowError;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressBar.ShowPaused":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressBar");
			xamlMember = new XamlMember(this, "ShowPaused", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_126_ProgressBar_ShowPaused;
			xamlMember.Setter = set_126_ProgressBar_ShowPaused;
			break;
		case "Microsoft.UI.Xaml.Controls.ProgressBar.TemplateSettings":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.ProgressBar");
			xamlMember = new XamlMember(this, "TemplateSettings", "Microsoft.UI.Xaml.Controls.ProgressBarTemplateSettings");
			xamlMember.Getter = get_127_ProgressBar_TemplateSettings;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Thickness.Left":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Thickness");
			xamlMember = new XamlMember(this, "Left", "Double");
			xamlMember.Getter = get_128_Thickness_Left;
			xamlMember.Setter = set_128_Thickness_Left;
			break;
		case "Microsoft.UI.Xaml.Thickness.Top":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Thickness");
			xamlMember = new XamlMember(this, "Top", "Double");
			xamlMember.Getter = get_129_Thickness_Top;
			xamlMember.Setter = set_129_Thickness_Top;
			break;
		case "Microsoft.UI.Xaml.Thickness.Right":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Thickness");
			xamlMember = new XamlMember(this, "Right", "Double");
			xamlMember.Getter = get_130_Thickness_Right;
			xamlMember.Setter = set_130_Thickness_Right;
			break;
		case "Microsoft.UI.Xaml.Thickness.Bottom":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Thickness");
			xamlMember = new XamlMember(this, "Bottom", "Double");
			xamlMember.Getter = get_131_Thickness_Bottom;
			xamlMember.Setter = set_131_Thickness_Bottom;
			break;
		case "Microsoft.UI.Xaml.CornerRadius.TopLeft":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.CornerRadius");
			xamlMember = new XamlMember(this, "TopLeft", "Double");
			xamlMember.Getter = get_132_CornerRadius_TopLeft;
			xamlMember.Setter = set_132_CornerRadius_TopLeft;
			break;
		case "Microsoft.UI.Xaml.CornerRadius.TopRight":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.CornerRadius");
			xamlMember = new XamlMember(this, "TopRight", "Double");
			xamlMember.Getter = get_133_CornerRadius_TopRight;
			xamlMember.Setter = set_133_CornerRadius_TopRight;
			break;
		case "Microsoft.UI.Xaml.CornerRadius.BottomRight":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.CornerRadius");
			xamlMember = new XamlMember(this, "BottomRight", "Double");
			xamlMember.Getter = get_134_CornerRadius_BottomRight;
			xamlMember.Setter = set_134_CornerRadius_BottomRight;
			break;
		case "Microsoft.UI.Xaml.CornerRadius.BottomLeft":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.CornerRadius");
			xamlMember = new XamlMember(this, "BottomLeft", "Double");
			xamlMember.Getter = get_135_CornerRadius_BottomLeft;
			xamlMember.Setter = set_135_CornerRadius_BottomLeft;
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.Children":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "Children", "System.Collections.Generic.IList`1<Microsoft.UI.Xaml.Controls.TreeViewNode>");
			xamlMember.Getter = get_136_TreeViewNode_Children;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.Content":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "Content", "Object");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_137_TreeViewNode_Content;
			xamlMember.Setter = set_137_TreeViewNode_Content;
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.Depth":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "Depth", "Int32");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_138_TreeViewNode_Depth;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.HasChildren":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "HasChildren", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_139_TreeViewNode_HasChildren;
			xamlMember.SetIsReadOnly();
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.HasUnrealizedChildren":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "HasUnrealizedChildren", "Boolean");
			xamlMember.Getter = get_140_TreeViewNode_HasUnrealizedChildren;
			xamlMember.Setter = set_140_TreeViewNode_HasUnrealizedChildren;
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.IsExpanded":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "IsExpanded", "Boolean");
			xamlMember.SetIsDependencyProperty();
			xamlMember.Getter = get_141_TreeViewNode_IsExpanded;
			xamlMember.Setter = set_141_TreeViewNode_IsExpanded;
			break;
		case "Microsoft.UI.Xaml.Controls.TreeViewNode.Parent":
			_ = (XamlUserType)GetXamlTypeByName("Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember = new XamlMember(this, "Parent", "Microsoft.UI.Xaml.Controls.TreeViewNode");
			xamlMember.Getter = get_142_TreeViewNode_Parent;
			xamlMember.SetIsReadOnly();
			break;
		}
		return xamlMember;
	}
}
