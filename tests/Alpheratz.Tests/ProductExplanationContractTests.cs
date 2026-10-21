using System.Xml.Linq;
using Alpheratz.Messages;

namespace Alpheratz.Tests;

/// <summary>実表示とは分けて、説明が参照する文言と外部宛先の宣言を検証する。</summary>
public sealed class ProductExplanationContractTests
{
    [Fact]
    public void CreditLinksAndBackupDescriptionsReferenceTheCurrentDeclaredTargetsAndScope()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "app", "Alpheratz.Frontend.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "app", "Features", "Settings", "SettingsPage.xaml"));
        var credits = document.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "CreditsSection");
        var links = credits.Descendants().Where(element => element.Name.LocalName == "HyperlinkButton").Select(element => element.Attribute("NavigateUri")!.Value).Order().ToArray();
        Assert.Equal(new[] { "https://cosmo-arts-store.booth.pm/", "https://lit.link/planet_vrc", "https://x.com/planet_vrc" }.Order(), links);
        Assert.All(links, link => Assert.Equal("https", new Uri(link).Scheme));
        Assert.Equal("Bootstrap Icons / MIT License / Copyright (c) 2019-2024 The Bootstrap Authors", MessageCatalog.getMsg("SettingsPage.bootstrapIconsCredit"));
        var textBindings = document.Descendants().Where(element => element.Name.LocalName == "TextBlock").Select(element => element.Attribute("Text")?.Value).ToArray();
        Assert.Contains(textBindings, text => text?.Contains("SettingsPage.photoUserDataBackupDescription", StringComparison.Ordinal) == true);
        Assert.Contains(textBindings, text => text?.Contains("SettingsPage.photoUserDataRestoreDescription", StringComparison.Ordinal) == true);
        Assert.Equal("現在読み込んでいるすべての写真から、ファイル名、お気に入り、タグを保存します。同じファイル名の写真は1件にまとめます。", MessageCatalog.getMsg("SettingsPage.photoUserDataBackupDescription"));
        Assert.Equal("復元すると、同じファイル名の写真へお気に入りを戻し、保存済みのタグを追加します。現在付いているタグは削除しません。", MessageCatalog.getMsg("SettingsPage.photoUserDataRestoreDescription"));
    }
}
