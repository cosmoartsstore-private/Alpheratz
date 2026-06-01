using System.Runtime.InteropServices;
using ABI.Microsoft.UI.Xaml;
using ABI.Microsoft.UI.Xaml.Markup;

namespace WinRT.Alpheratz_FrontendVtableClasses;

internal sealed class Alpheratz_Features_WorldResolve_WorldResolvePage_WorldResolvePage_obj9_BindingsWinRTTypeDetails : IWinRTExposedTypeDetails
{
	public ComWrappers.ComInterfaceEntry[] GetExposedInterfaces()
	{
		return new ComWrappers.ComInterfaceEntry[3]
		{
			new ComWrappers.ComInterfaceEntry
			{
				IID = IDataTemplateExtensionMethods.IID,
				Vtable = IDataTemplateExtensionMethods.AbiToProjectionVftablePtr
			},
			new ComWrappers.ComInterfaceEntry
			{
				IID = IDataTemplateComponentMethods.IID,
				Vtable = IDataTemplateComponentMethods.AbiToProjectionVftablePtr
			},
			new ComWrappers.ComInterfaceEntry
			{
				IID = IComponentConnectorMethods.IID,
				Vtable = IComponentConnectorMethods.AbiToProjectionVftablePtr
			}
		};
	}
}
