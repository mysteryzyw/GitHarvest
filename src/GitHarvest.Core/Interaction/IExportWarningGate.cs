namespace GitHarvest.Core.Interaction;

/// <summary>
/// 「输出路径落在仓库工作区内」这类一次性警告的门（spec 用户故事 33：收到一次警告、可继续）。
/// 「会话内只提示一次」的状态必须由壳以单例持有——页面 VM 每次导航都会重建，
/// 把「已提示过」记在页面里等于每次进页都再问一遍。
/// </summary>
public interface IExportWarningGate
{
    /// <summary>
    /// 本次会话是否还需要就「输出路径落在仓库工作区内」征求用户意见；
    /// 用户已经确认过则为假（此后不再打扰，直接按可继续处理）。
    /// </summary>
    bool ShouldWarnOutputInsideRepository { get; }

    /// <summary>记下用户对「输出路径落在仓库工作区内」的确认（本次会话不再重复询问）。</summary>
    void AcknowledgeOutputInsideRepository();
}
