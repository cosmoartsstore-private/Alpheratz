namespace Alpheratz.Tests;

/// <summary>
/// AppPaths の標準 imgCache を触るテストを同じ xUnit collection にまとめる。
///
/// ThumbnailService は標準 imgCache にファイルを生成し、AlpheratzDb.ResetPhotoCacheBySlotAsync は
/// 同じ imgCache を物理削除する。並列実行すると生成直後のファイルが別テストから消されるため、
/// この collection に入ったテスト同士は直列化する。
/// </summary>
[CollectionDefinition("AppPaths imgCache")]
public sealed class AppPathsCacheTestCollection
{
    public const string Name = "AppPaths imgCache";
}
