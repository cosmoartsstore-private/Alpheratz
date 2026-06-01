using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ABI.System.Collections.Generic;
using Windows.Storage;

namespace WinRT.Alpheratz_FrontendGenericHelpers;

internal static class IReadOnlyList_Windows_Storage_IStorageFile2
{
	private static readonly bool _initialized = Init();

	internal static bool Initialized => _initialized;

	private unsafe static bool Init()
	{
		return IReadOnlyListMethods<IStorageFile2, nint>.InitCcw((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(&Do_Abi_GetAt_0), (delegate* unmanaged[Stdcall]<nint, uint*, int>)(&Do_Abi_get_Size_1), (delegate* unmanaged[Stdcall]<nint, nint, uint*, byte*, int>)(&Do_Abi_IndexOf_2), (delegate* unmanaged[Stdcall]<nint, uint, int, nint, uint*, int>)(&Do_Abi_GetMany_3));
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_GetAt_0(nint thisPtr, uint index, nint* __return_value__)
	{
		IStorageFile2 storageFile = null;
		*__return_value__ = 0;
		try
		{
			storageFile = IReadOnlyListMethods<IStorageFile2>.Abi_GetAt_0(thisPtr, index);
			*__return_value__ = MarshalInterface<IStorageFile2>.FromManaged(storageFile);
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
			flag = IReadOnlyListMethods<IStorageFile2>.Abi_IndexOf_2(thisPtr, MarshalInterface<IStorageFile2>.FromAbi(value), out index2);
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
		IStorageFile2[] items2 = MarshalInterface<IStorageFile2>.FromAbiArray((__itemsSize, items));
		try
		{
			num = IReadOnlyListMethods<IStorageFile2>.Abi_GetMany_3(thisPtr, startIndex, ref items2);
			MarshalInterface<IStorageFile2>.CopyManagedArray(items2, items);
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
			num = IReadOnlyListMethods<IStorageFile2>.Abi_get_Size_1(thisPtr);
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
