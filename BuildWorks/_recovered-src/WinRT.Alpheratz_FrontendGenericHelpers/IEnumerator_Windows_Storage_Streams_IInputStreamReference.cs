using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ABI.System.Collections.Generic;
using Windows.Storage.Streams;

namespace WinRT.Alpheratz_FrontendGenericHelpers;

internal static class IEnumerator_Windows_Storage_Streams_IInputStreamReference
{
	private static readonly bool _initialized = Init();

	internal static bool Initialized => _initialized;

	private unsafe static bool Init()
	{
		return IEnumeratorMethods<IInputStreamReference, nint>.InitCcw((delegate* unmanaged[Stdcall]<nint, nint*, int>)(&Do_Abi_get_Current_0), (delegate* unmanaged[Stdcall]<nint, byte*, int>)(&Do_Abi_get_HasCurrent_1), (delegate* unmanaged[Stdcall]<nint, byte*, int>)(&Do_Abi_MoveNext_2), (delegate* unmanaged[Stdcall]<nint, int, nint, uint*, int>)(&Do_Abi_GetMany_3));
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_MoveNext_2(nint thisPtr, byte* __return_value__)
	{
		bool flag = false;
		*__return_value__ = 0;
		try
		{
			flag = IEnumeratorMethods<IInputStreamReference>.Abi_MoveNext_2(thisPtr);
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
	private unsafe static int Do_Abi_GetMany_3(nint thisPtr, int __itemsSize, nint items, uint* __return_value__)
	{
		uint num = 0u;
		*__return_value__ = 0u;
		IInputStreamReference[] items2 = MarshalInterface<IInputStreamReference>.FromAbiArray((__itemsSize, items));
		try
		{
			num = IEnumeratorMethods<IInputStreamReference>.Abi_GetMany_3(thisPtr, ref items2);
			MarshalInterface<IInputStreamReference>.CopyManagedArray(items2, items);
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
	private unsafe static int Do_Abi_get_Current_0(nint thisPtr, nint* __return_value__)
	{
		IInputStreamReference inputStreamReference = null;
		*__return_value__ = 0;
		try
		{
			inputStreamReference = IEnumeratorMethods<IInputStreamReference>.Abi_get_Current_0(thisPtr);
			*__return_value__ = MarshalInterface<IInputStreamReference>.FromManaged(inputStreamReference);
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_get_HasCurrent_1(nint thisPtr, byte* __return_value__)
	{
		bool flag = false;
		*__return_value__ = 0;
		try
		{
			flag = IEnumeratorMethods<IInputStreamReference>.Abi_get_HasCurrent_1(thisPtr);
			*__return_value__ = (flag ? ((byte)1) : ((byte)0));
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}
}
