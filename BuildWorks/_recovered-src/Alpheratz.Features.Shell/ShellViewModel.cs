using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Features.WorldResolve;
using Alpheratz.Models;
using Alpheratz.Models.Events;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.Shell;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class ShellViewModel : UiThreadSafeObservableObject, IAsyncDisposable
{
	private const bool DETACH_RUNTIME_DATA = false;

	private const bool DETACH_AUXILIARY_RUNTIME_DATA = false;

	private readonly SettingsService settingsService;

	private readonly AlpheratzDb db;

	private readonly PhotoScanner scanner;

	private readonly PhashService phashService;

	private readonly OrientationService orientationService;

	private readonly WorldService worldService;

	private readonly LocalEventBus eventBus;

	public readonly ToastService toastService;

	private readonly PhotoModalState photoModalState;

	private readonly DispatcherService dispatcherService;

	private readonly ThumbnailWorker thumbnailWorker;

	private bool isScanningRef;

	private readonly List<IAsyncDisposable> scanUnlistenFns = new List<IAsyncDisposable>();

	private readonly List<IAsyncDisposable> phashUnlistenFns = new List<IAsyncDisposable>();

	private readonly SemaphoreSlim postScanGate = new SemaphoreSlim(1, 1);

	private const long PhashUiUpdateMinIntervalMs = 1000L;

	private long lastPhashUiUpdateTicks;

	private string scanStatus = "idle";

	private ScanProgressDto scanProgress = new ScanProgressDto
	{
		processed = 0,
		total = 0,
		current_world = "",
		phase = "scan"
	};

	private string photoFolderPath = "";

	private string secondaryPhotoFolderPath = "";

	private PhashProgressEvent pdqProgress = PhashProgressEvent.Empty;

	private bool isPdqRunning;

	private string? pendingFolderPath;

	private int pendingFolderSlot = 1;

	private PendingResetRequest? pendingResetRequest;

	private bool isApplyingFolderChange;

	private bool startupEnabled;

	private ThemeMode themeMode;

	private ViewMode viewMode;

	private string activeTweetTemplate = "";

	public GalleryViewModel galleryViewModel { get; }

	public SettingsViewModel settingsViewModel { get; }

	public TagMasterViewModel tagMasterViewModel { get; }

	public TemplatePageViewModel templatePageViewModel { get; }

	public UiObservableCollection<string> tweetTemplates { get; } = new UiObservableCollection<string>();

	public string ScanStatus
	{
		get
		{
			return scanStatus;
		}
		set
		{
			SetProperty(ref scanStatus, value, "ScanStatus");
		}
	}

	public ScanProgressDto ScanProgress
	{
		get
		{
			return scanProgress;
		}
		set
		{
			SetProperty(ref scanProgress, value, "ScanProgress");
		}
	}

	public string PhotoFolderPath
	{
		get
		{
			return photoFolderPath;
		}
		set
		{
			SetProperty(ref photoFolderPath, value, "PhotoFolderPath");
		}
	}

	public string SecondaryPhotoFolderPath
	{
		get
		{
			return secondaryPhotoFolderPath;
		}
		set
		{
			SetProperty(ref secondaryPhotoFolderPath, value, "SecondaryPhotoFolderPath");
		}
	}

	public PhashProgressEvent PdqProgress
	{
		get
		{
			return pdqProgress;
		}
		set
		{
			SetProperty(ref pdqProgress, value, "PdqProgress");
		}
	}

	public bool IsPdqRunning
	{
		get
		{
			return isPdqRunning;
		}
		set
		{
			SetProperty(ref isPdqRunning, value, "IsPdqRunning");
		}
	}

	public string? PendingFolderPath
	{
		get
		{
			return pendingFolderPath;
		}
		set
		{
			SetProperty(ref pendingFolderPath, value, "PendingFolderPath");
		}
	}

	public int PendingFolderSlot
	{
		get
		{
			return pendingFolderSlot;
		}
		set
		{
			SetProperty(ref pendingFolderSlot, value, "PendingFolderSlot");
		}
	}

	public PendingResetRequest? PendingResetRequest
	{
		get
		{
			return pendingResetRequest;
		}
		set
		{
			SetProperty(ref pendingResetRequest, value, "PendingResetRequest");
		}
	}

	public bool IsApplyingFolderChange
	{
		get
		{
			return isApplyingFolderChange;
		}
		set
		{
			SetProperty(ref isApplyingFolderChange, value, "IsApplyingFolderChange");
		}
	}

	public bool StartupEnabled
	{
		get
		{
			return startupEnabled;
		}
		set
		{
			SetProperty(ref startupEnabled, value, "StartupEnabled");
		}
	}

	public ThemeMode ThemeMode
	{
		get
		{
			return themeMode;
		}
		set
		{
			SetProperty(ref themeMode, value, "ThemeMode");
		}
	}

	public ViewMode ViewMode
	{
		get
		{
			return viewMode;
		}
		set
		{
			SetProperty(ref viewMode, value, "ViewMode");
		}
	}

	public string ActiveTweetTemplate
	{
		get
		{
			return activeTweetTemplate;
		}
		set
		{
			SetProperty(ref activeTweetTemplate, value, "ActiveTweetTemplate");
		}
	}

	public ToastService toastState => toastService;

	public ShellViewModel(SettingsService settingsService, AlpheratzDb db, PhotoScanner scanner, PhashService phashService, OrientationService orientationService, WorldService worldService, LocalEventBus eventBus, ToastService toastService, GalleryViewModel galleryViewModel, SettingsViewModel settingsViewModel, TagMasterViewModel tagMasterViewModel, TemplatePageViewModel templatePageViewModel, PhotoModalState photoModalState, DispatcherService dispatcherService, ThumbnailWorker thumbnailWorker)
	{
		this.settingsService = settingsService;
		this.db = db;
		this.scanner = scanner;
		this.phashService = phashService;
		this.orientationService = orientationService;
		this.worldService = worldService;
		this.eventBus = eventBus;
		this.toastService = toastService;
		this.galleryViewModel = galleryViewModel;
		this.settingsViewModel = settingsViewModel;
		this.tagMasterViewModel = tagMasterViewModel;
		this.templatePageViewModel = templatePageViewModel;
		this.photoModalState = photoModalState;
		this.dispatcherService = dispatcherService;
		this.thumbnailWorker = thumbnailWorker;
	}

	public WorldResolveViewModel CreateWorldResolveViewModel()
	{
		return new WorldResolveViewModel(db, thumbnailWorker, toastService);
	}

	public PhotoModalViewModel? createPhotoModalViewModel(PhotoThumbnailItem? photo)
	{
		if (photo == null)
		{
			return null;
		}
		List<PhotoThumbnailItem> photoList = galleryViewModel.photosState.displayItems.Select((PhotoGridItem item) => item.Photo).ToList();
		photoModalState.setPhotoList(photoList);
		photoModalState.onSelectPhoto(photo);
		return new PhotoModalViewModel(photoModalState, worldService, toastService);
	}

	public PhotoModalViewModel? createPhotoModalViewModelFromList(PhotoThumbnailItem? photo, IReadOnlyList<PhotoThumbnailItem> photos)
	{
		if (photo == null)
		{
			return null;
		}
		photoModalState.setPhotoList(photos.ToList());
		photoModalState.onSelectPhoto(photo);
		return new PhotoModalViewModel(photoModalState, worldService, toastService);
	}

	public async Task initialize()
	{
		_ = 2;
		try
		{
			await Task.WhenAll(registerScanListeners(), registerPhashWorker(), refreshSettings()).ConfigureAwait(continueOnCapturedContext: false);
			Task task = galleryViewModel.photosState.InitializeAsync();
			Task task2 = tagMasterViewModel.loadTags();
			Task task3 = galleryViewModel.loadWorldFilterOptions();
			Task task4 = galleryViewModel.loadTagFilterCounts();
			await Task.WhenAll(task, task2, task3, task4).ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrWhiteSpace(PhotoFolderPath) || !string.IsNullOrWhiteSpace(SecondaryPhotoFolderPath))
			{
				await startScan().ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				toastService.addToast("写真フォルダが未設定です。設定から参照フォルダを選択してください。");
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.initialize: threw: {value}");
			throw;
		}
	}

	public Task startScan()
	{
		if (isScanningRef)
		{
			return Task.CompletedTask;
		}
		isScanningRef = true;
		ScanStatus = "scanning";
		ScanProgress = new ScanProgressDto
		{
			processed = 0,
			total = 0,
			current_world = "",
			phase = "scan"
		};
		try
		{
			Task.Run(async delegate
			{
				try
				{
					await scanner.ScanAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					AppLogger.Error($"ShellViewModel.startScan: ScanAsync wrapper threw: {ex}");
					try
					{
						await eventBus.PublishAsync("scan:error", ex.Message).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (Exception value2)
					{
						AppLogger.Error($"ShellViewModel.startScan: failed to publish scan:error: {value2}");
					}
				}
			});
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.startScan: threw: {value}");
			isScanningRef = false;
			ScanStatus = "error";
			toastService.addToast($"スキャンの開始に失敗しました: {value}", ToastType.error);
		}
		return Task.CompletedTask;
	}

	public Task cancelScan()
	{
		try
		{
			scanner.RequestCancel();
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.cancelScan: threw: {value}");
			toastService.addToast($"スキャンの中断に失敗しました: {value}", ToastType.error);
		}
		return Task.CompletedTask;
	}

	public async Task refreshSettings()
	{
		try
		{
			AlpheratzSettingDto setting = await settingsService.GetSettingAsync().ConfigureAwait(continueOnCapturedContext: false);
			await dispatcherService.RunOnUiThread(delegate
			{
				PhotoFolderPath = setting.photoFolderPath ?? "";
				SecondaryPhotoFolderPath = setting.secondaryPhotoFolderPath ?? "";
				StartupEnabled = setting.enableStartup == true;
				ThemeMode = setting.themeMode.GetValueOrDefault();
				ViewMode valueOrDefault = setting.viewMode.GetValueOrDefault();
				ViewMode = valueOrDefault;
				galleryViewModel.displayState.ViewMode = valueOrDefault;
				ActiveTweetTemplate = setting.activeTweetTemplate ?? "";
				tweetTemplates.Clear();
				foreach (string item in setting.tweetTemplates ?? Array.Empty<string>())
				{
					tweetTemplates.Add(item);
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
			await settingsViewModel.refreshSettings().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.refreshSettings: threw: {value}");
			throw;
		}
	}

	private async Task runPostScanWorkflow()
	{
		await postScanGate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			try
			{
				if (await worldService.ResolveUnknownWorldsFromArchiveAsync().ConfigureAwait(continueOnCapturedContext: false) > 0)
				{
					await galleryViewModel.photosState.loadPhotos().ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (Exception value)
			{
				AppLogger.Error($"ShellViewModel.runPostScanWorkflow archive: threw: {value}");
			}
			try
			{
				await orientationService.StartOrientationCalculationAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception value2)
			{
				AppLogger.Error($"ShellViewModel.runPostScanWorkflow orientation: threw: {value2}");
			}
			try
			{
				await phashService.StartPdqAnalysisAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception value3)
			{
				AppLogger.Error($"ShellViewModel.runPostScanWorkflow phash: threw: {value3}");
			}
		}
		finally
		{
			postScanGate.Release();
		}
	}

	private Task registerScanListeners()
	{
		try
		{
			scanUnlistenFns.Add(eventBus.Subscribe("scan:progress", delegate(ScanProgressDto payload)
			{
				dispatcherService.requestAnimationFrame(delegate
				{
					ScanProgress = payload;
				});
				return Task.CompletedTask;
			}));
			scanUnlistenFns.Add(eventBus.Subscribe("scan:completed", (Func<Task>)async delegate
			{
				_ = 1;
				try
				{
					dispatcherService.requestAnimationFrame(delegate
					{
						isScanningRef = false;
						ScanStatus = "completed";
					});
					await galleryViewModel.loadWorldFilterOptions().ConfigureAwait(continueOnCapturedContext: false);
					await galleryViewModel.loadTagFilterCounts().ConfigureAwait(continueOnCapturedContext: false);
					Task.Run((Func<Task?>)runPostScanWorkflow);
				}
				catch (Exception value2)
				{
					AppLogger.Error($"ShellViewModel.scan:completed: threw: {value2}");
				}
			}));
			scanUnlistenFns.Add(eventBus.Subscribe("scan:error", delegate(string payload)
			{
				try
				{
					dispatcherService.requestAnimationFrame(delegate
					{
						isScanningRef = false;
						ScanStatus = "error";
						toastService.addToast(string.IsNullOrWhiteSpace(payload) ? "スキャンに失敗しました。" : payload, ToastType.error);
					});
				}
				catch (Exception value2)
				{
					AppLogger.Error($"ShellViewModel.scan:error: threw: {value2}");
				}
				return Task.CompletedTask;
			}));
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.registerScanListeners: threw: {value}");
			throw;
		}
		return Task.CompletedTask;
	}

	private async Task registerPhashWorker()
	{
		try
		{
			PhashProgressEvent phashProgressEvent = (PdqProgress = await phashService.GetPhashProgressAsync().ConfigureAwait(continueOnCapturedContext: false));
			IsPdqRunning = phashProgressEvent.total > 0 && phashProgressEvent.done < phashProgressEvent.total;
		}
		catch (Exception value)
		{
			AppLogger.Warn($"ShellViewModel.registerPhashWorker: initial probe failed: {value}");
			PdqProgress = PhashProgressEvent.Empty;
		}
		try
		{
			phashUnlistenFns.Add(eventBus.Subscribe("phash_progress", delegate(PhashProgressEvent payload)
			{
				IsPdqRunning = true;
				long tickCount = Environment.TickCount64;
				if (payload.done >= payload.total || tickCount - lastPhashUiUpdateTicks >= 1000)
				{
					lastPhashUiUpdateTicks = tickCount;
					PdqProgress = payload;
				}
				return Task.CompletedTask;
			}));
			phashUnlistenFns.Add(eventBus.Subscribe("phash_complete", (Func<Task>)delegate
			{
				IsPdqRunning = false;
				bool num = PdqProgress.total > 0;
				PdqProgress = PdqProgress with
				{
					done = PdqProgress.total,
					current = null
				};
				if (num)
				{
					toastService.addToast("類似画像の解析が完了しました", ToastType.success);
				}
				return Task.CompletedTask;
			}));
			phashUnlistenFns.Add(eventBus.Subscribe("phash_error", delegate(string payload)
			{
				try
				{
					IsPdqRunning = false;
					bool flag = payload == "中断されました";
					string msg = (flag ? "類似画像の解析を中断しました" : (string.IsNullOrWhiteSpace(payload) ? "類似画像の解析に失敗しました。" : ("類似画像の解析に失敗しました: " + payload)));
					toastService.addToast(msg, (!flag) ? ToastType.error : ToastType.info);
				}
				catch (Exception value3)
				{
					AppLogger.Error($"ShellViewModel.phash_error: threw: {value3}");
				}
				return Task.CompletedTask;
			}));
		}
		catch (Exception value2)
		{
			AppLogger.Error($"ShellViewModel.registerPhashWorker: subscription failed: {value2}");
			throw;
		}
	}

	public AlpheratzSettingDto buildSettingPayload(AlpheratzSettingDto? overrides = null)
	{
		return new AlpheratzSettingDto
		{
			photoFolderPath = (overrides?.photoFolderPath ?? PhotoFolderPath),
			secondaryPhotoFolderPath = (overrides?.secondaryPhotoFolderPath ?? SecondaryPhotoFolderPath),
			enableStartup = (overrides?.enableStartup ?? StartupEnabled),
			themeMode = (overrides?.themeMode ?? ThemeMode),
			viewMode = (overrides?.viewMode ?? ViewMode),
			tweetTemplates = (overrides?.tweetTemplates ?? tweetTemplates),
			activeTweetTemplate = (overrides?.activeTweetTemplate ?? ActiveTweetTemplate)
		};
	}

	public async Task applyFolderChange(string newPath)
	{
		IsApplyingFolderChange = true;
		try
		{
			await db.ResetPhotoCacheBySlotAsync(PendingFolderSlot).ConfigureAwait(continueOnCapturedContext: false);
			PendingFolderPath = null;
			await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
			{
				photoFolderPath = ((PendingFolderSlot == 1) ? newPath : PhotoFolderPath),
				secondaryPhotoFolderPath = ((PendingFolderSlot == 2) ? newPath : SecondaryPhotoFolderPath)
			})).ConfigureAwait(continueOnCapturedContext: false);
			await refreshSettings().ConfigureAwait(continueOnCapturedContext: false);
			await galleryViewModel.photosState.loadPhotos().ConfigureAwait(continueOnCapturedContext: false);
			await startScan().ConfigureAwait(continueOnCapturedContext: false);
			toastService.addToast("写真フォルダを更新しました");
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.applyFolderChange: threw: {value}");
			toastService.addToast($"写真フォルダの更新に失敗しました: {value}", ToastType.error);
		}
		finally
		{
			IsApplyingFolderChange = false;
		}
	}

	public async Task executeResetFolder(int slot)
	{
		if (string.IsNullOrEmpty((slot == 1) ? PhotoFolderPath : SecondaryPhotoFolderPath))
		{
			return;
		}
		string nextPrimaryPath = ((slot == 1) ? "" : PhotoFolderPath);
		string nextSecondaryPath = ((slot == 2) ? "" : SecondaryPhotoFolderPath);
		IsApplyingFolderChange = true;
		try
		{
			await db.ResetPhotoCacheBySlotAsync(slot).ConfigureAwait(continueOnCapturedContext: false);
			await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
			{
				photoFolderPath = nextPrimaryPath,
				secondaryPhotoFolderPath = nextSecondaryPath
			})).ConfigureAwait(continueOnCapturedContext: false);
			await refreshSettings().ConfigureAwait(continueOnCapturedContext: false);
			await galleryViewModel.photosState.loadPhotos().ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrEmpty(nextPrimaryPath) || !string.IsNullOrEmpty(nextSecondaryPath))
			{
				await startScan().ConfigureAwait(continueOnCapturedContext: false);
			}
			PendingResetRequest = null;
			toastService.addToast((slot == 1) ? "1st 写真フォルダをリセットしました" : "2nd 写真フォルダをリセットしました");
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.executeResetFolder: threw: {value}");
			toastService.addToast($"写真フォルダのリセットに失敗しました: {value}", ToastType.error);
		}
		finally
		{
			IsApplyingFolderChange = false;
		}
	}

	public void promptFolderChange(int slot, string newPath)
	{
		PendingFolderSlot = slot;
		PendingFolderPath = newPath;
		PendingResetRequest = null;
	}

	public void handleResetFolder(int slot)
	{
		string text = ((slot == 1) ? PhotoFolderPath : SecondaryPhotoFolderPath);
		if (!string.IsNullOrEmpty(text))
		{
			PendingFolderPath = null;
			PendingResetRequest = new PendingResetRequest(slot, text);
		}
	}

	public async Task handleToggleViewMode()
	{
		ViewMode nextMode = ((ViewMode == ViewMode.standard) ? ViewMode.gallery : ViewMode.standard);
		await handleSetViewMode(nextMode).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task handleSetViewMode(ViewMode nextMode)
	{
		if (ViewMode == nextMode)
		{
			return;
		}
		try
		{
			await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
			{
				viewMode = nextMode
			})).ConfigureAwait(continueOnCapturedContext: false);
			await dispatcherService.RunOnUiThread(delegate
			{
				if (nextMode == ViewMode.gallery && galleryViewModel.filtersState.GroupingMode != GroupingMode.none)
				{
					galleryViewModel.filtersState.GroupingMode = GroupingMode.none;
				}
				ViewMode = nextMode;
				galleryViewModel.displayState.ViewMode = nextMode;
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.handleSetViewMode: threw: {value}");
			toastService.addToast($"ビューモードの変更に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task handleThemeChange(ThemeMode mode)
	{
		try
		{
			await settingsService.SaveSettingAsync(buildSettingPayload(new AlpheratzSettingDto
			{
				themeMode = mode
			})).ConfigureAwait(continueOnCapturedContext: false);
			ThemeMode = mode;
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.handleThemeChange: threw: {value}");
			toastService.addToast($"テーマの変更に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task handleStartupPreference(bool enabled)
	{
		try
		{
			await settingsService.SaveStartupPreferenceAsync(enabled).ConfigureAwait(continueOnCapturedContext: false);
			StartupEnabled = enabled;
			toastService.addToast(enabled ? "Alpheratz をログイン時に起動する設定にしました。" : "Alpheratz のログイン時起動を無効にしました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"ShellViewModel.handleStartupPreference: threw: {value}");
			toastService.addToast($"自動起動設定の更新に失敗しました: {value}", ToastType.error);
		}
	}

	public async ValueTask DisposeAsync()
	{
		foreach (IAsyncDisposable scanUnlistenFn in scanUnlistenFns)
		{
			try
			{
				await scanUnlistenFn.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception value)
			{
				AppLogger.Error($"ShellViewModel.DisposeAsync: scan unlisten threw: {value}");
			}
		}
		foreach (IAsyncDisposable phashUnlistenFn in phashUnlistenFns)
		{
			try
			{
				await phashUnlistenFn.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception value2)
			{
				AppLogger.Error($"ShellViewModel.DisposeAsync: phash unlisten threw: {value2}");
			}
		}
		galleryViewModel.Cleanup();
	}
}
