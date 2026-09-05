using ECS.Core;
using CommandFramework.Extensions;
using CommandFramework.Utilities;

namespace CommandFramework.Execution.Commands;

/// <summary>
/// 命令系统契约（下沉自 BackEndDriven.CommandExecution，单一实现权威——[CR: axioms:Strategy.SingleImplementation@consume]）。
/// 命令处理器按 HandledActions 注册分发；Execute 由协调器在命令边界调用。
/// </summary>
public interface ICommandSystem
{
    IReadOnlyCollection<string> HandledActions { get; }

    void Execute(Entity entity, CommandMetaComponent meta);
}

/// <summary>
/// 可选异步命令系统：实现者由协调器以 await 方式执行，避免同步阻塞命令线程
/// （典型场景：exec 调用长时间运行的异步函数，如 Cline 池预热）。
/// </summary>
public interface IAsyncCommandSystem
{
    Task ExecuteAsync(Entity entity, CommandMetaComponent meta);
}

/// <summary>
/// 命令系统基类（下沉自 BackEndDriven.CommandExecution）：
/// 统一 SetSuccess/SetFailure 状态回写、配置路径解析与读取模板。
/// 模块日志经 <see cref="CommandLog"/> 注入（默认 null 跳过），不依赖具体日志门面。
/// </summary>
public abstract class CommandSystemBase : ICommandSystem
{
    protected readonly EntityManager EntityManager;

    protected CommandSystemBase(EntityManager entityManager, params string[] actions)
    {
        EntityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        HandledActions = actions;
    }

    public IReadOnlyCollection<string> HandledActions { get; }

    public abstract void Execute(Entity entity, CommandMetaComponent meta);

    /// <summary>命令事件日志回调（如 ReadConfigFile 的配置读取日志）。由使用方注入模块日志门面；null 时跳过。</summary>
    protected Action<string, object?>? CommandLog { get; set; }

    protected void SetSuccess(Entity entity, string? resultValue = null, CommandOutcome outcome = CommandOutcome.Ok, string? target = null)
    {
        EntityManager.SetComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Success
        });
        EntityManager.AddOrSetComponent(entity, new CommandResultComponent
        {
            ResultValue = resultValue,
            CompletedAt = DateTime.UtcNow,
            Outcome = outcome,
            Target = target
        });
    }

    protected void SetFailure(Entity entity, string errorMessage, CommandOutcome outcome = CommandOutcome.Error, string? target = null)
    {
        EntityManager.SetComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Failed,
            ErrorMessage = errorMessage
        });
        EntityManager.AddOrSetComponent(entity, new CommandResultComponent
        {
            CompletedAt = DateTime.UtcNow,
            Outcome = outcome,
            Target = target
        });
    }

    private static string? FindProjectRootViaLocator()
    {
        try
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var root = ProjectRootLocator.FindRootUpward(start);
                if (root != null)
                    return root;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    protected static string? ResolveConfigPath(string target, string subFolder = "")
    {
        var projectRoot = FindProjectRootViaLocator();
        if (projectRoot == null)
            return null;

        if (string.IsNullOrWhiteSpace(target))
            return null;

        var basePath = string.IsNullOrEmpty(subFolder)
            ? Path.Combine(projectRoot, "FileDriven", "DrivenConfig")
            : Path.Combine(projectRoot, "FileDriven", "DrivenConfig", subFolder);

        var fullPath = Path.GetFullPath(Path.Combine(basePath, target));
        if (File.Exists(fullPath))
            return fullPath;

        // JsonConfig 兜底：UI 配置约定存放于 DrivenConfig/JsonConfig（boot/configLoader 同源）。
        // sideload/load 缺省 subFolder 时，目标若位于 JsonConfig 也应解析成功——此前仅解析 DrivenConfig 根，
        // 导致 sideload AgentRuntimeUI.json 等 JsonConfig 配置静默 SetFailure「配置文件不存在」、完全不渲染。
        if (string.IsNullOrEmpty(subFolder))
        {
            var jsonPath = Path.GetFullPath(Path.Combine(projectRoot, "FileDriven", "DrivenConfig", "JsonConfig", target));
            if (File.Exists(jsonPath))
                return jsonPath;
        }

        return Path.GetFullPath(Path.Combine(projectRoot, target));
    }

    /// <summary>
    /// 公共"配置读取"模板：空目标检查 → 路径解析 → 文件存在检查 → 读取。
    /// 收敛 Sideload/Load 的重复 try/catch + SetFailure/SetSuccess 包装模式。
    /// </summary>
    protected void ReadConfigFile(
        Entity entity,
        string? target,
        string subFolder,
        string commandName,
        string logEventName)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, $"{commandName}: 缺少配置文件路径");
            return;
        }

        try
        {
            var fullPath = ResolveConfigPath(target, subFolder);
            if (fullPath == null)
            {
                SetFailure(entity, $"{commandName}: 无法定位项目根，无法解析配置文件路径 '{target}'");
                return;
            }
            if (!File.Exists(fullPath))
            {
                var scopeDesc = string.IsNullOrEmpty(subFolder)
                    ? "DrivenConfig"
                    : $"DrivenConfig\\{subFolder}";
                SetFailure(entity, $"{commandName}: 配置文件不存在 '{fullPath}'（'{target}' 无解析结果或不在 {scopeDesc} 下）");
                return;
            }

            var json = File.ReadAllText(fullPath);
            CommandLog?.Invoke(logEventName, new { target, fullPath });
            SetSuccess(entity, json);
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"{commandName} 失败: {ex.Message}");
        }
    }
}
