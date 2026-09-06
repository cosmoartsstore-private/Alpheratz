namespace Alpheratz.Tests;

/// <summary>リリーススクリプトとインストーラの配布・更新契約を検証する。</summary>
public sealed class ReleasePackagingTests
{
    [Fact]
    public void BuildRelease_RemovesStaleInstaller_PreparesRuntime_AndChecksMakensisResult()
    {
        var source = ReadRepositoryText("BuildWorks", "scripts", "build-release.ps1");
        var staleOutputRemoval = source.IndexOf(
            "Remove-Item -LiteralPath $InstallerOutputPath -Force",
            StringComparison.Ordinal);
        var runtimePreparation = source.IndexOf("prepare-vc-redist.ps1", StringComparison.Ordinal);
        var makensisInvocation = source.IndexOf("& $MakensisPath", StringComparison.Ordinal);

        Assert.True(staleOutputRemoval >= 0);
        Assert.True(runtimePreparation > staleOutputRemoval);
        Assert.True(makensisInvocation > runtimePreparation);
        Assert.Contains("/DVC_REDIST_VERSION=$VcRedistVersion", source, StringComparison.Ordinal);
        Assert.Contains("if ($LASTEXITCODE -ne 0)", source, StringComparison.Ordinal);
        Assert.Contains("if (!(Test-Path $InstallerOutputPath))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishScripts_CheckDotnetExitCodesAndRequiredRuntimeOutputs()
    {
        var appScript = ReadRepositoryText("BuildWorks", "scripts", "publish-app-release.ps1");
        var launcherScript = ReadRepositoryText("BuildWorks", "scripts", "publish-launcher-release.ps1");

        AssertCommandResultChecked(appScript, "& dotnet publish", "Frontend の publish に失敗しました。");
        AssertCommandResultChecked(launcherScript, "& dotnet publish", "Launcher の publish に失敗しました。");
        Assert.Contains("\"App.xbf\"", appScript, StringComparison.Ordinal);
        Assert.Contains("\"MainWindow.xbf\"", appScript, StringComparison.Ordinal);
        Assert.Contains("\"resources.pri\"", appScript, StringComparison.Ordinal);
        Assert.Contains("Missing frontend executable", appScript, StringComparison.Ordinal);
        Assert.Contains("Missing launcher executable", launcherScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_ReinstallPreservesDataAndUninstallRemovesStartupRegistration()
    {
        var source = ReadRepositoryText("BuildWorks", "nsis", "Installer.nsi");
        var installSection = Section(source, "Section \"Install\"");
        var uninstallSection = Section(source, "Section \"Uninstall\"");

        Assert.Contains("RMDir /r \"$INSTDIR\\app\"", installSection, StringComparison.Ordinal);
        Assert.Contains("CreateDirectory \"$INSTDIR\\Data\"", installSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RMDir /r \"$INSTDIR\\Data\"", installSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RMDir /r \"$INSTDIR\"", installSection, StringComparison.Ordinal);
        Assert.Contains(
            "DeleteRegValue HKCU \"Software\\Microsoft\\Windows\\CurrentVersion\\Run\" \"${PRODUCTNAME}\"",
            uninstallSection,
            StringComparison.Ordinal);
    }

    private static void AssertCommandResultChecked(string source, string invocation, string failureMessage)
    {
        var invocationIndex = source.IndexOf(invocation, StringComparison.Ordinal);
        Assert.True(invocationIndex >= 0);
        var exitCodeIndex = source.IndexOf("if ($LASTEXITCODE -ne 0)", invocationIndex, StringComparison.Ordinal);
        Assert.True(exitCodeIndex > invocationIndex);
        var failureIndex = source.IndexOf(failureMessage, exitCodeIndex, StringComparison.Ordinal);

        Assert.True(failureIndex > exitCodeIndex);
    }

    private static string Section(string source, string heading)
    {
        var start = source.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("SectionEnd", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadRepositoryText(params string[] pathParts)
    {
        var fullPathParts = new string[pathParts.Length + 1];
        fullPathParts[0] = FindRepositoryRoot();
        Array.Copy(pathParts, 0, fullPathParts, 1, pathParts.Length);
        return File.ReadAllText(Path.Combine(fullPathParts));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "app", "Alpheratz.Frontend.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Alpheratz.Frontend.csproj を含むリポジトリルートが見つかりません。");
    }
}
