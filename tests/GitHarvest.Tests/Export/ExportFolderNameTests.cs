using GitHarvest.Core.Export;

namespace GitHarvest.Tests.Export;

/// <summary>
/// 更新日期目录名的规则测试（纯函数，不碰文件系统）：
/// 默认名是 yyyy-MM-dd；用户编辑后的名字必须能安全地当作一个目录段；
/// 目录已存在时自动让位成 _HHmmss 变体（spec 用户故事 31：同日多次导出不互相覆盖）。
/// 「哪些名字已存在」通过委托传入，因此这里不需要真实目录。
/// </summary>
public sealed class ExportFolderNameTests
{
    [Fact]
    public void 默认目录名按年月日格式生成()
    {
        var name = ExportFolderName.DefaultFor(new DateTimeOffset(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8)));

        Assert.Equal("2026-09-20", name);
    }

    [Theory]
    [InlineData("2026-09-20")]
    [InlineData("2026-09-20_134500")]
    [InlineData("v2.4 交付")]
    [InlineData("发布_2.4-交付")]
    [InlineData("第 3 批")]
    public void 常规目录名通过校验(string name) => Assert.Null(ExportFolderName.Validate(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026/09/20")]
    [InlineData(@"2026\09\20")]
    [InlineData("..")]
    [InlineData("交付:第一批")]
    [InlineData("交付?第一批")]
    [InlineData("交付*第一批")]
    [InlineData("交付<第一批>")]
    [InlineData("交付|第一批")]
    [InlineData("交付\"第一批")]
    public void 非法目录名被拒绝并给出原因(string name)
    {
        var message = ExportFolderName.Validate(name);

        Assert.NotNull(message);
        Assert.NotEmpty(message!);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    public void Windows保留设备名被拒绝(string name) => Assert.NotNull(ExportFolderName.Validate(name));

    [Theory]
    [InlineData("2026-09-20.")]
    [InlineData("2026-09-20 ")]
    public void 以点或空格结尾的目录名被拒绝(string name) => Assert.NotNull(ExportFolderName.Validate(name));

    [Fact]
    public void 目录名超长被拒绝()
    {
        // Windows 单个目录段的上限是 255 个字符。
        Assert.NotNull(ExportFolderName.Validate(new string('长', 256)));
        Assert.Null(ExportFolderName.Validate(new string('长', 255)));
    }

    [Fact]
    public void 目录不存在时沿用请求的名字()
    {
        var resolved = ExportFolderName.ResolveUnique("2026-09-20", Moment, _ => false);

        Assert.Equal("2026-09-20", resolved);
    }

    [Fact]
    public void 目录已存在时追加HHmmss并及时刻()
    {
        var resolved = ExportFolderName.ResolveUnique("2026-09-20", Moment, name => name == "2026-09-20");

        Assert.Equal("2026-09-20_134500", resolved);
    }

    [Fact]
    public void 带时分的名字也冲突时继续加序号()
    {
        var withSecondTaken = new HashSet<string> { "2026-09-20", "2026-09-20_134500" };

        Assert.Equal(
            "2026-09-20_134500_2",
            ExportFolderName.ResolveUnique("2026-09-20", Moment, withSecondTaken.Contains));

        var withCounterTaken = new HashSet<string> { "2026-09-20", "2026-09-20_134500", "2026-09-20_134500_2" };

        Assert.Equal(
            "2026-09-20_134500_3",
            ExportFolderName.ResolveUnique("2026-09-20", Moment, withCounterTaken.Contains));
    }

    /// <summary>固定的「此刻」：目录名解析要用到时钟，测试里不能读系统时间。</summary>
    private static readonly DateTimeOffset Moment =
        new(2026, 9, 20, 13, 45, 0, TimeSpan.FromHours(8));
}
