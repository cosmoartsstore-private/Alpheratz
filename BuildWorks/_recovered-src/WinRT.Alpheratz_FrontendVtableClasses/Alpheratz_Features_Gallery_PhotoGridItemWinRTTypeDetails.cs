using System.Runtime.InteropServices;
using ABI.System.ComponentModel;

namespace WinRT.Alpheratz_FrontendVtableClasses;

internal sealed class Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails : IWinRTExposedTypeDetails
{
	public ComWrappers.ComInterfaceEntry[] GetExposedInterfaces()
	{
		return new ComWrappers.ComInterfaceEntry[1]
		{
			new ComWrappers.ComInterfaceEntry
			{
				IID = INotifyPropertyChangedMethods.IID,
				Vtable = INotifyPropertyChangedMethods.AbiToProjectionVftablePtr
			}
		};
	}
}
