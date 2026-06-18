using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Alpheratz.Tests;

/// <summary>
/// coverage 測定対象から外すソースの範囲を固定するテスト。
///
/// このプロジェクトでは WinUI の Page/UserControl、Composition アニメーション、OS ダイアログ、
/// プロセス起動点のようなフレームワーク境界だけを <c>ExcludeFromCodeCoverage</c> で除外する。
/// 一方で、状態計算やサービス、DB、パーサ、表示計算 helper は coverage 対象に残す。
/// このテストは除外属性が不用意に広がることを検出し、80% ゲートの意味が崩れないようにする。
/// </summary>
public sealed class CoveragePolicyTests
{
    private const string CoverageBoundaryJustification =
        "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.";

    /// <summary>
    /// <c>ExcludeFromCodeCoverage</c> が許可された UI/OS 境界ファイルだけに付いていることを確認する。
    ///
    /// 除外は coverage の逃げ道ではなく、テストしにくいフレームワーク呼び出しを薄い glue に閉じ込めたことを示す印である。
    /// そのため、新しい除外を追加する場合はこのテストの許可リストを更新し、理由をレビューできる差分として残す。
    /// </summary>
    [Fact]
    public void ExcludeFromCoverage_IsLimitedToReviewedFrameworkBoundaryFiles()
    {
        var root = FindRepositoryRoot();
        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "app"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !NormalizeRepositoryPath(root, path).Contains("/artifacts/", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = NormalizeRepositoryPath(root, path),
                Justification = ExtractCoverageJustification(path),
            })
            .Where(item => item.Justification is not null)
            .ToArray();

        var expected = new[]
        {
            "app/App.xaml.cs",
            "app/Features/Bootstrap/BootstrapPage.xaml.cs",
            "app/Features/Gallery/Controls/GalleryFilterPanel.xaml.cs",
            "app/Features/Gallery/Controls/GalleryFilterSidebar.xaml.cs",
            "app/Features/Gallery/Controls/GalleryGridStage.xaml.cs",
            "app/Features/Gallery/Controls/GalleryMasonryView.xaml.cs",
            "app/Features/Gallery/Controls/MonthNav.xaml.cs",
            "app/Features/Gallery/GalleryPage.xaml.cs",
            "app/Features/Gallery/GroupDrillDownPage.xaml.cs",
            "app/Features/PhotoModal/PhotoModalPage.xaml.cs",
            "app/Features/Settings/SettingsPage.xaml.cs",
            "app/Features/Shell/Controls/ShellHeaderBar.xaml.cs",
            "app/Features/Shell/Controls/ShellStage.xaml.cs",
            "app/Features/Shell/ShellPage.xaml.cs",
            "app/Features/WorldResolve/WorldResolvePage.xaml.cs",
            "app/MainWindow.xaml.cs",
            "app/Program.cs",
            "app/Shared/Animations/AnimationHelper.cs",
            "app/Shared/Controls/AnimatedFavoriteStar.xaml.cs",
            "app/Shared/Controls/AppIcon.xaml.cs",
            "app/Shared/Controls/CustomScrollbar.xaml.cs",
            "app/Shared/Controls/EmptyState.xaml.cs",
            "app/Shared/Controls/FavoriteCornerBadge.xaml.cs",
            "app/Shared/Controls/PhotoGrid.xaml.cs",
            "app/Shared/Controls/PhotoGridItemsView.xaml.cs",
            "app/Shared/Controls/ScanningOverlay.xaml.cs",
            "app/Shared/Controls/ToastHost.xaml.cs",
            "app/Shared/Controls/WaveProgressBar.xaml.cs",
            "app/Shared/Controls/WrapPanel.cs",
            "app/Shared/Services/DialogService.cs",
            "app/Shared/Services/ThemeHelper.cs",
        };

        Assert.Equal(
            expected.OrderBy(path => path),
            actual.Select(item => item.Path).OrderBy(path => path));
        Assert.All(actual, item => Assert.Equal(CoverageBoundaryJustification, item.Justification));
    }

    /// <summary>
    /// 生成物のみ除外する runsettings が、ソースファイルをファイルパターンで除外していないことを確認する。
    ///
    /// coverage の主ゲートは <c>tests/coverlet.generated-only.runsettings</c> を使う。
    /// ここでは generated obj 以外の <c>ExcludeByFile</c> が混ざっていないことを固定し、
    /// 通常ソースの測定対象は属性付きのフレームワーク境界だけに限定する。
    /// </summary>
    [Fact]
    public void GeneratedOnlyRunsettings_ExcludesOnlyBuildGeneratedObjFiles()
    {
        var settingsPath = Path.Combine(FindRepositoryRoot(), "tests", "coverlet.generated-only.runsettings");
        var document = XDocument.Load(settingsPath);
        var excludeByFile = document.Descendants("ExcludeByFile").Single().Value.Trim();

        Assert.Equal("**/artifacts/obj/**/*.cs", excludeByFile);
    }

    /// <summary>
    /// テスト実行ディレクトリからリポジトリルートを探す。
    /// dotnet test はビルド出力を基準に実行されるため、上位階層をたどって app プロジェクトを探す。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "app", "Alpheratz.Frontend.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Alpheratz.Frontend.csproj を含むリポジトリルートが見つかりません。");
    }

    /// <summary>
    /// ファイル内の coverage 除外理由を取り出す。
    /// 属性が無いファイルは null を返し、属性が複数ある場合は最初の理由を検証対象にする。
    /// </summary>
    private static string? ExtractCoverageJustification(string path)
    {
        var text = File.ReadAllText(path);
        var match = Regex.Match(text, @"ExcludeFromCodeCoverage\(Justification = ""([^""]+)""\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 絶対パスをリポジトリルートからのスラッシュ区切り相対パスへ正規化する。
    /// Windows の区切り文字差をなくし、許可リストとの比較を安定させる。
    /// </summary>
    private static string NormalizeRepositoryPath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');
}
