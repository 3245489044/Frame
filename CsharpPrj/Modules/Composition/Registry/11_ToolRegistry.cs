using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Registry;

/// <summary>
/// 工具来源域 —— 内/外二分契约（工具调用的两种路由，统一建模为 <see cref="ToolSpec"/>）：
/// <b>Internal</b>：平台自身能力（建任务/写图/查询/决策函数等），进程内经工作流/节点执行（NodeExecutor + WorkflowContext），不经网络；
/// <b>External</b>：跨进程/跨网的外部能力（文件系统、终端、代码库、web、外部服务等），经 MCP 桥（MCPBridge）转发。
/// 二者都收敛为 <see cref="ToolSpec"/>，只差 <paramref name="ToolSpec.ExecuteAsync"/> 的路由实现（装配期注入、装配期校验）。
/// </summary>
public enum ToolKind
{
    /// <summary>内部能力：进程内直调度（工作流节点执行）。</summary>
    Internal = 0,

    /// <summary>外部能力：经 MCP 桥转发。</summary>
    External = 1,
}

/// <summary>
/// 工具规范 —— LLM-free 的统一工具声明（与 Engine/Agent 无关）。
/// <see cref="Name"/>/<see cref="Description"/>/<see cref="InputSchema"/> 供工具选择面（LLM/规则）使用；
/// <see cref="ExecuteAsync"/> 为执行器（入参 = 决策引擎/LLM 产出的 arguments JSON，返回 = 结果文本/JSON）；
/// <see cref="Route"/> 为装配期校验路由标识（内部 = 工作流节点键，外部 = MCP 桥目标），供 <see cref="ToolRegistry.Validate"/> 校验存在性。
/// </summary>
public sealed record ToolSpec(
    string Name,
    string Description,
    ToolKind Kind,
    Func<string, CancellationToken, Task<string>> ExecuteAsync,
    JsonElement? InputSchema = null,
    string? Route = null)
{
    /// <summary>装配期校验是否齐备（Name 非空、执行器非空、Route 非空）。</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Description)
        && ExecuteAsync is not null
        && !string.IsNullOrWhiteSpace(Route);
}

/// <summary>
/// 工具注册表 —— 复用 <see cref="MapRegistryBase{TKey,TValue}"/> 的唯一工具真源（单实例约定）。
/// 声明即校验：每个工具以 <see cref="ToolSpec"/> 注册（内/外二分），装配期经 <see cref="Validate"/> 检查齐备性，
/// 取代"在调用点手工拼接 tools"的散点透传（对齐 R3/R4：工具可用性单一注册、装配期校验，缺失显式告警不回退）。
/// 注册采用反射安全的 <see cref="TryRegisterTool"/> 模式：模块不可用时返回 false，不抛、不崩。
/// </summary>
public sealed class ToolRegistry : MapRegistryBase<string, ToolSpec>
{
    /// <summary>全局单实例（装配期/组合根注入；如需 DI 可另经容器注册同一类型）。</summary>
    public static ToolRegistry Instance { get; } = new ToolRegistry();

    /// <summary>构造：大小写不敏感键、禁止覆盖（重复注册=装配错误，显式暴露而非静默覆盖）。</summary>
    public ToolRegistry()
        : base(StringComparer.OrdinalIgnoreCase, allowOverwrite: false)
    {
    }

    /// <summary>
    /// 反射安全注册（对齐项目 TryRegister* 惯例）：已存在（含大小写不敏感撞键）或注册调用抛异常 → false，
    /// host/模块装配失败不崩溃；成功 → true。</summary>
    /// <param name="name">工具名（键）。</param>
    /// <param name="spec">工具规范。</param>
    public bool TryRegisterTool(string name, ToolSpec spec)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(spec);
        if (Contains(name)) return false;
        try
        {
            Register(name, spec);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 装配期校验：返回所有不齐备/缺失路由的工具名（<see cref="ToolSpec.IsComplete"/> 为 false 者）。
    /// 空列表 = 全部齐备。校验只读，不抛（供组合根装配后 fail-fast 显式告警）。</summary>
    public IReadOnlyList<string> Validate()
    {
        var missing = new List<string>();
        foreach (var key in GetKeys())
        {
            if (!TryGet(key, out var spec) || spec is null || !spec.IsComplete)
                missing.Add(key);
        }
        return missing;
    }
}