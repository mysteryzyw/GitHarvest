namespace GitHarvest.Tests.Support;

/// <summary>
/// 集成测试共享的造仓库辅助：在临时目录里用真实 git.exe 造出形态可配的小仓库
/// （普通 / bare、指定提交数）。这是后续所有 ticket 集成测试的统一造仓库入口——
/// 仓库形态知识（bare 不能 commit、要先工作仓库再克隆等）只沉淀在这里，各测试类不再
/// 各自编写 init/commit/clone 序列。
/// </summary>
internal static class TestRepository
{
    /// <summary>
    /// 在 <paramref name="parentDirectory"/> 下造一个名为 <paramref name="name"/> 的仓库并返回其路径。
    /// <paramref name="commitCount"/> 为 0 时是空仓库（unborn HEAD）；
    /// <paramref name="bare"/> 为真时先造普通工作仓库再 <c>clone --bare</c>
    /// （bare 仓库没有工作区，不能直接 commit），中间的工作仓库会被删掉，只留 bare 仓库。
    /// </summary>
    public static async Task<string> CreateAsync(
        string parentDirectory,
        string name,
        int commitCount,
        bool bare = false)
    {
        var repositoryPath = Path.Combine(parentDirectory, name);

        if (bare)
        {
            // bare 仓库没有工作区不能 commit：先在工作仓库凑够提交，
            // 再克隆成 bare 形态，随后删掉工作仓库只留 bare 仓库。
            var workingPath = repositoryPath + ".work";
            await TestGit.RunAsync(["init", "-q", "-b", "main", workingPath]);
            for (var index = 1; index <= commitCount; index++)
            {
                await TestGit.RunAsync(["-C", workingPath, "commit", "-q", "--allow-empty", "-m", $"第 {index} 个提交"]);
            }

            await TestGit.RunAsync(["clone", "-q", "--bare", workingPath, repositoryPath]);
            TestDirectory.DeleteDirectory(workingPath);
            return repositoryPath;
        }

        Directory.CreateDirectory(repositoryPath);
        await TestGit.RunAsync(["init", "-q", "-b", "main", repositoryPath]);
        for (var index = 1; index <= commitCount; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryPath, $"file-{index}.txt"),
                $"第 {index} 个提交的内容");
            await TestGit.RunAsync(["add", "."], repositoryPath);
            await TestGit.RunAsync(["commit", "-q", "-m", $"第 {index} 个提交"], repositoryPath);
        }

        return repositoryPath;
    }
}
