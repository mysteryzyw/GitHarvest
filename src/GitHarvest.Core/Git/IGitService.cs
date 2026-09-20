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

    /// <summary>
    /// 读取指定引用（分支名 / 远程跟踪分支 / <c>HEAD</c>）可达的提交列表中的一页，
    /// 新提交在前。<see cref="CommitQuery.Search"/> 非空时按「哈希 / 提交信息 / 作者」过滤
    /// （大小写不敏感），分页切片作用在过滤后的结果集上；
    /// <see cref="CommitListResult.HasMore"/> 标记是否还有下一页（驱动界面的滚动增量加载）。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="reference">提交可达的引用：分支名（含远程跟踪分支）或 <c>HEAD</c>。</param>
    /// <param name="query">过滤与分页条件。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>读取结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<CommitListResult> GetCommitsAsync(
        string repositoryPath,
        string reference,
        CommitQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取单个提交的完整详情：完整提交信息、作者与时间、父提交与该次提交的 diffstat 汇总
    /// （合并提交按对第一父提交的变更计）。入参接受完整 / 短哈希或其他 git 可解析的提交表达。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="hash">提交（完整 / 短哈希）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>详情结果；失败不抛异常，提示在结果里。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<CommitDetailResult> GetCommitDetailAsync(
        string repositoryPath,
        string hash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 祖先校验（spec 用户故事 18）：基准提交必须是 Head 提交的祖先，两者不在同一祖先链上时
    /// 禁止导出。入参接受完整 / 短哈希或其他 git 可解析的提交表达；
    /// 基准与 Head 为同一提交时视为祖先（差异为空，由空范围逻辑处理）。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="baseHash">基准提交（完整 / 短哈希）。</param>
    /// <param name="headHash">Head 提交（完整 / 短哈希）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>校验结果；「不是祖先」是成功得到的结论（<see cref="AncestorCheckResult.IsAncestor"/> 为假），流程失败才走 <see cref="AncestorCheckResult.Failure"/>。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<AncestorCheckResult> CheckAncestorAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算双点变更范围 <c>base..head</c>（CONTEXT.md「变更范围」：含 Head 提交本身、不含基准提交）：
    /// 对两次提交的树做差异并归类（<see cref="ChangeKind"/>，重命名按 git -M 默认 50% 阈值），
    /// 附每文件的 +行数（二进制为空）与展示大小。与重命名检测同一份差异还产出
    /// 新旧 blob 哈希，供导出编排（ticket 09）直接取快照。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="baseHash">基准提交（完整 / 短哈希）。</param>
    /// <param name="headHash">Head 提交（完整 / 短哈希）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>计算结果；空差异（含基准与 Head 为同一提交）是成功结论（空清单），失败不抛异常。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<ChangeRangeResult> GetChangeRangeAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计双点范围 <c>base..head</c> 内的提交数（含 Head、不含基准，与变更范围同一语义），
    /// 供第 2 步「选择提交」的范围条即时展示。入参接受完整 / 短哈希或其他 git 可解析的提交表达。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="baseHash">基准提交（完整 / 短哈希）。</param>
    /// <param name="headHash">Head 提交（完整 / 短哈希）。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>统计结果；基准与 Head 为同一提交时成功且数量为 0，失败不抛异常。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<CommitCountResult> GetRangeCommitCountAsync(
        string repositoryPath,
        string baseHash,
        string headHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一个 blob 的内容原样写入 <paramref name="destination"/>，供导出编排写出
    /// 「更新前 / 更新后」快照（用户故事 24：导出的是完整文件快照而不是 diff 补丁）。
    /// 内容按**原始字节**拷贝、不做任何编码转换（二进制文件同样正确），也不整体读进内存。
    /// 子模块指针（gitlink）的哈希指向另一个仓库的提交、不是本仓库的 blob，
    /// 调用方不应把它交给本方法（落在导出侧的责任由 <c>SnapshotPlanner</c> 承担）。
    /// </summary>
    /// <param name="repositoryPath">仓库目录（仓库根或其子目录）。</param>
    /// <param name="blobHash">blob 完整哈希（来自 <see cref="ChangedFile.OldBlobHash"/> / <see cref="ChangedFile.NewBlobHash"/>）。</param>
    /// <param name="destination">内容落点流；本方法不关闭它，生命周期由调用方管理。</param>
    /// <param name="cancellationToken">取消令牌；取消时终止正在执行的 git 进程。</param>
    /// <returns>取内容的结果；失败不抛异常，且失败时目标流可能已有半截内容（清理口径由调用方定）。</returns>
    /// <exception cref="OperationCanceledException">令牌已取消，或执行过程中被取消。</exception>
    Task<BlobCopyResult> CopyBlobToAsync(
        string repositoryPath,
        string blobHash,
        Stream destination,
        CancellationToken cancellationToken = default);
}
