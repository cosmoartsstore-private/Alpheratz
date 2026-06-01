using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ABI.System.Collections.Generic;

namespace WinRT.Alpheratz_FrontendGenericHelpers;

internal static class KeyValuePair_string_long
{
	private static readonly bool _initialized = Init();

	internal static bool Initialized => _initialized;

	private unsafe static bool Init()
	{
		return KeyValuePairMethods<string, nint, long, long>.InitCcw((delegate* unmanaged[Stdcall]<nint, nint*, int>)(&Do_Abi_get_Key_0), (delegate* unmanaged[Stdcall]<nint, long*, int>)(&Do_Abi_get_Value_1));
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_get_Key_0(nint thisPtr, nint* __return_value__)
	{
		string text = null;
		*__return_value__ = 0;
		try
		{
			text = KeyValuePairMethods<string, long>.Abi_get_Key_0(thisPtr);
			*__return_value__ = MarshalString.FromManaged(text);
		}
		catch (Exception ex)
		{
			ExceptionHelpers.SetErrorInfo(ex);
			return ExceptionHelpers.GetHRForException(ex);
		}
		return 0;
	}

	[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
	private unsafe static int Do_Abi_get_Value_1(nint thisPtr, long* __return_value__)
	{
		long num = 0L;
		*__return_value__ = 0L;
		try
		{
			num = KeyValuePairMethods<string, long>.Abi_get_Value_1(thisPtr);
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
