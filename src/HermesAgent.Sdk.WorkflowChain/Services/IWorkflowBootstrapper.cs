namespace HermesAgent.Sdk.WorkflowChain;

/// <summary>
/// 工作流引导程序 — 负责将 YAML 工作流定义加载到 WorkflowRegistry 并同步到 WorkflowEngine 运行时。
///
/// YAML 是 C# Handler 的运行时策略配置（timeout/retry/error_policy/prompt），
/// 通过热重载机制生效，不参与工作流拓扑定义。
///
/// 典型用法（Program.cs）：
/// <code>
/// var host = CreateHostBuilder(args).Build();
/// var bootstrapper = host.Services.GetRequiredService&lt;IWorkflowBootstrapper&gt;();
/// await bootstrapper.LoadAndApplyFromFileAsync("workflow.yaml");
/// await host.RunAsync();
/// </code>
/// </summary>
public interface IWorkflowBootstrapper
{
    /// <summary>
    /// 将指定工作流从 Registry 同步到 Engine 运行时。
    /// 首次调用为注册，重复调用为替换（Replace 语义）。
    /// </summary>
    /// <param name="workflowName">工作流名称</param>
    /// <param name="ct">取消令牌</param>
    Task ApplyAsync(string workflowName, CancellationToken ct = default);

    /// <summary>
    /// 同步所有已注册但未应用的工作流到 Engine 运行时。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    Task ApplyAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 检查指定工作流是否已应用（已同步到 Engine）。
    /// </summary>
    /// <param name="workflowName">工作流名称</param>
    bool IsApplied(string workflowName);

    /// <summary>
    /// 从 YAML 内容加载工作流定义并同步到 Engine 运行时。
    /// 一步完成：解析 YAML → 注册到 Registry → 同步到 Engine。
    /// 此方法替代了已废弃的 RegisterFromYaml（builder 阶段），
    /// 将 YAML 处理归位到运行时层。
    /// </summary>
    /// <param name="yamlContent">YAML 内容字符串</param>
    /// <param name="version">可选的版本号覆盖（默认从 YAML 中读取）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工作流名称</returns>
    Task<string> LoadAndApplyAsync(string yamlContent, string? version = null, CancellationToken ct = default);

    /// <summary>
    /// 从 YAML 文件加载工作流定义并同步到 Engine 运行时。
    /// 一步完成：解析 YAML 文件 → 注册到 Registry → 同步到 Engine。
    /// </summary>
    /// <param name="filePath">YAML 文件路径</param>
    /// <param name="version">可选的版本号覆盖（默认从 YAML 中读取）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工作流名称</returns>
    Task<string> LoadAndApplyFromFileAsync(string filePath, string? version = null, CancellationToken ct = default);
}
