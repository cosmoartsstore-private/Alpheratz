using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ABI.System;
using ABI.System.Collections;
using ABI.System.Collections.Generic;
using ABI.System.Collections.Specialized;
using ABI.System.ComponentModel;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WinRT.Alpheratz_FrontendGenericHelpers;

internal static class GlobalVtableLookup
{
	[ModuleInitializer]
	internal static void InitializeGlobalVtableLookup()
	{
		ComWrappersSupport.RegisterTypeComInterfaceEntriesLookup(LookupVtableEntries);
		ComWrappersSupport.RegisterTypeRuntimeClassNameLookup(LookupRuntimeClassName);
	}

	private static ComWrappers.ComInterfaceEntry[] LookupVtableEntries(System.Type type)
	{
		switch (type.ToString())
		{
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemProperties2]":
			_ = IEnumerator_Windows_Storage_IStorageItemProperties2.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItemProperties.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties2>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.String[]":
			_ = IList_string.Initialized;
			_ = IReadOnlyList_string.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IReadOnlyList_System_Collections_IEnumerable.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_string.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[13]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods<string>.IID,
					Vtable = IListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<string>.IID,
					Vtable = IReadOnlyListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<char>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<object>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<string>.IID,
					Vtable = IEnumerableMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<char>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<object>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable>.IID,
					Vtable = IEnumerableMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
		case "System.Collections.Generic.List`1[Alpheratz.Core.Database.ArchiveWorldVisitData]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.Int64 slot, System.String filename, System.String path)`3[System.Int64,System.String,System.String]]":
		case "System.Collections.Generic.List`1[(System.String PhotoPath, System.Int64 SourceSlot)`2[System.String,System.Int64]]":
		case "System.Collections.Generic.List`1[Alpheratz.Models.PhotoRecordDto]":
		case "System.Collections.Generic.List`1[(System.Int64 slot, System.String filename, System.String path)`3[System.Int64,System.String,System.String]]":
		case "System.Collections.Generic.List`1[Alpheratz.Models.SelectedPhotoRefDto]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Core.Database.ArchiveWorldVisitData]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.String path, System.Int64 slot)`2[System.String,System.Int64]]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.Gallery.GalleryMonthGroup]":
		case "System.Collections.Generic.List`1[(System.String path, System.Int64 slot)`2[System.String,System.Int64]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Models.PhotoRecordDto]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.String PhotoPath, System.Int64 SourceSlot)`2[System.String,System.Int64]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.GalleryMonthGroup]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Models.SelectedPhotoRefDto]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[4]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItem]":
			_ = IEnumerator_Windows_Storage_IStorageItem.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem>.IID,
					Vtable = IEnumeratorMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.PhotoGridItem]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
			_ = IReadOnlyList_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[6]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<INotifyPropertyChanged>.IID,
					Vtable = IReadOnlyListMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<INotifyPropertyChanged>.IID,
					Vtable = IEnumerableMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Object]":
			_ = IEnumerator_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<object>.IID,
					Vtable = IEnumeratorMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[System.String]":
		case "System.Collections.Generic.List`1[System.String]":
			_ = IList_string.Initialized;
			_ = IReadOnlyList_string.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IReadOnlyList_System_Collections_IEnumerable.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_string.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[13]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods<string>.IID,
					Vtable = IListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<string>.IID,
					Vtable = IReadOnlyListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<char>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<object>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<string>.IID,
					Vtable = IEnumerableMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<char>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<object>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable>.IID,
					Vtable = IEnumerableMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItem2]":
			_ = IEnumerator_Windows_Storage_IStorageItem2.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItem.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem2>.IID,
					Vtable = IEnumeratorMethods<IStorageItem2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem>.IID,
					Vtable = IEnumeratorMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Threading.Tasks.Task":
		case "System.Threading.Tasks.Task`1[System.Collections.Generic.IReadOnlyList`1[(System.String, System.String)`2[System.String,System.String]]]":
			return new ComWrappers.ComInterfaceEntry[1]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
			_ = IReadOnlyList_string.Initialized;
			_ = IEnumerator_System_Collections_Generic_IReadOnlyList_string_.Initialized;
			_ = IEnumerable_string.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_string_.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_char__.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_object__.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_IEnumerable_.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[8]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IReadOnlyList<string>>.IID,
					Vtable = IEnumeratorMethods<IReadOnlyList<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<string>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable<char>>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable<char>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable<object>>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable<object>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<object>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "Microsoft.Extensions.DependencyInjection.ServiceProvider":
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IServiceProviderMethods.IID,
					Vtable = IServiceProviderMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.StorageFile]":
			_ = IEnumerator_Windows_Storage_StorageFile.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageFile.Initialized;
			_ = IEnumerator_Windows_Storage_Streams_IRandomAccessStreamReference.Initialized;
			_ = IEnumerator_Windows_Storage_Streams_IInputStreamReference.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItemProperties2.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItem2.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItem.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItemPropertiesWithProvider.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItemProperties.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageFilePropertiesWithAvailability.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageFile2.Initialized;
			_ = IEnumerator_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[13]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<StorageFile>.IID,
					Vtable = IEnumeratorMethods<StorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFile>.IID,
					Vtable = IEnumeratorMethods<IStorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IRandomAccessStreamReference>.IID,
					Vtable = IEnumeratorMethods<IRandomAccessStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IInputStreamReference>.IID,
					Vtable = IEnumeratorMethods<IInputStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties2>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem2>.IID,
					Vtable = IEnumeratorMethods<IStorageItem2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem>.IID,
					Vtable = IEnumeratorMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemPropertiesWithProvider>.IID,
					Vtable = IEnumeratorMethods<IStorageItemPropertiesWithProvider>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFilePropertiesWithAvailability>.IID,
					Vtable = IEnumeratorMethods<IStorageFilePropertiesWithAvailability>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFile2>.IID,
					Vtable = IEnumeratorMethods<IStorageFile2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<object>.IID,
					Vtable = IEnumeratorMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Generic.List`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
			_ = IReadOnlyList_string.Initialized;
			_ = IList_System_Collections_Generic_IReadOnlyList_string_.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IReadOnlyList_string_.Initialized;
			_ = IEnumerable_string.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_string_.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_char__.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_object__.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_global__System_Collections_IEnumerable_.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IReadOnlyList_System_Collections_IEnumerable.Initialized;
			_ = IEnumerable_System_Collections_Generic_IReadOnlyList_string_.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_string_.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_char__.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_object__.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_global__System_Collections_IEnumerable_.Initialized;
			return new ComWrappers.ComInterfaceEntry[17]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods<IReadOnlyList<string>>.IID,
					Vtable = IListMethods<IReadOnlyList<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IReadOnlyList<string>>.IID,
					Vtable = IReadOnlyListMethods<IReadOnlyList<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<string>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<IEnumerable<char>>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<IEnumerable<char>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<IEnumerable<object>>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<IEnumerable<object>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<IEnumerable>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<IEnumerable>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<object>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IReadOnlyList<string>>.IID,
					Vtable = IEnumerableMethods<IReadOnlyList<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<string>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<IEnumerable<char>>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<IEnumerable<char>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<IEnumerable<object>>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<IEnumerable<object>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<IEnumerable>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<IEnumerable>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<object>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable>.IID,
					Vtable = IEnumerableMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFile]":
			_ = IEnumerator_Windows_Storage_IStorageFile.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItem.Initialized;
			_ = IEnumerator_Windows_Storage_Streams_IRandomAccessStreamReference.Initialized;
			_ = IEnumerator_Windows_Storage_Streams_IInputStreamReference.Initialized;
			return new ComWrappers.ComInterfaceEntry[5]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFile>.IID,
					Vtable = IEnumeratorMethods<IStorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItem>.IID,
					Vtable = IEnumeratorMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IRandomAccessStreamReference>.IID,
					Vtable = IEnumeratorMethods<IRandomAccessStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IInputStreamReference>.IID,
					Vtable = IEnumeratorMethods<IInputStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Generic.HashSet`1[System.String]":
			_ = IEnumerable_string.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[6]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<string>.IID,
					Vtable = IEnumerableMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<char>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<object>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable>.IID,
					Vtable = IEnumerableMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.Streams.IRandomAccessStreamReference]":
			_ = IEnumerator_Windows_Storage_Streams_IRandomAccessStreamReference.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IRandomAccessStreamReference>.IID,
					Vtable = IEnumeratorMethods<IRandomAccessStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFile2]":
			_ = IEnumerator_Windows_Storage_IStorageFile2.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFile2>.IID,
					Vtable = IEnumeratorMethods<IStorageFile2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[6]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyCollectionChangedMethods.IID,
					Vtable = INotifyCollectionChangedMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyPropertyChangedMethods.IID,
					Vtable = INotifyPropertyChangedMethods.AbiToProjectionVftablePtr
				}
			};
		case "Windows.Storage.StorageFile[]":
			_ = IList_Windows_Storage_StorageFile.Initialized;
			_ = IReadOnlyList_Windows_Storage_StorageFile.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageFile.Initialized;
			_ = IReadOnlyList_Windows_Storage_Streams_IRandomAccessStreamReference.Initialized;
			_ = IReadOnlyList_Windows_Storage_Streams_IInputStreamReference.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageItemProperties2.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageItem2.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageItem.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageItemPropertiesWithProvider.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageItemProperties.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageFilePropertiesWithAvailability.Initialized;
			_ = IReadOnlyList_Windows_Storage_IStorageFile2.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_Windows_Storage_StorageFile.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageFile.Initialized;
			_ = IEnumerable_Windows_Storage_Streams_IRandomAccessStreamReference.Initialized;
			_ = IEnumerable_Windows_Storage_Streams_IInputStreamReference.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageItemProperties2.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageItem2.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageItem.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageItemPropertiesWithProvider.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageItemProperties.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageFilePropertiesWithAvailability.Initialized;
			_ = IEnumerable_Windows_Storage_IStorageFile2.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[27]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods<StorageFile>.IID,
					Vtable = IListMethods<StorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<StorageFile>.IID,
					Vtable = IReadOnlyListMethods<StorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageFile>.IID,
					Vtable = IReadOnlyListMethods<IStorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IRandomAccessStreamReference>.IID,
					Vtable = IReadOnlyListMethods<IRandomAccessStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IInputStreamReference>.IID,
					Vtable = IReadOnlyListMethods<IInputStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageItemProperties2>.IID,
					Vtable = IReadOnlyListMethods<IStorageItemProperties2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageItem2>.IID,
					Vtable = IReadOnlyListMethods<IStorageItem2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageItem>.IID,
					Vtable = IReadOnlyListMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageItemPropertiesWithProvider>.IID,
					Vtable = IReadOnlyListMethods<IStorageItemPropertiesWithProvider>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageItemProperties>.IID,
					Vtable = IReadOnlyListMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageFilePropertiesWithAvailability>.IID,
					Vtable = IReadOnlyListMethods<IStorageFilePropertiesWithAvailability>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IStorageFile2>.IID,
					Vtable = IReadOnlyListMethods<IStorageFile2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<StorageFile>.IID,
					Vtable = IEnumerableMethods<StorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageFile>.IID,
					Vtable = IEnumerableMethods<IStorageFile>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IRandomAccessStreamReference>.IID,
					Vtable = IEnumerableMethods<IRandomAccessStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IInputStreamReference>.IID,
					Vtable = IEnumerableMethods<IInputStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageItemProperties2>.IID,
					Vtable = IEnumerableMethods<IStorageItemProperties2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageItem2>.IID,
					Vtable = IEnumerableMethods<IStorageItem2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageItem>.IID,
					Vtable = IEnumerableMethods<IStorageItem>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageItemPropertiesWithProvider>.IID,
					Vtable = IEnumerableMethods<IStorageItemPropertiesWithProvider>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageItemProperties>.IID,
					Vtable = IEnumerableMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageFilePropertiesWithAvailability>.IID,
					Vtable = IEnumerableMethods<IStorageFilePropertiesWithAvailability>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IStorageFile2>.IID,
					Vtable = IEnumerableMethods<IStorageFile2>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ConstantSplittableMap`2[System.String,System.Int64]":
			_ = IReadOnlyDictionary_string_long.Initialized;
			_ = KeyValuePair_string_long.Initialized;
			_ = IEnumerable_System_Collections_Generic_KeyValuePair_string__long_.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[4]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyDictionaryMethods<string, long>.IID,
					Vtable = IReadOnlyDictionaryMethods<string, long>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<System.Collections.Generic.KeyValuePair<string, long>>.IID,
					Vtable = IEnumerableMethods<System.Collections.Generic.KeyValuePair<string, long>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "Alpheratz.Features.Gallery.PhotoThumbnailItem[]":
		case "Alpheratz.Features.Gallery.PhotoGridItem[]":
			_ = IReadOnlyList_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[6]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<INotifyPropertyChanged>.IID,
					Vtable = IReadOnlyListMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<INotifyPropertyChanged>.IID,
					Vtable = IEnumerableMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Generic.Dictionary`2[System.String,System.Int64]":
		case "System.Collections.ObjectModel.ReadOnlyDictionary`2[System.String,System.Int64]":
			_ = IDictionary_string_long.Initialized;
			_ = IReadOnlyDictionary_string_long.Initialized;
			_ = KeyValuePair_string_long.Initialized;
			_ = IEnumerable_System_Collections_Generic_KeyValuePair_string__long_.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[5]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDictionaryMethods<string, long>.IID,
					Vtable = IDictionaryMethods<string, long>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyDictionaryMethods<string, long>.IID,
					Vtable = IReadOnlyDictionaryMethods<string, long>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<System.Collections.Generic.KeyValuePair<string, long>>.IID,
					Vtable = IEnumerableMethods<System.Collections.Generic.KeyValuePair<string, long>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemProperties]":
			_ = IEnumerator_Windows_Storage_IStorageItemProperties.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemPropertiesWithProvider]":
			_ = IEnumerator_Windows_Storage_IStorageItemPropertiesWithProvider.Initialized;
			_ = IEnumerator_Windows_Storage_IStorageItemProperties.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemPropertiesWithProvider>.IID,
					Vtable = IEnumeratorMethods<IStorageItemPropertiesWithProvider>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageItemProperties>.IID,
					Vtable = IEnumeratorMethods<IStorageItemProperties>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.IEnumerable]":
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Object]]]":
			_ = IEnumerable_object.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_object__.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable<object>>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable<object>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Generic.Dictionary`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "ABI.System.Collections.Generic.ConstantSplittableMap`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.ObjectModel.ReadOnlyDictionary`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.KeyValuePair`2[System.String,System.Int64]]":
			_ = KeyValuePair_string_long.Initialized;
			_ = IEnumerator_System_Collections_Generic_KeyValuePair_string__long_.Initialized;
			_ = IEnumerator_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<System.Collections.Generic.KeyValuePair<string, long>>.IID,
					Vtable = IEnumeratorMethods<System.Collections.Generic.KeyValuePair<string, long>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<object>.IID,
					Vtable = IEnumeratorMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Specialized.ReadOnlyList":
		case "System.Collections.Specialized.SingleItemReadOnlyList":
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Char]]]":
			_ = IEnumerable_char.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_Generic_IEnumerable_char__.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable<char>>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable<char>>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.Gallery.PhotoGridItem]":
			_ = IReadOnlyList_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_System_ComponentModel_INotifyPropertyChanged.Initialized;
			_ = IEnumerable_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[8]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<INotifyPropertyChanged>.IID,
					Vtable = IReadOnlyListMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<INotifyPropertyChanged>.IID,
					Vtable = IEnumerableMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyCollectionChangedMethods.IID,
					Vtable = INotifyCollectionChangedMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyPropertyChangedMethods.IID,
					Vtable = INotifyPropertyChangedMethods.AbiToProjectionVftablePtr
				}
			};
		case "Alpheratz.Core.UiObservableCollection`1[System.String]":
			_ = IList_string.Initialized;
			_ = IReadOnlyList_string.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IReadOnlyList_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IReadOnlyList_System_Collections_IEnumerable.Initialized;
			_ = IReadOnlyList_object.Initialized;
			_ = IEnumerable_string.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[15]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods<string>.IID,
					Vtable = IListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<string>.IID,
					Vtable = IReadOnlyListMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<char>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable<object>>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<IEnumerable>.IID,
					Vtable = IReadOnlyListMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IReadOnlyListMethods<object>.IID,
					Vtable = IReadOnlyListMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<string>.IID,
					Vtable = IEnumerableMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<char>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable<object>>.IID,
					Vtable = IEnumerableMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<IEnumerable>.IID,
					Vtable = IEnumerableMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods<object>.IID,
					Vtable = IEnumerableMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IListMethods.IID,
					Vtable = IListMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumerableMethods.IID,
					Vtable = IEnumerableMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyCollectionChangedMethods.IID,
					Vtable = INotifyCollectionChangedMethods.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = INotifyPropertyChangedMethods.IID,
					Vtable = INotifyPropertyChangedMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.String]]":
			_ = IEnumerable_string.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_string_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<string>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<string>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "System.Collections.Generic.KeyValuePair`2[System.String,System.Int64]":
			_ = KeyValuePair_string_long.Initialized;
			return new ComWrappers.ComInterfaceEntry[1]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = KeyValuePairMethods<string, long>.IID,
					Vtable = KeyValuePairMethods<string, long>.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.IEnumerable]]":
			_ = IEnumerable_System_Collections_IEnumerable.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_global__System_Collections_IEnumerable_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<IEnumerable>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<IEnumerable>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Char]]":
			_ = IEnumerable_char.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<char>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFilePropertiesWithAvailability]":
			_ = IEnumerator_Windows_Storage_IStorageFilePropertiesWithAvailability.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IStorageFilePropertiesWithAvailability>.IID,
					Vtable = IEnumeratorMethods<IStorageFilePropertiesWithAvailability>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Object]]":
			_ = IEnumerable_object.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			return new ComWrappers.ComInterfaceEntry[3]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<object>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.ComponentModel.INotifyPropertyChanged]":
			_ = IEnumerator_System_ComponentModel_INotifyPropertyChanged.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<INotifyPropertyChanged>.IID,
					Vtable = IEnumeratorMethods<INotifyPropertyChanged>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.String]":
			_ = IEnumerator_string.Initialized;
			_ = IEnumerable_char.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_char_.Initialized;
			_ = IEnumerable_object.Initialized;
			_ = IEnumerator_System_Collections_Generic_IEnumerable_object_.Initialized;
			_ = IEnumerator_System_Collections_IEnumerable.Initialized;
			_ = IEnumerator_object.Initialized;
			return new ComWrappers.ComInterfaceEntry[6]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<string>.IID,
					Vtable = IEnumeratorMethods<string>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<char>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<char>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable<object>>.IID,
					Vtable = IEnumeratorMethods<IEnumerable<object>>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IEnumerable>.IID,
					Vtable = IEnumeratorMethods<IEnumerable>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<object>.IID,
					Vtable = IEnumeratorMethods<object>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.Streams.IInputStreamReference]":
			_ = IEnumerator_Windows_Storage_Streams_IInputStreamReference.Initialized;
			return new ComWrappers.ComInterfaceEntry[2]
			{
				new ComWrappers.ComInterfaceEntry
				{
					IID = IEnumeratorMethods<IInputStreamReference>.IID,
					Vtable = IEnumeratorMethods<IInputStreamReference>.AbiToProjectionVftablePtr
				},
				new ComWrappers.ComInterfaceEntry
				{
					IID = IDisposableMethods.IID,
					Vtable = IDisposableMethods.AbiToProjectionVftablePtr
				}
			};
		default:
			return null;
		}
	}

	private static string LookupRuntimeClassName(System.Type type)
	{
		switch (type.ToString())
		{
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemProperties2]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageItemProperties2>";
		case "System.String[]":
		case "Windows.Storage.StorageFile[]":
		case "Alpheratz.Features.Gallery.PhotoThumbnailItem[]":
		case "System.Collections.Specialized.ReadOnlyList":
		case "Alpheratz.Features.Gallery.PhotoGridItem[]":
		case "System.Collections.Specialized.SingleItemReadOnlyList":
			return "Microsoft.UI.Xaml.Interop.IBindableVector";
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
		case "System.Collections.Generic.List`1[Alpheratz.Core.Database.ArchiveWorldVisitData]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.Int64 slot, System.String filename, System.String path)`3[System.Int64,System.String,System.String]]":
		case "System.Collections.Generic.List`1[(System.String PhotoPath, System.Int64 SourceSlot)`2[System.String,System.Int64]]":
		case "System.Collections.Generic.List`1[Alpheratz.Models.PhotoRecordDto]":
		case "System.Collections.Generic.List`1[(System.Int64 slot, System.String filename, System.String path)`3[System.Int64,System.String,System.String]]":
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
		case "System.Collections.Generic.List`1[Alpheratz.Models.SelectedPhotoRefDto]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Core.Database.ArchiveWorldVisitData]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.String path, System.Int64 slot)`2[System.String,System.Int64]]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.Gallery.GalleryMonthGroup]":
		case "System.Collections.Generic.List`1[(System.String path, System.Int64 slot)`2[System.String,System.Int64]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Models.PhotoRecordDto]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[(System.String PhotoPath, System.Int64 SourceSlot)`2[System.String,System.Int64]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.GalleryMonthGroup]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Models.SelectedPhotoRefDto]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.WorldResolve.CandidateEntry]":
			return "Windows.Foundation.Collections.IVectorView`1<Object>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItem]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageItem>";
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[Alpheratz.Features.Gallery.PhotoGridItem]":
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
		case "Alpheratz.Core.UiObservableCollection`1[Alpheratz.Features.Gallery.PhotoGridItem]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.Generic.List`1[Alpheratz.Features.WorldResolve.WorldResolveItem]":
			return "Windows.Foundation.Collections.IVectorView`1<Microsoft.UI.Xaml.Data.INotifyPropertyChanged>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Object]":
			return "Windows.Foundation.Collections.IIterator`1<Object>";
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[System.String]":
		case "System.Collections.Generic.List`1[System.String]":
		case "Alpheratz.Core.UiObservableCollection`1[System.String]":
			return "Windows.Foundation.Collections.IVector`1<String>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItem2]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageItem2>";
		case "System.Threading.Tasks.Task":
		case "System.Threading.Tasks.Task`1[System.Collections.Generic.IReadOnlyList`1[(System.String, System.String)`2[System.String,System.String]]]":
			return "Windows.Foundation.IClosable";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IVectorView`1<String>>";
		case "Microsoft.Extensions.DependencyInjection.ServiceProvider":
			return "Microsoft.UI.Xaml.IXamlServiceProvider";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.StorageFile]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.StorageFile>";
		case "System.Collections.Generic.List`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
		case "System.Collections.ObjectModel.ReadOnlyCollection`1[System.Collections.Generic.IReadOnlyList`1[System.String]]":
			return "Windows.Foundation.Collections.IVector`1<Windows.Foundation.Collections.IVectorView`1<String>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFile]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageFile>";
		case "System.Collections.Generic.HashSet`1[System.String]":
			return "Windows.Foundation.Collections.IIterable`1<String>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.Streams.IRandomAccessStreamReference]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.Streams.IRandomAccessStreamReference>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFile2]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageFile2>";
		case "ABI.System.Collections.Generic.ConstantSplittableMap`2[System.String,System.Int64]":
			return "Windows.Foundation.Collections.IMapView`2<String, Int64>";
		case "System.Collections.Generic.Dictionary`2[System.String,System.Int64]":
		case "System.Collections.ObjectModel.ReadOnlyDictionary`2[System.String,System.Int64]":
			return "Windows.Foundation.Collections.IMap`2<String, Int64>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemProperties]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageItemProperties>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageItemPropertiesWithProvider]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageItemPropertiesWithProvider>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.IEnumerable]":
			return "Windows.Foundation.Collections.IIterator`1<Microsoft.UI.Xaml.Interop.IBindableIterable>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Object]]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<Windows.Foundation.Collections.IIterable`1<Object>>>";
		case "System.Collections.Generic.Dictionary`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "ABI.System.Collections.Generic.ConstantSplittableMap`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
		case "System.Collections.ObjectModel.ReadOnlyDictionary`2[System.String,Alpheratz.Features.Gallery.PhotoThumbnailItem]":
			return "Windows.Foundation.Collections.IIterable`1<Object>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.KeyValuePair`2[System.String,System.Int64]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IKeyValuePair`2<String, Int64>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.Generic.IEnumerable`1[System.Char]]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<Windows.Foundation.Collections.IIterable`1<Char>>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.String]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<String>>";
		case "System.Collections.Generic.KeyValuePair`2[System.String,System.Int64]":
			return "Windows.Foundation.Collections.IKeyValuePair`2<String, Int64>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Collections.IEnumerable]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<Microsoft.UI.Xaml.Interop.IBindableIterable>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Char]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<Char>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.IStorageFilePropertiesWithAvailability]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.IStorageFilePropertiesWithAvailability>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.Collections.Generic.IEnumerable`1[System.Object]]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Foundation.Collections.IIterable`1<Object>>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.ComponentModel.INotifyPropertyChanged]":
			return "Windows.Foundation.Collections.IIterator`1<Microsoft.UI.Xaml.Data.INotifyPropertyChanged>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[System.String]":
			return "Windows.Foundation.Collections.IIterator`1<String>";
		case "ABI.System.Collections.Generic.ToAbiEnumeratorAdapter`1[Windows.Storage.Streams.IInputStreamReference]":
			return "Windows.Foundation.Collections.IIterator`1<Windows.Storage.Streams.IInputStreamReference>";
		default:
			return null;
		}
	}
}
