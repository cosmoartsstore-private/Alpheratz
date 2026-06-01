using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ABI.System.Collections.Generic;
using Windows.Storage;

namespace WinRT.Alpheratz_FrontendGenericHelpers;

internal static class IReadOnlyList_Windows_Storage_IStorageFile
{
	private static readonly bool _initialized = Init();

	internal static bool Initialized => _initialized;

	private unsafe static bool Init()
	{
		return IReadOnlyListMethods<IStorageFile, nint>.InitCcw((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(&Do_Abi_GetAt_0), (delegate* unmanaged[Stdcall]<nint, uint*, int>)(&Do_Abi_get_Size_1), (delegate* unmanaged[Stdcall]<nint, nint, uint*, byte*, int>)(&Do_Abi_IndexOf_2), (delegate* unmanaged[Stdcall]<nint, uint, int, nint, uint*, int>)(&Do_Abi_GetMany_3));
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_GetAt_0(nint thisPtr, uint index, nint* __return_value__)
	{
		IStorageFile storageFile = null;
		*__return_value__ = 0;
		try
		{
			storageFile = IReadOnlyListMethods<IStorageFile>.Abi_GetAt_0(thisPtr, index);
			*__return_value__ = MarshalInterface<IStorageFile>.FromManaged(storageFile);
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_IndexOf_2(nint thisPtr, nint value, uint* index, byte* __return_value__)
	{
		bool flag = false;
		*index = 0u;
		*__return_value__ = 0;
		uint index2 = 0u;
		try
		{
			flag = IReadOnlyListMethods<IStorageFile>.Abi_IndexOf_2(thisPtr, MarshalInterface<IStorageFile>.FromAbi(value), out index2);
			*index = index2;
			*__return_value__ = (flag ? ((byte)1) : ((byte)0));
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_GetMany_3(nint thisPtr, uint startIndex, int __itemsSize, nint items, uint* __return_value__)
	{
		uint num = 0u;
		*__return_value__ = 0u;
		IStorageFile[] items2 = MarshalInterface<IStorageFile>.FromAbiArray((__itemsSize, items));
		try
		{
			num = IReadOnlyListMethods<IStorageFile>.Abi_GetMany_3(thisPtr, startIndex, ref items2);
			MarshalInterface<IStorageFile>.CopyManagedArray(items2, items);
			*__return_value__ = num;
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_get_Size_1(nint thisPtr, uint* __return_value__)
	{
		uint num = 0u;
		*__return_value__ = 0u;
		try
		{
			num = IReadOnlyListMethods<IStorageFile>.Abi_get_Size_1(thisPtr);
			*__return_value__ = num;
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}
}
