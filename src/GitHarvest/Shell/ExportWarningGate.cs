namespace GitHarvest.Shell;

/// <summary>
/// 一次性警告门的会话实现（单例）：把「已经提示过」记在应用运行期，页面重建不影响。
/// 与 <see cref="RepositorySession"/> 同一层次——都是内存态、应用退出即失效的会话数据。
/// </summary>
internal sealed class ExportWarningGate : GitHarvest.Core.Interaction.IExportWarningGate
{
    /// <inheritdoc />
    public bool ShouldWarnOutputInsideRepository => !_warnedOutputInsideRepository;

    private bool _warnedOutputInsideRepository;

    /// <inheritdoc />
    public void AcknowledgeOutputInsideRepository() => _warnedOutputInsideRepository = true;
}
