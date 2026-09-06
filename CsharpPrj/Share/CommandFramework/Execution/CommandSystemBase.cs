using ECS.Core;
using CommandFramework.Extensions;

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

    /// <summary>
    /// 解析配置文件路径（经 Route 单一真源：资源统一锚定 <c>Projects\Information\DrivenConfig</c>）。
    /// <paramref name="subFolder"/> 为 DrivenConfig 下的子目录（如 "JsonConfig"/"WorkFlow"/"Templates"）；空则直接在 DrivenConfig 根下查找。
    /// 支持：绝对路径已存在 → 直接返回；绝对 / 相对路径经 DrivenConfig（+subFolder）组合 → JsonConfig 兜底 → 返回最终候选。
    /// 兼容旧脚本的相对路径形态（如 "..\..\DrivenConfig\JsonConfig\X.json"，Path.GetFullPath 归一后同样落在 JsonConfig）。
    /// </summary>
    protected static string? ResolveConfigPath(string target, string subFolder = "")
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        try
        {
            // 绝对路径已存在 → 直接命中（历史调用方可能直接传绝对路径）。
            if (File.Exists(target))
                return Path.GetFullPath(target);

            // ★ 2026-09-06：经 Route.InformationPaths 取资源仓根，替代旧 projectRoot\FileDriven\DrivenConfig 硬编码
            //（该目录已迁移至 Projects\Information\DrivenConfig，旧解析对裸配置名必落空 → load 报「配置文件不存在」）。
            var drivenConfig = Route.InformationPaths.DrivenConfigDir;
            var basePath = string.IsNullOrEmpty(subFolder)
                ? drivenConfig
                : Path.Combine(drivenConfig, subFolder);

            var fullPath = Path.GetFullPath(Path.Combine(basePath, target));
            if (File.Exists(fullPath))
                return fullPath;

            // JsonConfig 兜底：UI 配置约定存放于 DrivenConfig/JsonConfig（boot/configLoader 同源）。
            // sideload/load 缺省 subFolder 时，目标若位于 JsonConfig 也应解析成功。
            if (string.IsNullOrEmpty(subFolder))
            {
                var jsonPath = Path.GetFullPath(Path.Combine(Route.InformationPaths.JsonConfigDir, target));
                if (File.Exists(jsonPath))
                    return jsonPath;
            }

            // 返回最终候选（可能不存在），交由调用方 File.Exists 判失败并报明细路径（便于定位解析结果落点）。
            return fullPath;
        }
        catch
        {
            return null;
        }
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
