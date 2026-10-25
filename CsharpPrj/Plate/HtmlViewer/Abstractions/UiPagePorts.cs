#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>
/// 取数端口（M5）：页面意图 → 数据模型。框架侧零领域词——查询/排序/权限/错误分类全部留在实现内，
/// 工作流节点（<c>Ui_Gather</c>）只经本端口取数，不直连领域服务。
/// </summary>
public interface IGatherPort
{
    /// <summary>按意图取数；<paramref name="payload"/> 为事件负载原文，返回值供 <see cref="IProjectPort"/> 投影。</summary>
    Task<object?> GatherAsync(string intent, string payload, CancellationToken ct);
}

/// <summary>
/// 投影端口（M5）：取数模型 → <see cref="RenderOutput"/>（写回计划/文本）。实现侧只做适配，不复制领域逻辑。
/// </summary>
public interface IProjectPort
{
    /// <summary>按意图投影；模型为 null ⇒ 返回 <see cref="RenderOutput.Skip"/>（不写）。</summary>
    RenderOutput Project(string intent, object? model);
}

/// <summary>
/// 页面取数/投影端口的组合根登记点（M5）：组合根注入一次；未登记 ⇒ 工作流节点走既有实现（零回归）。
/// <para>诊断：按意图累计 <c>Ui_Gather</c> 调用次数（声明值 vs 实际值对拍载体），经 <see cref="GatherCallSnapshot"/> 读取。</para>
/// </summary>
public static class UiPagePorts
{
    private static readonly ConcurrentDictionary<string, int> GatherCalls = new(StringComparer.Ordinal);
    private static IGatherPort? _gather;
    private static IProjectPort? _project;

    /// <summary>已装配的取数端口；null = 未装配（调用方回退既有分发）。</summary>
    public static IGatherPort? Gather => Volatile.Read(ref _gather);

    /// <summary>已装配的投影端口；null = 未装配（调用方直接透传取数产物）。</summary>
    public static IProjectPort? Project => Volatile.Read(ref _project);

    /// <summary>装配端口（幂等；null 表示保持未装配/清除该端口）。</summary>
    public static void Attach(IGatherPort? gather, IProjectPort? project)
    {
        Volatile.Write(ref _gather, gather);
        Volatile.Write(ref _project, project);
    }

    /// <summary>登记一次取数调用（诊断账；按意图分账）。</summary>
    public static void RecordGather(string? intent)
        => GatherCalls.AddOrUpdate(string.IsNullOrWhiteSpace(intent) ? "?" : intent!, 1, static (_, count) => count + 1);

    /// <summary>取数调用快照（意图 → 次数；只读副本）。</summary>
    public static IReadOnlyDictionary<string, int> GatherCallSnapshot()
        => new Dictionary<string, int>(GatherCalls, StringComparer.Ordinal);

    /// <summary>清空诊断账（测试/批次边界用）。</summary>
    public static void ResetGatherCalls() => GatherCalls.Clear();
}
