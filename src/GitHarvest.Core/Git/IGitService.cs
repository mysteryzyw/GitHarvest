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

    /// <summary>
    /// 读取仓库的分支候选项：本地分支与远程跟踪分支（<see cref="BranchInfo.IsRemote"/> 区分），
    /// 每项附带最新提交摘要；游离头指针时列表最前面有一个 <see cref="BranchInfo.IsDetached"/>
    /// 候选项。空仓库（还没有任何提交）返回空列表而非失败。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>读取结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<BranchListResult> GetBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 「拉取」：执行 <c>git fetch</c>——只更新远程跟踪分支，不 merge、不动工作区。
    /// 仓库没有配置远程时不是错误崩溃，而是 <see cref="FetchFailure.NoRemote"/> 的明确结果。
    /// 成功后由调用方重新读取分支数据完成刷新。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>拉取结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<FetchResult> FetchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
