using Xunit;

// AppPaths のテスト用データディレクトリはプロセス全体で共有されるため、テスト間の競合を防ぐ。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
