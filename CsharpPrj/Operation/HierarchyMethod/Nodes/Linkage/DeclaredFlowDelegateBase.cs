using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Linkage;

/// <summary>
/// 声明流转发**模板基类**（DUP-1/DUP-2/ERR-8 收敛）—— 把每个调用点各写一遍的四件事
/// （**空键口径 + 入口未装配诊断 + inports 组装 + 结论回报**）收到一处，子类只声明「哪条流 + 什么入参」。
/// <para>
/// 分层：本基类只做「端口调用 → 声明流执行」的转发，不含顺序 / 分支 / 重试（O-原则2）；
/// 编排真源恒为声明面 .ushar 的 [Flow]；诊断经构造注入 <see cref="IDiagnosticsSink"/>（框架不感知应用层日志门面）。
/// </para>
/// </summary>
public abstract class DeclaredFlowDelegateBase
{
    private readonly IDeclaredFlowRunner? _runner;
    private readonly IDiagnosticsSink _diagnostics;

    /// <param name="runner">声明流执行器；null = 未装配（调用时可见化并回报未完成）。</param>
    /// <param name="diagnostics">诊断端口（缺省空端口 = 不记）。</param>
    protected DeclaredFlowDelegateBase(IDeclaredFlowRunner? runner, IDiagnosticsSink? diagnostics = null)
    {
        _runner = runner;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    /// <summary>执行器是否已装配（供组合根 / 自检诊断）。</summary>
    public bool IsAttached => _runner is not null;

    /// <summary>
    /// 统一驱动：入口未装配 ⇒ FlowOutcome.Missing（可见化，禁静默）；否则跑声明流并按结果回报。
    /// </summary>
    /// <param name="entry">簿记流数据行（文件名 / 流号 / 标签）。</param>
    /// <param name="inports">入参表（键 = 声明面 inports=；null 值不入参，等价「未给」）。</param>
    /// <param name="ct">取消令牌。</param>
    protected async Task<FlowOutcome> RunAsync(
        DeclaredFlowEntry entry, IReadOnlyDictionary<string, object?> inports, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(inports);

        if (_runner is null)
        {
            _diagnostics.Report("BookkeepingFlow_runner_missing", new
            {
                file = entry.FileName,
                flow = entry.FlowName,
                label = entry.Label,
                code = DiagnosticsCodes.FlowMissing,
            });
            return FlowOutcome.Missing($"{entry.Label}：OneShotFlowRunner 未装配（{entry.FileName}）");
        }

        var result = await _runner.RunAsync(entry.FileName, inports, ct).ConfigureAwait(false);
        return result.Ok
            ? FlowOutcome.Committed()
            : FlowOutcome.Failed($"{entry.Label}：声明流 {entry.FlowName} 执行失败（step={result.ErrorStep}）");
    }

    /// <summary>组装 inports：null 值不入参（等价「未给」），键序 = 声明面 inports= 书写序。</summary>
    /// <param name="items">(键, 值) 序列。</param>
    protected static Dictionary<string, object?> Inports(params (string Key, string? Value)[] items)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in items)
        {
            if (value is not null)
                map[key] = value;
        }

        return map;
    }
}
