using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alpheratz.Alpheratz_Frontend_XamlTypeInfo;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Core.Imaging;
using Alpheratz.Core.Scanner;
using Alpheratz.Features.Bootstrap;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.PhotoModal;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Shell;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;
using Alpheratz.Services;
using Alpheratz.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IApplicationOverrides")]
[WinRTExposedType(typeof(Alpheratz_AppWinRTTypeDetails))]
public class App : Application, IXamlMetadataProvider
{
	private Window? mainWindow;

	private ServiceProvider? serviceProvider;

	private bool shellSwapped;

	private DateTimeOffset splashShownAt;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private bool _contentLoaded;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	private XamlMetaDataProvider __appProvider;

	public static Window? MainWindowInstance => (Application.Current as App)?.mainWindow;

	public static IServiceProvider? Services => (Application.Current as App)?.serviceProvider;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	private XamlMetaDataProvider _AppProvider
	{
		get
		{
			if (__appProvider == null)
			{
				__appProvider = new XamlMetaDataProvider();
			}
			return __appProvider;
		}
	}

	public App()
	{
		try
		{
			string settingDir = AppPaths.GetSettingDir();
			if (settingDir != null)
			{
				string path = Path.Combine(settingDir, "setting.json");
				if (File.Exists(path))
				{
					base.RequestedTheme = ((JsonSerializer.Deserialize<AlpheratzSetting>(File.ReadAllText(path, Encoding.UTF8))?.ThemeMode == "dark") ? ApplicationTheme.Dark : ApplicationTheme.Light);
				}
				else
				{
					base.RequestedTheme = ApplicationTheme.Light;
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.Warn("App.ctor: theme pre-set failed: " + ex.Message);
		}
		try
		{
			InitializeComponent();
		}
		catch (Exception value)
		{
			AppLogger.Error($"App.ctor: InitializeComponent failed: {value}");
			throw;
		}
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		base.UnhandledException += delegate(object _, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
		{
			AppLogger.Error($"App.UnhandledException: {e.Exception}");
			e.Handled = true;
		};
		try
		{
			OnLaunchedCore(args);
		}
		catch (Exception value)
		{
			AppLogger.Error($"App.OnLaunched: fatal: {value}");
		}
	}

	private void OnLaunchedCore(LaunchActivatedEventArgs args)
	{
		DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
		UiThread.Queue = dispatcherQueue;
		ServiceCollection services = new ServiceCollection();
		services.AddSingleton<AppLifecycleService>();
		services.AddSingleton<LocalEventBus>();
		services.AddSingleton<AppConfig>();
		services.AddSingleton<AlpheratzDb>();
		services.AddSingleton<ThumbnailService>();
		services.AddSingleton<ThumbnailWorker>();
		services.AddSingleton((IServiceProvider _) => new DispatcherService(dispatcherQueue));
		services.AddSingleton<PhotoScanner>();
		services.AddSingleton<ToastService>();
		services.AddSingleton<DialogService>();
		services.AddSingleton<SettingsService>();
		services.AddSingleton<PhotoService>();
		services.AddSingleton<WorldService>();
		services.AddSingleton<PhashService>();
		services.AddSingleton<OrientationService>();
		services.AddSingleton<GalleryPhotosState>();
		services.AddSingleton<GalleryFiltersState>();
		services.AddSingleton<GallerySelectionState>();
		services.AddSingleton<GalleryDisplayState>();
		services.AddSingleton<GalleryScrollState>();
		services.AddSingleton<PhotoModalState>();
		services.AddSingleton<GalleryViewModel>();
		services.AddSingleton<SettingsViewModel>();
		services.AddSingleton<TagMasterViewModel>();
		services.AddSingleton<TemplatePageViewModel>();
		services.AddSingleton<ShellViewModel>();
		serviceProvider = services.BuildServiceProvider();
		AppLifecycleService requiredService = serviceProvider.GetRequiredService<AppLifecycleService>();
		requiredService.advanceTo(AppLifecyclePhase.sdkReady);
		try
		{
			serviceProvider.GetRequiredService<AlpheratzDb>().Initialize();
		}
		catch (Exception value)
		{
			AppLogger.Error($"App.OnLaunchedCore: DB initialize failed: {value}");
			throw;
		}
		requiredService.advanceTo(AppLifecyclePhase.servicesReady);
		ShellViewModel shellViewModel = serviceProvider.GetRequiredService<ShellViewModel>();
		MainWindow win;
		try
		{
			win = new MainWindow();
			mainWindow = win;
		}
		catch (Exception value2)
		{
			AppLogger.Error($"App.OnLaunchedCore: MainWindow creation failed: {value2}");
			throw;
		}
		try
		{
			mainWindow.Activate();
		}
		catch (Exception value3)
		{
			AppLogger.Error($"App.OnLaunchedCore: MainWindow.Activate failed: {value3}");
			throw;
		}
		BootstrapPage bootstrapPage;
		try
		{
			bootstrapPage = new BootstrapPage();
			bootstrapPage.SetPhase(requiredService.CurrentPhase);
			win.SetRoot(bootstrapPage);
			splashShownAt = DateTimeOffset.UtcNow;
		}
		catch (Exception value4)
		{
			AppLogger.Error($"App.OnLaunchedCore: BootstrapPage setup failed: {value4}");
			throw;
		}
		requiredService.PhaseAdvanced += delegate(object? _, AppLifecyclePhase phase)
		{
			try
			{
				dispatcherQueue.TryEnqueue(delegate
				{
					try
					{
						bootstrapPage.SetPhase(phase);
					}
					catch (Exception value6)
					{
						AppLogger.Error($"App.bootstrap.PhaseAdvanced ui: threw: {value6}");
					}
					if (phase >= AppLifecyclePhase.dataReady)
					{
						TimeSpan timeSpan = DateTimeOffset.UtcNow - splashShownAt;
						TimeSpan timeSpan2 = TimeSpan.FromSeconds(1.5);
						if (timeSpan >= timeSpan2)
						{
							SwapToShell(win, shellViewModel, bootstrapPage);
						}
						else
						{
							TimeSpan delay = timeSpan2 - timeSpan;
							_ = Task.Delay(delay).ContinueWith((Task task) => dispatcherQueue.TryEnqueue(delegate
							{
								SwapToShell(win, shellViewModel, bootstrapPage);
							}), TaskContinuationOptions.ExecuteSynchronously);
						}
					}
				});
			}
			catch (Exception value5)
			{
				AppLogger.Error($"App.bootstrap.PhaseAdvanced: threw: {value5}");
			}
		};
		BeginSplashSequenceAsync(bootstrapPage, shellViewModel, requiredService);
	}

	private async Task BeginSplashSequenceAsync(BootstrapPage bootstrapPage, ShellViewModel shellViewModel, AppLifecycleService lifecycle)
	{
		await Task.Delay(TimeSpan.FromSeconds(1.0));
		await bootstrapPage.FadeInAsync();
		splashShownAt = DateTimeOffset.UtcNow;
		shellViewModel.initialize().ContinueWith(delegate(Task t)
		{
			try
			{
				if (t.IsCompletedSuccessfully)
				{
					lifecycle.advanceTo(AppLifecyclePhase.dataReady);
				}
				else if (t.Exception != null)
				{
					AppLogger.Error($"App.initContinuation: initialize() failed: {t.Exception}");
				}
			}
			catch (Exception value)
			{
				AppLogger.Error($"App.initContinuation: threw: {value}");
			}
		}, TaskContinuationOptions.ExecuteSynchronously);
	}

	private async void SwapToShell(MainWindow win, ShellViewModel shellViewModel, BootstrapPage bootstrapPage)
	{
		if (shellSwapped)
		{
			return;
		}
		shellSwapped = true;
		try
		{
			await bootstrapPage.FadeOutAsync();
			ShellPage root = new ShellPage(shellViewModel);
			win.SetRoot(root);
		}
		catch (Exception value)
		{
			AppLogger.Error($"App.SwapToShell: failed: {value}");
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///App.xaml");
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IXamlType GetXamlType(Type type)
	{
		return _AppProvider.GetXamlType(type);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public IXamlType GetXamlType(string fullName)
	{
		return _AppProvider.GetXamlType(fullName);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2502")]
	[DebuggerNonUserCode]
	public XmlnsDefinition[] GetXmlnsDefinitions()
	{
		return _AppProvider.GetXmlnsDefinitions();
	}
}
