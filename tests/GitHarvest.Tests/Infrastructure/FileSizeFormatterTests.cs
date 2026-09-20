using GitHarvest.Core.Infrastructure;

namespace GitHarvest.Tests.Infrastructure;

/// <summary>
/// 文件大小展示格式化的单元测试：B / KB / MB / GB 的换算与一位小数，
/// 以及无大小条目（子模块指针）的「—」占位。
/// </summary>
public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "0 B")]
    [InlineData(511L, "511 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(8396L, "8.2 KB")]   // 原型样例「8.2 KB」
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    public void 字节数按1024进制格式化(long? bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes));
    }
}
