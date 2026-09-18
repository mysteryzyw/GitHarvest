namespace GitHarvest.Tests.Support;

/// <summary>
/// 一个在系统临时目录下创建的独占测试目录，测试结束时递归删除。
/// </summary>
internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "githarvest-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // 清理失败不影响测试结论：个别文件被占用或拒绝访问时保留现场，
        // 不让清理噪声盖过测试结论。
        try
        {
            DeleteDirectory(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 递归删除测试中创建的目录。git 写入的松散对象文件带只读位，直接删会 Access denied，
    /// 因此删除前先把整棵树上的只读属性摘掉。造仓库的测试辅助（bare clone 留下的工作仓库等）
    /// 也要清自己的中间产物，提为公共方法避免每个使用点各写一遍。
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
