using GitHarvest.Core.Export;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 输出路径解析的测试（纯函数）：spec 用户故事 43 的「本次 &gt; 每仓库上次 &gt; 全局默认」。
/// 三级都为空时返回空串（由界面引导用户选择），任一非空即用最近的可用值。
/// </summary>
public sealed class OutputPathResolverTests
{
    [Fact]
    public void 本次的临时值优先()
        => Assert.Equal(
            @"D:\本次",
            OutputPathResolver.Resolve(@"D:\本次", @"D:\每仓库", @"D:\全局"));

    [Fact]
    public void 没有本次时用每仓库上次()
        => Assert.Equal(
            @"D:\每仓库",
            OutputPathResolver.Resolve(sessionPath: null, @"D:\每仓库", @"D:\全局"));

    [Fact]
    public void 前两级都空时用全局默认()
        => Assert.Equal(
            @"D:\全局",
            OutputPathResolver.Resolve(sessionPath: null, lastOutputPath: null, @"D:\全局"));

    [Fact]
    public void 空串与空白按未配置处理()
        => Assert.Equal(
            @"D:\全局",
            OutputPathResolver.Resolve("   ", string.Empty, @"D:\全局"));

    [Fact]
    public void 三级都未配置时返回空串()
        => Assert.Equal(string.Empty, OutputPathResolver.Resolve(null, null, null));
}
