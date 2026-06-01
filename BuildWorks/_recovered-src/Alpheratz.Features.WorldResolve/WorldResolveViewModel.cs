using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging.Pdq;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.WorldResolve;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class WorldResolveViewModel : UiThreadSafeObservableObject
{
	private readonly AlpheratzDb db;

	private readonly ThumbnailWorker thumbnailWorker;

	private readonly ToastService toastService;

	private bool isLoading;

	private string loadingText = "候補を検索中...";

	private bool isApplying;

	private int applyCount;

	private bool isCandidatePickerOpen;

	private WorldResolveItem? activePickerItem;

	private bool isCandidateLoading;

	private Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>> knownBySlot = new Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>>();

	public UiObservableCollection<WorldResolveItem> Items { get; } = new UiObservableCollection<WorldResolveItem>();

	public bool IsLoading
	{
		get
		{
			return isLoading;
		}
		set
		{
			SetProperty(ref isLoading, value, "IsLoading");
		}
	}

	public string LoadingText
	{
		get
		{
			return loadingText;
		}
		set
		{
			SetProperty(ref loadingText, value, "LoadingText");
		}
	}

	public bool IsApplying
	{
		get
		{
			return isApplying;
		}
		set
		{
			SetProperty(ref isApplying, value, "IsApplying");
		}
	}

	public int ApplyCount
	{
		get
		{
			return applyCount;
		}
		set
		{
			SetProperty(ref applyCount, value, "ApplyCount");
		}
	}

	public bool IsCandidatePickerOpen
	{
		get
		{
			return isCandidatePickerOpen;
		}
		set
		{
			SetProperty(ref isCandidatePickerOpen, value, "IsCandidatePickerOpen");
		}
	}

	public WorldResolveItem? ActivePickerItem
	{
		get
		{
			return activePickerItem;
		}
		set
		{
			SetProperty(ref activePickerItem, value, "ActivePickerItem");
		}
	}

	public UiObservableCollection<CandidateEntry> CandidateList { get; } = new UiObservableCollection<CandidateEntry>();

	public bool IsCandidateLoading
	{
		get
		{
			return isCandidateLoading;
		}
		set
		{
			SetProperty(ref isCandidateLoading, value, "IsCandidateLoading");
		}
	}

	public WorldResolveViewModel(AlpheratzDb db, ThumbnailWorker thumbnailWorker, ToastService toastService)
	{
		this.db = db;
		this.thumbnailWorker = thumbnailWorker;
		this.toastService = toastService;
	}

	public async Task InitializeAsync(CancellationToken ct = default(CancellationToken))
	{
		IsLoading = true;
		LoadingText = "候補を検索中...";
		try
		{
			IReadOnlyList<AlpheratzDb.UnknownPhashRow> unknowns = await db.GetUnknownWorldPhotosWithPhashAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (unknowns.Count == 0)
			{
				IsLoading = false;
				return;
			}
			knownBySlot = new Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>>();
			foreach (AlpheratzDb.UnknownPhashRow item in unknowns)
			{
				ct.ThrowIfCancellationRequested();
				if (!knownBySlot.ContainsKey(item.SourceSlot))
				{
					Dictionary<long, IReadOnlyList<AlpheratzDb.KnownWorldRow>> dictionary = knownBySlot;
					long sourceSlot = item.SourceSlot;
					dictionary[sourceSlot] = await db.GetKnownWorldPhotosAsync(item.SourceSlot, null, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			int total = unknowns.Count;
			List<WorldResolveItem> items = await Task.Run(delegate
			{
				Dictionary<long, List<IReadOnlyList<string>>> dictionary2 = new Dictionary<long, List<IReadOnlyList<string>>>();
				List<WorldResolveItem> list = new List<WorldResolveItem>(total);
				int num = 0;
				foreach (AlpheratzDb.UnknownPhashRow item2 in unknowns)
				{
					ct.ThrowIfCancellationRequested();
					IReadOnlyList<AlpheratzDb.KnownWorldRow> readOnlyList = knownBySlot[item2.SourceSlot];
					if (!dictionary2.TryGetValue(item2.SourceSlot, out var value))
					{
						value = new List<IReadOnlyList<string>>(readOnlyList.Count);
						foreach (AlpheratzDb.KnownWorldRow item3 in readOnlyList)
						{
							value.Add(PdqHasher.ParseHashVariants(item3.Phash));
						}
						dictionary2[item2.SourceSlot] = value;
					}
					WorldResolveItem worldResolveItem = new WorldResolveItem(item2.PhotoPath, item2.PhotoFilename, item2.Phash, item2.SourceSlot);
					if (readOnlyList.Count > 0)
					{
						(AlpheratzDb.KnownWorldRow, int)? tuple = WorldService.FindBestMatchWithDetails(item2.Phash, readOnlyList, value);
						if (tuple.HasValue)
						{
							worldResolveItem.MatchPhotoPath = tuple.Value.Item1.PhotoPath;
							worldResolveItem.MatchPhotoFilename = tuple.Value.Item1.PhotoFilename;
							worldResolveItem.MatchWorldName = tuple.Value.Item1.WorldName;
							worldResolveItem.MatchWorldId = tuple.Value.Item1.WorldId;
							worldResolveItem.MatchDistance = tuple.Value.Item2;
						}
					}
					list.Add(worldResolveItem);
					num++;
					LoadingText = $"分析中 {num}/{total}";
				}
				return list;
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			Items.ReplaceAll(items);
			IsLoading = false;
			List<(string path, long slot)> thumbTargets = new List<(string, long)>();
			foreach (WorldResolveItem item4 in items)
			{
				thumbTargets.Add((item4.TargetPhotoPath, item4.TargetSourceSlot));
				if (item4.MatchPhotoPath != null)
				{
					thumbTargets.Add((item4.MatchPhotoPath, item4.TargetSourceSlot));
				}
			}
			Task.Run(async delegate
			{
				try
				{
					await thumbnailWorker.GenerateGridAsync(thumbTargets, delegate(ThumbnailResult result)
					{
						foreach (WorldResolveItem item5 in items)
						{
							if (item5.TargetPhotoPath == result.PhotoPath)
							{
								item5.TargetThumbPath = result.ThumbPath;
							}
							if (item5.MatchPhotoPath == result.PhotoPath)
							{
								item5.MatchThumbPath = result.ThumbPath;
							}
						}
					}, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex4)
				{
					AppLogger.Warn("WorldResolve thumb generation: " + ex4.Message);
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			AppLogger.Error($"WorldResolveViewModel.InitializeAsync: {ex2}");
			toastService.addToast("初期化に失敗しました: " + ex2.Message, ToastType.error);
		}
		finally
		{
			IsLoading = false;
		}
	}

	public void ToggleApply(WorldResolveItem item)
	{
		item.IsApplied = !item.IsApplied;
		RecountApply();
	}

	public void ApplyAll()
	{
		foreach (WorldResolveItem item in Items)
		{
			if (item.HasMatch)
			{
				item.IsApplied = true;
			}
		}
		RecountApply();
	}

	public void SkipAll()
	{
		foreach (WorldResolveItem item in Items)
		{
			item.IsApplied = false;
		}
		RecountApply();
	}

	private void RecountApply()
	{
		ApplyCount = Items.Count((WorldResolveItem x) => x.IsApplied);
	}

	public async Task<int> ApplyConfirmedAsync(CancellationToken ct = default(CancellationToken))
	{
		IsApplying = true;
		int applied = 0;
		try
		{
			foreach (WorldResolveItem item in Items)
			{
				ct.ThrowIfCancellationRequested();
				if (item.IsApplied && item.MatchWorldName != null)
				{
					await db.UpdatePhotoWorldAsync(item.TargetPhotoPath, item.MatchWorldName, item.MatchWorldId, "phash_confirmed", ct).ConfigureAwait(continueOnCapturedContext: false);
					applied++;
				}
			}
			return applied;
		}
		finally
		{
			IsApplying = false;
		}
	}

	public async Task OpenCandidatePickerAsync(WorldResolveItem item, CancellationToken ct = default(CancellationToken))
	{
		ActivePickerItem = item;
		IsCandidateLoading = true;
		IsCandidatePickerOpen = true;
		try
		{
			if (!knownBySlot.TryGetValue(item.TargetSourceSlot, out IReadOnlyList<AlpheratzDb.KnownWorldRow> knownPhotos))
			{
				knownPhotos = await db.GetKnownWorldPhotosAsync(item.TargetSourceSlot, null, ct).ConfigureAwait(continueOnCapturedContext: false);
				knownBySlot[item.TargetSourceSlot] = knownPhotos;
			}
			List<CandidateEntry> list = (await Task.Run(() => WorldService.RankCandidatesByDistance(item.TargetPhash, knownPhotos), ct).ConfigureAwait(continueOnCapturedContext: false)).Select<(AlpheratzDb.KnownWorldRow, int), CandidateEntry>(((AlpheratzDb.KnownWorldRow Row, int Distance) r) => new CandidateEntry(r.Row.PhotoPath, r.Row.PhotoFilename, r.Row.WorldName, r.Row.WorldId, r.Distance, r.Row.SourceSlot)).ToList();
			CandidateList.ReplaceAll(list);
			IsCandidateLoading = false;
			List<(string PhotoPath, long SourceSlot)> thumbTargets = list.Select((CandidateEntry e) => (PhotoPath: e.PhotoPath, SourceSlot: e.SourceSlot)).ToList();
			Task.Run(async delegate
			{
				try
				{
					await thumbnailWorker.GenerateGridAsync(thumbTargets, delegate
					{
					}, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex3)
				{
					AppLogger.Warn("Candidate thumb generation: " + ex3.Message);
				}
			});
		}
		catch (OperationCanceledException)
		{
			CloseCandidatePicker();
		}
		catch (Exception value)
		{
			AppLogger.Error($"WorldResolveViewModel.OpenCandidatePickerAsync: {value}");
			CloseCandidatePicker();
		}
	}

	public void SelectCandidate(CandidateEntry entry)
	{
		WorldResolveItem item = ActivePickerItem;
		if (item == null)
		{
			return;
		}
		item.MatchPhotoPath = entry.PhotoPath;
		item.MatchPhotoFilename = entry.PhotoFilename;
		item.MatchWorldName = entry.WorldName;
		item.MatchWorldId = entry.WorldId;
		item.MatchDistance = entry.Distance;
		item.MatchThumbPath = null;
		Task.Run(async delegate
		{
			try
			{
				await thumbnailWorker.GenerateGridAsync(new _003C_003Ez__ReadOnlySingleElementList<(string, long)>((entry.PhotoPath, entry.SourceSlot)), delegate(ThumbnailResult result)
				{
					item.MatchThumbPath = result.ThumbPath;
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				AppLogger.Warn("SelectCandidate thumb: " + ex.Message);
			}
		});
		CloseCandidatePicker();
	}

	public void CloseCandidatePicker()
	{
		IsCandidatePickerOpen = false;
		ActivePickerItem = null;
		IsCandidateLoading = false;
	}
}
