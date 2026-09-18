namespace GitHarvest.Core.Git;

/// <summary>
/// 仓库级 Git 操作的唯一接口：ViewModel 与导出编排都只依赖它，
/// 对 git.exe 进程、CLI 输出解析与编码一无所知（ADR-0001）。
/// 本接口在 ticket 04 以「打开仓库」起步，分支/提交读取等能力在后续 ticket 扩展；
/// Core 的单元测试全部 mock 本接口（spec 的测试接缝决策）。
/// </summary>
public interface IGitService
{
    /// <summary>
    /// 打开仓库：验证目标目录是 Git 仓库（含 bare），并读取概要信息（根路径、bare 与否、
    /// 当前分支、提交总数）。从仓库的子目录打开时，Git 会向上找到真正的仓库根。
    /// 「提交数不足以构成变更范围」不是打开失败——仓库照常打开，由
    /// <see cref="RepositoryInfo.HasEnoughCommits"/> 让界面给出提示。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>打开结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<RepositoryOpenResult> OpenRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
