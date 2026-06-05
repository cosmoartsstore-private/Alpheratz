using Microsoft.UI.Xaml;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Media;

namespace Alpheratz.Shared.Services;

/// <summary>
/// 要素の ActualTheme に基づいて ThemeDictionaries からリソースを解決する。
///
/// Application.Current.Resources["key"] や {ThemeResource} を Application.Resources 階層で引くと
/// Application.RequestedTheme で解決されるが、WinUI 3 では Application.RequestedTheme は
/// InitializeComponent 後に変更できないため、テーマのランタイム切替に追従しない。
/// 本ヘルパーは visual tree を要素から App.Resources まで辿り、各 ResourceDictionary の
/// ThemeDictionaries を element の ActualTheme で解決することで、要素単位のテーマ切替に対応する。
///
/// WinUI の組み込みコントロール ({ThemeResource ButtonBackground} 等) の解決についても、
/// Brushes.xaml を MainWindow.RootHost.Resources に MergedDictionary として登録することで
/// element ActualTheme で解決されるようになっている。
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")]
public static class ThemeHelper
{
    /// <summary>
    /// ShellPage.ApplyTheme から最後に通知されたテーマ。IValueConverter のように
    /// element context を受け取れない経路用のフォールバック。
    /// </summary>
    public static ElementTheme SelectedTheme { get; private set; } = ElementTheme.Default;

    /// <summary>テーマ切替時に呼び出して SelectedTheme を最新化する。</summary>
    public static void NotifySelectedThemeChanged(ElementTheme theme) => SelectedTheme = theme;

    /// <summary>element の ActualTheme に対応した Brush を ThemeDictionaries から取得する。</summary>
    public static Brush? Brush(FrameworkElement element, string key)
        => Resolve<Brush>(element, key);

    /// <summary>
    /// element 不在 (IValueConverter 等) で SelectedTheme をフォールバックに使う lookup。
    /// Application.Resources の MergedDictionaries 内 ThemeDictionaries を走査する。
    /// </summary>
    public static Brush? BrushForSelectedTheme(string key)
    {
        var themeKey = (SelectedTheme == ElementTheme.Dark
            ? "Default"
            : SelectedTheme == ElementTheme.Light
                ? "Light"
                : Application.Current.RequestedTheme == ApplicationTheme.Dark ? "Default" : "Light");
        if (TryFind<Brush>(Application.Current.Resources, themeKey, key, out var found)) return found;
        return null;
    }

    /// <summary>element の ActualTheme に対応したリソースを ThemeDictionaries から取得する。</summary>
    public static T? Resource<T>(FrameworkElement element, string key) where T : class
        => Resolve<T>(element, key);

    /// <summary>テーマ非依存リソース (FontFamily 等)。Application.Resources を直接引く。</summary>
    public static T? AppResource<T>(string key) where T : class
    {
        var res = Application.Current.Resources;
        if (res.TryGetValue(key, out var v) && v is T t) return t;
        for (int i = res.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (res.MergedDictionaries[i].TryGetValue(key, out var mv) && mv is T mt) return mt;
        }
        return null;
    }

    private static T? Resolve<T>(FrameworkElement element, string key) where T : class
    {
        // ActualTheme は visual tree に未追加だと ElementTheme.Default の可能性がある。
        // その場合は Application.RequestedTheme で代用する (起動直後のビルダで参照される)。
        var themeKey = element.ActualTheme switch
        {
            ElementTheme.Dark => "Default",
            ElementTheme.Light => "Light",
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark ? "Default" : "Light",
        };

        // 要素から logical parent を辿り各 Resources を ThemeDictionaries 込みで検索する。
        // VisualTreeHelper だと Tree に未追加の要素で null になるが、Parent は親が
        // 物理的に存在しなくても辿れるケースがある。両方を試して確実に検索する。
        FrameworkElement? current = element;
        while (current is not null)
        {
            if (TryFind<T>(current.Resources, themeKey, key, out var found)) return found;
            current = current.Parent as FrameworkElement;
        }

        // App.Resources は Application.RequestedTheme 経由でなく自前で themeKey を適用する。
        if (TryFind<T>(Application.Current.Resources, themeKey, key, out var appFound)) return appFound;
        return null;
    }

    private static bool TryFind<T>(ResourceDictionary dict, string themeKey, string key, out T? value) where T : class
    {
        // この階層の ThemeDictionaries
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var raw)
            && raw is ResourceDictionary themed
            && themed.TryGetValue(key, out var v)
            && v is T t)
        {
            value = t;
            return true;
        }
        // MergedDictionaries を逆順で (WinUI の解決順)
        for (int i = dict.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (TryFind<T>(dict.MergedDictionaries[i], themeKey, key, out value)) return true;
        }
        value = null;
        return false;
    }
}
