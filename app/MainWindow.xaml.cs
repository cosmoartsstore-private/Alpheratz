using Alpheratz.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Alpheratz;

/// <summary>
/// アプリケーションのメインウィンドウ。
/// RootHost (Grid) に ShellPage 等のルートページを差し替えて表示する。
/// 起動時に最大化した状態で表示される。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        AppLogger.Trace("MainWindow.ctor: enter");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // InnerException を掘り下げてログ出力する。
            // XamlParseException は内部の実例外を InnerException に持つため。
            AppLogger.Error($"MainWindow.ctor: InitializeComponent failed: {ex}");
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                AppLogger.Error($"MainWindow.ctor: InnerException: {inner}");
            throw;
        }
        AppLogger.Trace("MainWindow.ctor: InitializeComponent done");
        Title = "Alpheratz";
        ExtendsContentIntoTitleBar = false;

        // Win32 API 経由でウィンドウアイコン設定・最大化する
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets\\icon.ico");
        if (appWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();

        AppLogger.Trace("MainWindow.ctor: exit");
    }

    /// <summary>ルートページを指定してウィンドウを生成するコンビニエンスコンストラクタ。</summary>
    public MainWindow(Page rootPage) : this()
    {
        AppLogger.Trace("MainWindow.ctor(Page): enter");
        try
        {
            SetRoot(rootPage);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MainWindow.ctor(Page): SetRoot failed: {ex}");
            throw;
        }
        AppLogger.Trace("MainWindow.ctor(Page): exit");
    }

    /// <summary>RootHost に RequestedTheme を設定し、ウィンドウ全体のテーマを切り替える。</summary>
    public void SetTheme(ElementTheme theme)
    {
        try { RootHost.RequestedTheme = theme; }
        catch (Exception ex) { AppLogger.Error($"MainWindow.SetTheme: threw: {ex}"); }
    }

    /// <summary>RootHost の子要素を差し替えてルートページを設定する。</summary>
    public void SetRoot(Page rootPage)
    {
        AppLogger.Trace($"MainWindow.SetRoot: enter rootPage={rootPage?.GetType().FullName ?? "(null)"}");
        try
        {
            RootHost.Children.Clear();
            RootHost.Children.Add(rootPage);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"MainWindow.SetRoot: failed: {ex}");
            throw;
        }
        AppLogger.Trace("MainWindow.SetRoot: exit");
    }
}
