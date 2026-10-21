using Alpheratz.Core.Database;
using Alpheratz.Features.TagMaster;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using Microsoft.Data.Sqlite;

namespace Alpheratz.Tests;

/// <summary>タグの保存失敗と保存後の一覧更新失敗を区別し、DBと画面・通知の対応を確認する。</summary>
public sealed class TagMasterFailureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alpheratz.TagMaster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AlpheratzDb db;
    private readonly ToastService toasts = new();
    private readonly TagMasterViewModel model;
    private string DbPath => Path.Combine(directory, "tags.db");

    public TagMasterFailureTests()
    {
        Directory.CreateDirectory(directory); db = new AlpheratzDb(DbPath); db.Initialize();
        model = new TagMasterViewModel(db, toasts);
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Alpheratz.TagMaster.Tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
        SqliteConnection.ClearPool(connection); Directory.Delete(directory, recursive: true);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=OFF;" + sql; command.ExecuteNonQuery();
    }

    private long CountTag(string name)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM tags WHERE name = @name"; command.Parameters.AddWithValue("@name", name);
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>初期化後の専用DBを意図的に壊し、書込成功後のGetStringによる読込失敗を再現する。</summary>
    private void CorruptTagRead()
    {
        Execute("DROP TABLE tags; CREATE TABLE tags (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE); INSERT INTO tags (name) VALUES (NULL);");
    }

    [Fact]
    public async Task DuplicateDraftIgnoresCaseAndWhitespaceWithoutChangingStorage()
    {
        await db.CreateTagMasterAsync("Alpha"); await model.loadTags(); model.TagDraft = "  ALPHA  "; await model.createTag();
        Assert.Equal(["Alpha"], model.masterTags); Assert.Equal(["Alpha"], await db.GetAllTagsAsync()); Assert.Equal("  ALPHA  ", model.TagDraft);
        Assert.Equal("同じタグが登録されています。", Assert.Single(toasts.toasts).Msg);
    }

    [Fact]
    public async Task MaximumLengthDraftIsAcceptedAndClearedAfterCommit()
    {
        var tag = new string('あ', 25); model.TagDraft = $"  {tag}  "; await model.createTag();
        Assert.Equal([tag], await db.GetAllTagsAsync()); Assert.Equal([tag], model.masterTags); Assert.Equal(string.Empty, model.TagDraft);
        Assert.Equal("タグを保存しました。", Assert.Single(toasts.toasts).Msg);
        var tooLong = new string('あ', 26); model.TagDraft = tooLong; await model.createTag();
        Assert.Equal([tag], await db.GetAllTagsAsync()); Assert.Equal([tag], model.masterTags); Assert.Equal(tooLong, model.TagDraft);
        Assert.Equal("タグは25文字以内で入力してください。", toasts.toasts.Last().Msg); Assert.Equal(ToastType.error, toasts.toasts.Last().Type);
    }

    [Fact]
    public async Task InsertFailureKeepsDraftAndExistingTagsForRetry()
    {
        await db.CreateTagMasterAsync("既存"); await model.loadTags(); Execute("CREATE TRIGGER reject_tag_insert BEFORE INSERT ON tags BEGIN SELECT RAISE(ABORT, '検証用の保存失敗'); END;");
        model.TagDraft = "  新規  "; await model.createTag();
        Assert.Equal(["既存"], model.masterTags); Assert.Equal(["既存"], await db.GetAllTagsAsync()); Assert.Equal("  新規  ", model.TagDraft);
        Assert.Equal("タグを追加できませんでした。入力内容は保持されています。", Assert.Single(toasts.toasts).Msg);
        Execute("DROP TRIGGER reject_tag_insert;"); await model.createTag();
        Assert.Equal(string.Empty, model.TagDraft); Assert.Equal(1, CountTag("新規"));
    }

    [Fact]
    public async Task RefreshFailureAfterInsertKeepsCommittedTagAndReportsSuccess()
    {
        CorruptTagRead(); model.masterTags.Add("既存表示"); model.TagDraft = "新規"; await model.createTag();
        Assert.Equal(1, CountTag("新規")); Assert.Contains("新規", model.masterTags); Assert.Contains("既存表示", model.masterTags); Assert.Equal(string.Empty, model.TagDraft);
        Assert.Equal("タグを保存しました。", Assert.Single(toasts.toasts).Msg);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.loadTags());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshFailureAfterDeleteUpdatesOnlyMatchingDisplayedTag(bool displayed)
    {
        CorruptTagRead(); await db.CreateTagMasterAsync("victim"); model.masterTags.Add("other"); if (displayed) model.masterTags.Add("VICTIM");
        Assert.True(await model.tryDeleteTag("victim")); Assert.Equal(0, CountTag("victim")); Assert.Equal(["other"], model.masterTags);
        Assert.Equal("タグと写真への設定を削除しました。", Assert.Single(toasts.toasts).Msg);
    }

    [Fact]
    public async Task DeleteFailureRetainsTagAndAllowsWrapperRetry()
    {
        await db.CreateTagMasterAsync("victim"); await model.loadTags(); Execute("CREATE TRIGGER reject_tag_delete BEFORE DELETE ON tags BEGIN SELECT RAISE(ABORT, '検証用の削除失敗'); END;");
        Assert.False(await model.tryDeleteTag("victim")); Assert.Equal(["victim"], model.masterTags); Assert.Equal(1, CountTag("victim"));
        Assert.Equal("タグを削除できませんでした。写真への設定は変更されていません。", Assert.Single(toasts.toasts).Msg);
        Execute("DROP TRIGGER reject_tag_delete;"); await model.deleteTag("victim"); Assert.Empty(model.masterTags); Assert.Equal(0, CountTag("victim"));
    }
}
