using GitHarvest.Core.Export;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 输出路径与仓库工作区位置关系的测试（纯函数，不碰文件系统）：
/// spec 用户故事 33 只要求「落在仓库工作区内时给一次性警告、可继续」，
/// 所以判断必须准——误报会让正常导出每次都弹警告，漏报则失去提示的意义。
/// </summary>
public sealed class OutputPathPlacementTests
{
    [Theory]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\DemoService")]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\DemoService\交付物")]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\DemoService\交付物\2026-09-20")]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\DemoService\")]
    public void 落在仓库工作区内(string repositoryRoot, string outputPath)
        => Assert.True(OutputPathPlacement.IsInsideRepository(outputPath, repositoryRoot));

    [Theory]
    [InlineData(@"D:\Code\DemoService", @"D:\交付物")]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\DemoService-备份")]
    [InlineData(@"D:\Code\DemoService", @"D:\Code\其他仓库")]
    [InlineData(@"D:\Code\DemoService", @"C:\Code\DemoService")]
    public void 不在仓库工作区内(string repositoryRoot, string outputPath)
        => Assert.False(OutputPathPlacement.IsInsideRepository(outputPath, repositoryRoot));

    [Fact]
    public void 大小写与分隔符差异按同一路径处理()
    {
        Assert.True(OutputPathPlacement.IsInsideRepository(
            @"d:/code/demoservice/交付物",
            @"D:\Code\DemoService"));
    }

    [Fact]
    public void 相对路径按当前目录解析后判断()
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        Assert.True(OutputPathPlacement.IsInsideRepository(Path.Combine("子目录", "更深"), currentDirectory));
        Assert.False(OutputPathPlacement.IsInsideRepository(@"Z:\绝对路径之外", currentDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 输出路径为空时不算落在里面(string outputPath)
        => Assert.False(OutputPathPlacement.IsInsideRepository(outputPath, @"D:\Code\DemoService"));

    [Fact]
    public void 仓库根为空时不算落在里面()
        => Assert.False(OutputPathPlacement.IsInsideRepository(@"D:\交付物", string.Empty));

    [Fact]
    public void 含非法字符的输出路径按不在里面处理而不抛异常()
        => Assert.False(OutputPathPlacement.IsInsideRepository("D:\\交付|物", @"D:\Code\DemoService"));
}
