using System.Xml.Linq;

namespace Alpheratz.Tests;

/// <summary>
/// プロジェクトファイルに記載された配布資材の扱いを検証するテスト。
///
/// ビルド自体は成功しても publish 出力に必要な画像や埋め込み文言が含まれないと、
/// インストール後の画面表示や文言解決だけが壊れる。
/// ここではアプリを起動せずに csproj を読み、配布資材の宣言を固定する。
/// </summary>
public sealed class ProjectPackagingTests
{
    /// <summary>
    /// 画面表示とウィンドウアイコンで使う画像だけが出力コピー対象に含まれることを確認する。
    ///
    /// LogoText と avatar は XAML の ms-appx 参照から使われ、icon.ico はウィンドウとインストーラのアイコンとして使われる。
    /// Logo.png は現在の画面と非表示中の外部連携登録から参照されないため、配布対象から明示的に除外する。
    /// </summary>
    [Fact]
    public void FrontendProject_CopiesRuntimeAssetsRequiredByViewsAndWindow()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "app", "Alpheratz.Frontend.csproj");
        var document = XDocument.Load(projectPath);
        var copiedAssets = document.Descendants("Content")
            .Where(element =>
            {
                var copyMode = element.Element("CopyToOutputDirectory")?.Value.Trim();
                return string.Equals(copyMode, "PreserveNewest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(copyMode, "Always", StringComparison.OrdinalIgnoreCase);
            })
            .Select(element => NormalizeProjectPath(element.Attribute("Include")?.Value ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Assets/Logo.png", copiedAssets);
        Assert.Contains(
            document.Descendants("Content"),
            element => string.Equals(
                NormalizeProjectPath(element.Attribute("Remove")?.Value ?? string.Empty),
                "Assets/Logo.png",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Assets/LogoText.png", copiedAssets);
        Assert.Contains("Assets/avatar.jpg", copiedAssets);
        Assert.Contains("Assets/icon.ico", copiedAssets);
        Assert.Contains("Assets/icon.png", copiedAssets);
        Assert.Equal(
            "Assets/icon.ico",
            NormalizeProjectPath(document.Descendants("ApplicationIcon").Single().Value));
    }

    [Fact]
    public void FrontendProject_EmbedsJapaneseMessagePropertiesWithCatalogResourceName()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "app", "Alpheratz.Frontend.csproj");
        var document = XDocument.Load(projectPath);
        var messageResource = document.Descendants("EmbeddedResource").Single(element =>
            string.Equals(
                NormalizeProjectPath(element.Attribute("Include")?.Value ?? string.Empty),
                "Messages/messages.ja.properties",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            "Alpheratz.Messages.messages.ja.properties",
            messageResource.Attribute("LogicalName")?.Value);
        Assert.Equal("false", messageResource.Element("WithCulture")?.Value);
    }

    /// <summary>Windows App SDK の自動 Bootstrap がアプリ DLL 読込時に testhost を停止させないことを確認する。</summary>
    [Fact]
    public void FrontendAndTestProjects_DisableWindowsAppSdkAutoBootstrap()
    {
        var root = FindRepositoryRoot();
        var projectPaths = new[]
        {
            Path.Combine(root, "app", "Alpheratz.Frontend.csproj"),
            Path.Combine(root, "tests", "Alpheratz.Tests", "Alpheratz.Tests.csproj"),
        };

        foreach (var projectPath in projectPaths)
        {
            var values = XDocument.Load(projectPath)
                .Descendants("WindowsAppSdkBootstrapInitialize")
                .Select(element => element.Value.Trim())
                .ToArray();

            Assert.NotEmpty(values);
            Assert.All(values, value => Assert.Equal("false", value));
        }
    }

    /// <summary>プロセス全体で共有するテスト用パスが並列テストから変更されないことを確認する。</summary>
    [Fact]
    public void TestAssembly_DisablesParallelExecutionForSharedAppPathsOverride()
    {
        var behavior = typeof(ProjectPackagingTests).Assembly
            .GetCustomAttributes(typeof(CollectionBehaviorAttribute), inherit: false)
            .Cast<CollectionBehaviorAttribute>()
            .Single();

        Assert.True(behavior.DisableTestParallelization);
    }

    [Fact]
    public void SettingsPage_KeepsStellaRecordIntegrationHiddenWithReenableTodo()
    {
        var pagePath = Path.Combine(FindRepositoryRoot(), "app", "Features", "Settings", "SettingsPage.xaml");
        var document = XDocument.Load(pagePath);
        var integrationLabel = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Value.Contains(
                "SettingsPage.stellaRecordIntegrationLabel",
                StringComparison.Ordinal)));
        var hiddenContainer = integrationLabel.Ancestors().First(element =>
            element.Name.LocalName == "Grid");

        Assert.Equal("Collapsed", hiddenContainer.Attribute("Visibility")?.Value);
        Assert.Contains(
            document.DescendantNodes().OfType<XComment>(),
            comment => comment.Value.Contains("TODO", StringComparison.Ordinal)
                && comment.Value.Contains("StellaRecord", StringComparison.Ordinal));
    }

    /// <summary>
    /// コンパイル済みバインディングを含む DataTemplate が、実データ型を明示することを確認する。
    ///
    /// x:DataType がない場合、XAML コンパイラは DataTemplate 自体をデータ型として生成し、
    /// 項目の実体を表示するときに不正な型変換でアプリを終了させる。
    /// </summary>
    [Fact]
    public void XamlDataTemplates_WithCompiledBindings_DeclareItemType()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "app");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var violations = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !NormalizeProjectPath(Path.GetRelativePath(appDirectory, path))
                .StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "DataTemplate")
                .Where(template => template
                    .DescendantsAndSelf()
                    .Attributes()
                    .Any(attribute => attribute.Value.Contains("{x:Bind", StringComparison.Ordinal)))
                .Where(template => string.IsNullOrWhiteSpace(template.Attribute(xaml + "DataType")?.Value))
                .Select(_ => NormalizeProjectPath(Path.GetRelativePath(FindRepositoryRoot(), path))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void FrontendProject_DoesNotRecursivelyCopyGeneratedXbfSubfolder()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "app", "Alpheratz.Frontend.csproj");
        var document = XDocument.Load(projectPath);
        var xbfFilesItem = document.Descendants("_XbfFiles").Single();
        var removeDir = document.Descendants("RemoveDir").Single();

        Assert.Equal("$(OutDir)**/*.xbf", NormalizeProjectPath(xbfFilesItem.Attribute("Include")?.Value ?? string.Empty));
        Assert.Equal(
            "$(OutDir)$(AssemblyName)/**/*.xbf",
            NormalizeProjectPath(xbfFilesItem.Attribute("Exclude")?.Value ?? string.Empty));
        Assert.Equal(
            "$(OutDir)$(AssemblyName)/$(AssemblyName)",
            NormalizeProjectPath(removeDir.Attribute("Directories")?.Value ?? string.Empty));
    }

    /// <summary>
    /// 配布installerがMicrosoft署名済みのVisual C++ Redistributableを内包し、
    /// 既存appを置換する前に必要versionを確保することを確認する。
    /// </summary>
    [Fact]
    public void ReleasePackaging_BundlesVerifiedVisualCppRuntimeBeforeReplacingApp()
    {
        var root = FindRepositoryRoot();
        var prepareScript = File.ReadAllText(Path.Combine(
            root,
            "BuildWorks",
            "scripts",
            "prepare-vc-redist.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(
            root,
            "BuildWorks",
            "scripts",
            "build-release.ps1"));
        var installerScript = File.ReadAllText(Path.Combine(
            root,
            "BuildWorks",
            "nsis",
            "Installer.nsi"));

        Assert.Contains("https://aka.ms/vc14/vc_redist.x64.exe", prepareScript, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", prepareScript, StringComparison.Ordinal);
        Assert.Contains("CN=Microsoft Corporation", prepareScript, StringComparison.Ordinal);
        Assert.Contains("VC_redist.x64.exe", prepareScript, StringComparison.Ordinal);
        Assert.Contains("Microsoft Visual C++*Redistributable*(x64)*", prepareScript, StringComparison.Ordinal);
        Assert.Contains("prepare-vc-redist.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("/DVC_REDIST_VERSION=", buildScript, StringComparison.Ordinal);
        Assert.Contains("File /oname=vc_redist.x64.exe", installerScript, StringComparison.Ordinal);
        Assert.Contains("/install /passive /norestart", installerScript, StringComparison.Ordinal);
        Assert.Contains("SetRebootFlag true", installerScript, StringComparison.Ordinal);

        var runtimeCheckIndex = installerScript.IndexOf(
            "Call EnsureVisualCppRuntime",
            StringComparison.Ordinal);
        var appReplacementIndex = installerScript.IndexOf(
            "Delete \"$INSTDIR\\${MAINBINARYNAME}.exe\"",
            StringComparison.Ordinal);

        Assert.True(runtimeCheckIndex >= 0);
        Assert.True(appReplacementIndex > runtimeCheckIndex);
    }

    /// <summary>mainへ入る変更がWindows上のtestと最終installer生成を通ることを確認する。</summary>
    [Fact]
    public void CiWorkflow_GatesMainChangesWithTestsAndReleasePackaging()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("push:", workflow, StringComparison.Ordinal);
        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("branches: [main]", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-2025", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Alpheratz.Tests/Alpheratz.Tests.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\BuildWorks\\scripts\\build-release.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("choco install nsis", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// テスト実行ディレクトリからリポジトリルートを探す。
    ///
    /// dotnet test はビルド出力ディレクトリを基準に実行されるため、固定の相対パスにすると
    /// IDE、CLI、CI の実行方法差で csproj を見失う。上位ディレクトリをたどり、app プロジェクトがある場所を基準にする。
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
    /// MSBuild のパス表記を比較しやすいスラッシュ区切りへ正規化する。
    /// Windows のプロジェクトではバックスラッシュ表記が多いが、テストでは OS 差を避けるため同じ形式へ寄せる。
    /// </summary>
    private static string NormalizeProjectPath(string value)
        => value.Trim().Replace('\\', '/');
}
