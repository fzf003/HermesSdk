namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流引擎选项。
/// 控制 YAML 配置文件目录、自动导出等行为。
/// </summary>
public sealed class WorkflowEngineOptions
{
    /// <summary>
    /// YAML 配置文件存放目录。默认 "./workflows"。
    /// 启动时从此目录加载已有 YAML 文件（如果存在）。
    /// 引擎启动完成后，异步导出到此目录。
    /// 多实例部署时，每个实例配置不同的目录以避免冲突。
    /// </summary>
    public string YamlConfigDirectory { get; set; } = "./workflows";

    /// <summary>
    /// 是否启用启动后自动导出。默认 false。
    /// 启用后引擎启动完成时会异步导出 YAML 文件到配置目录。
    /// </summary>
    public bool AutoExportEnabled { get; set; } = false;
}
