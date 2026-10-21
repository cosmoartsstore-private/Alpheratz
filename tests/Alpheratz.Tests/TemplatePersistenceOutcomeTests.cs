using System.Text.Json;
using System.Text.Json.Nodes;
using Alpheratz.Core;
using Alpheratz.Features.Gallery;
using Alpheratz.Features.Template;
using Alpheratz.Models;
using Alpheratz.Services;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;

namespace Alpheratz.Tests;

/// <summary>実設定fileへの公開template操作と、失敗後の入力保持・再保存を画面なしで検証する。</summary>
public sealed class TemplatePersistenceOutcomeTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("select")]
    [InlineData("delete")]
    public async Task LockedSettingKeepsCommittedTemplatesDraftAndOtherSettingsUntilSameOperationRetries(string operation)
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        if (operation is "edit" or "delete") vm.startEdit("保存A");
        vm.TweetTemplateDraft = "  変更後  ";
        var editing = vm.EditingTweetTemplate; var draft = vm.TweetTemplateDraft;
        var bytes = File.ReadAllBytes(fixture.SettingPath);
        using (var locked = new FileStream(fixture.SettingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await fixture.Operate(operation);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.SettingPath));
        Assert.Equal(["保存A", "保存B"], vm.tweetTemplates); Assert.Equal("保存A", vm.ActiveTweetTemplate);
        Assert.Equal(editing, vm.EditingTweetTemplate); Assert.Equal(draft, vm.TweetTemplateDraft);
        var failure = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.error, failure.Type);
        Assert.Equal(operation switch { "select" => "使用するテンプレートを変更できませんでした。使用中のテンプレートは変更されていません。もう一度選択してください。", "delete" => "テンプレートを削除できませんでした。保存済みの内容と編集中の入力は変更されていません。もう一度削除してください。", _ => "テンプレートを保存できませんでした。変更内容は画面に残っています。もう一度保存してください。" }, failure.Msg);
        Assert.Empty(Directory.GetFiles(fixture.Root, "setting.*.tmp")); fixture.AssertOtherSettings(); fixture.Toasts.toasts.Clear();

        await fixture.Operate(operation);

        var expected = operation switch { "create" => new[] { "保存A", "保存B", "変更後" }, "edit" => ["変更後", "保存B"], "delete" => ["保存B"], _ => ["保存A", "保存B"] };
        var active = operation switch { "edit" => "変更後", "select" or "delete" => "保存B", _ => "保存A" };
        fixture.AssertSaved(expected, active); fixture.AssertOtherSettings();
        if (operation == "select") { Assert.Equal(editing, vm.EditingTweetTemplate); Assert.Equal(draft, vm.TweetTemplateDraft); }
        else { Assert.Null(vm.EditingTweetTemplate); Assert.Equal("", vm.TweetTemplateDraft); }
        var success = Assert.Single(fixture.Toasts.toasts); Assert.Equal(ToastType.info, success.Type); Assert.Equal("テンプレートを保存しました。", success.Msg);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("duplicate-edit")]
    [InlineData("raw-too-long")]
    [InlineData("weighted-too-long")]
    public async Task InvalidDraftDoesNotChangeStorageOrLoseInputAndCanBeCorrected(string condition)
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        if (condition == "duplicate-edit") vm.startEdit("保存A");
        var draft = condition switch { "blank" => " \r\n ", "duplicate" or "duplicate-edit" => " 保存B ", "raw-too-long" => new string('A', 281), _ => new string('界', 141) };
        vm.TweetTemplateDraft = draft; var before = File.ReadAllBytes(fixture.SettingPath); var editing = vm.EditingTweetTemplate;

        await vm.saveTemplate(new AlpheratzSettingDto());

        Assert.Equal(before, File.ReadAllBytes(fixture.SettingPath)); fixture.AssertSaved(["保存A", "保存B"], "保存A");
        Assert.Equal(draft, vm.TweetTemplateDraft); Assert.Equal(editing, vm.EditingTweetTemplate);
        Assert.Equal(ToastType.error, Assert.Single(fixture.Toasts.toasts).Type); fixture.AssertOtherSettings();
        vm.TweetTemplateDraft = "修正済み"; fixture.Toasts.toasts.Clear(); await vm.saveTemplate(new AlpheratzSettingDto());
        fixture.AssertSaved(condition == "duplicate-edit" ? ["修正済み", "保存B"] : ["保存A", "保存B", "修正済み"], condition == "duplicate-edit" ? "修正済み" : "保存A");
        Assert.Equal("テンプレートを保存しました。", Assert.Single(fixture.Toasts.toasts).Msg); fixture.AssertOtherSettings();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRawAndWeightedLimitsPersistWithoutTruncation(bool japanese)
    {
        using var fixture = new Fixture(); var draft = new string(japanese ? '界' : 'A', japanese ? 140 : 280);
        fixture.ViewModel.TweetTemplateDraft = draft; await fixture.ViewModel.saveTemplate(new AlpheratzSettingDto());
        fixture.AssertSaved(["保存A", "保存B", draft], "保存A"); Assert.Equal(280, TemplatePageViewModel.countPostCharacters(draft)); fixture.AssertOtherSettings();
    }

    [Fact]
    public async Task CancelLastDeleteRecreationAndUnknownSelectionPreserveCommittedState()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.startEdit("保存A"); vm.TweetTemplateDraft = "保存しない編集"; var before = File.ReadAllBytes(fixture.SettingPath);
        vm.cancelEdit(); Assert.Null(vm.EditingTweetTemplate); Assert.Equal("", vm.TweetTemplateDraft); Assert.Equal(before, File.ReadAllBytes(fixture.SettingPath));
        await vm.selectTemplate("存在しないtemplate"); Assert.Empty(fixture.Toasts.toasts); Assert.Equal(before, File.ReadAllBytes(fixture.SettingPath));
        await vm.deleteTemplate("保存A", new AlpheratzSettingDto()); fixture.AssertSaved(["保存B"], "保存B");
        vm.startEdit("保存B"); await vm.deleteTemplate("保存B", new AlpheratzSettingDto()); fixture.AssertSaved([], "");
        Assert.Null(vm.EditingTweetTemplate); Assert.Equal("", vm.TweetTemplateDraft);
        vm.TweetTemplateDraft = " 再作成 "; await vm.saveTemplate(new AlpheratzSettingDto()); fixture.AssertSaved(["再作成"], "再作成"); fixture.AssertOtherSettings();
    }

    [Fact]
    public async Task AllTokensUnknownWorldAndExpandedLimitAreResolvedBeforeAnyExternalOperation()
    {
        var toasts = new ToastService(); var vm = new TemplatePageViewModel(null!, null!, toasts);
        var photo = new PhotoThumbnailItem { PhotoPath = "所有.png", PhotoFilename = "所有.png", WorldName = "  世界  ", WorldId = "wrld_owned", Timestamp = "2026-10-20T01:02:03", Tags = ["night sky", "夜"] };
        Assert.Equal("世界|世界|世界|wrld_owned|2026-10-20 01:02|2026-10-20T01:02:03|所有.png||#nightsky #夜", vm.buildTweetText("{world}|{world-name}|{world_name}|{world_id}|{date}|{timestamp}|{file}|{memo}|{tags}", photo));
        photo.WorldName = " "; photo.WorldId = null; photo.Timestamp = "短い"; photo.Tags = [];
        Assert.Equal("ワールド不明|||短い|", vm.buildTweetText("{world}|{world_id}|{date}|{timestamp}|{tags}", photo));
        photo.WorldName = new string('界', 141); vm.ActiveTweetTemplate = "{world}";
        Assert.False(await vm.openTweetIntent(photo));
        Assert.Equal("写真の情報を入れた投稿文が上限を超えています（282/280）。タグまたはテンプレートを短くしてください。", Assert.Single(toasts.toasts).Msg);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Alpheratz.Template.Headless.Tests", Guid.NewGuid().ToString("N"));
        public string SettingPath => Path.Combine(Root, "setting.json");
        public AppConfig Config { get; }
        public TemplatePageViewModel ViewModel { get; }
        public ToastService Toasts { get; } = new();
        private readonly string otherSettings;
        public Fixture()
        {
            Directory.CreateDirectory(Root); Config = new AppConfig(Root);
            Config.SaveSetting(new AlpheratzSetting { PhotoFolderPath = "保持する写真1", SecondaryPhotoFolderPath = "保持する写真2", ThemeMode = "dark", ViewMode = "masonry", EnableStartup = true, StartupPreferenceSet = true, OpenWorldLinkOnPost = true, TweetTemplates = ["保存A", "保存B"], ActiveTweetTemplate = "保存A", PendingFolderCleanup = new PendingFolderCleanupSetting { OperationId = "保持する要求", SourceSlot = 2, CommittedPath = "保持する写真2" } });
            otherSettings = OtherSettings(); ViewModel = new TemplatePageViewModel(new SettingsService(Config), null!, Toasts); ViewModel.applySettings(["保存A", "保存B"], "保存A", true);
        }
        public Task Operate(string operation) => operation switch { "select" => ViewModel.selectTemplate("保存B"), "delete" => ViewModel.deleteTemplate("保存A", new AlpheratzSettingDto()), _ => ViewModel.saveTemplate(new AlpheratzSettingDto()) };
        public void AssertSaved(string[] templates, string active)
        {
            Assert.Equal(templates, ViewModel.tweetTemplates); Assert.Equal(active, ViewModel.ActiveTweetTemplate);
            var fresh = new AppConfig(Root).LoadSetting(); Assert.Equal(templates, fresh.TweetTemplates); Assert.Equal(active, fresh.ActiveTweetTemplate);
        }
        private string OtherSettings() { var json = JsonNode.Parse(JsonSerializer.Serialize(Config.LoadSetting()))!.AsObject(); json.Remove("tweetTemplates"); json.Remove("activeTweetTemplate"); return json.ToJsonString(); }
        public void AssertOtherSettings() => Assert.Equal(otherSettings, OtherSettings());
        public void Dispose() { Assert.Equal(Path.Combine(Path.GetTempPath(), "Alpheratz.Template.Headless.Tests"), Path.GetDirectoryName(Root)); Directory.Delete(Root, recursive: true); }
    }
}
