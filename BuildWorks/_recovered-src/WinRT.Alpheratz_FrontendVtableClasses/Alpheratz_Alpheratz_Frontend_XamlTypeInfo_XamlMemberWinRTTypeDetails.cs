using System.Runtime.InteropServices;
using ABI.Microsoft.UI.Xaml.Markup;

namespace WinRT.Alpheratz_FrontendVtableClasses;

internal sealed class Alpheratz_Alpheratz_Frontend_XamlTypeInfo_XamlMemberWinRTTypeDetails : IWinRTExposedTypeDetails
{
	public ComWrappers.ComInterfaceEntry[] GetExposedInterfaces()
	{
		return new ComWrappers.ComInterfaceEntry[1]
		{
			new ComWrappers.ComInterfaceEntry
			{
				IID = IXamlMemberMethods.IID,
				Vtable = IXamlMemberMethods.AbiToProjectionVftablePtr
			}
		};
	}
}
