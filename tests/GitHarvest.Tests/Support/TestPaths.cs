namespace GitHarvest.Tests.Support;

/// <summary>
/// 路径断言辅助：git 输出（正斜杠风格）与持久化文件里的路径形式跟测试代码构造的路径
/// 可能有分隔符/尾部分隔符差异，比较前先统一归一化，避免路径形式差异干扰等值判断。
/// </summary>
internal static class TestPaths
{
    /// <summary>归一化为完整路径、无结尾分隔符的形式，供等值断言使用。</summary>
    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\');
}
