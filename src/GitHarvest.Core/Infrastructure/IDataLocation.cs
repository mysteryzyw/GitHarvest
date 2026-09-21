namespace GitHarvest.Core.Infrastructure;

/// <summary>
/// 数据目录的位置：默认是 <c>%APPDATA%\GitHarvest</c>，可以在设置页改到任意目录。
/// 「改到哪儿」记在**固定位置**的指针文件（默认数据目录下的 location.json）里——
/// 位置配置不能存在被它配置的那个目录里，否则形成自指循环（换目录后连「配置在哪」都变了）。
/// 数据目录下的日志、设置、每仓库状态、导出历史都从本接口派生，因此全应用只有一个真相源。
/// 实现由组合根在**日志初始化之前**解析一次，之后注入使用（日志目录本身也由它决定）。
/// </summary>
public interface IDataLocation
{
    /// <summary>当前生效的数据目录（绝对路径）。</summary>
    string DataDirectory { get; }

    /// <summary>日志目录（数据目录下的 logs）。</summary>
    string LogDirectory { get; }

    /// <summary>全局设置文件（数据目录下的 settings.json）。</summary>
    string SettingsFilePath { get; }

    /// <summary>每仓库状态文件（数据目录下的 repository-state.json）。</summary>
    string RepositoryStateFilePath { get; }

    /// <summary>导出历史文件（数据目录下的 export-history.jsonl）。</summary>
    string ExportHistoryFilePath { get; }

    /// <summary>指针文件路径（固定在默认数据目录下，不随自定义目录走）。</summary>
    string LocationFilePath { get; }

    /// <summary>默认数据目录（<c>%APPDATA%\GitHarvest</c>）；当前是否用它由 <see cref="IsCustom"/> 判断。</summary>
    string DefaultDataDirectory { get; }

    /// <summary>是否使用了自定义数据目录（假表示仍在默认位置）。</summary>
    bool IsCustom { get; }

    /// <summary>
    /// 读取指针文件时的容错提示（损坏、路径非法等）；一切正常时为 <see langword="null"/>。
    /// 本类型在日志初始化之前构造，因此提示由调用方在日志起来后补记（与模板回退提示同一手法）。
    /// </summary>
    string? Notice { get; }

    /// <summary>
    /// 把数据目录改为指定路径并写入指针文件（迁移成功后调用；写入失败向上抛出，由调用方回滚）。
    /// </summary>
    /// <param name="dataDirectory">新的数据目录（绝对路径）。</param>
    void SetDataDirectory(string dataDirectory);
}
