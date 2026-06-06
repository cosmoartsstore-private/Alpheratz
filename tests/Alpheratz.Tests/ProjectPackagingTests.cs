using System.Xml.Linq;

namespace Alpheratz.Tests;

/// <summary>
/// プロジェクトファイルに記載された配布資材の扱いを検証するテスト。
///
/// 画像の参照は XAML、ウィンドウ初期化、StellaRecord 登録のように別々の層へ散っている。
/// ビルド自体は成功しても publish 出力に画像が含まれないと、インストール後の画面表示や外部登録だけが壊れる。
/// ここではアプリを起動せずに csproj を読み、実行時に必要な資材が出力コピー対象として宣言されていることを固定する。
/// </summary>
public sealed class ProjectPackagingTests
{
    /// <summary>
    /// 画面表示、ウィンドウアイコン、StellaRecord 登録で使う画像が出力コピー対象に含まれることを確認する。
    ///
    /// Logo 系と avatar は XAML の ms-appx 参照から使われ、icon.ico はウィンドウとインストーラのアイコンとして使われる。
    /// icon.png は SettingsPage から AppContext.BaseDirectory 配下の実ファイルとして StellaRecord へ渡すため、
    /// publish 出力に存在しないと登録先でアイコン解決に失敗する。このテストはその配布漏れを検出する。
    /// </summary>
    [Fact]
    public void FrontendProject_CopiesRuntimeAssetsRequiredByViewsAndRegistrations()
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

        Assert.Contains("Assets/Logo.png", copiedAssets);
        Assert.Contains("Assets/LogoText.png", copiedAssets);
        Assert.Contains("Assets/avatar.jpg", copiedAssets);
        Assert.Contains("Assets/icon.ico", copiedAssets);
        Assert.Contains("Assets/icon.png", copiedAssets);
        Assert.Equal(
            "Assets/icon.ico",
            NormalizeProjectPath(document.Descendants("ApplicationIcon").Single().Value));
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
