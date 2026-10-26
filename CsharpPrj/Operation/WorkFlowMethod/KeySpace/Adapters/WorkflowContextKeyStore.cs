using System;
using System.Collections.Generic;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace WorkFlowMethod.KeySpace.Adapters;

/// <summary>
/// 工作流参数键存储适配：<see cref="WfContext.Parameters"/> ⇄ 三态键读取。
/// 旧 <c>WorkflowContextParams.Get</c> 保持原样；本适配器只提供显式三态入口（P1 影子）。
/// </summary>
public sealed class WorkflowContextKeyStore : IKeyReader, IKeyWriter
{
    private readonly WfContext _context;

    public WorkflowContextKeyStore(WfContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public KeyValue<object?> Read(KeyRef key)
    {
        if (key.IsEmpty)
        {
            return KeyValue<object?>.NotApplicable(
                KeyReasonCodes.NotDeclared, "空键不参与读取", key);
        }

        if (_context.Parameters.TryGetValue(key.Value, out var value) && value is not null)
        {
            return KeyValue<object?>.Known(value);
        }

        return KeyValue<object?>.Unknown(
            KeyReasonCodes.DeclaredMissing, $"工作流参数未命中: {key.Value}", key);
    }

    /// <summary>
    /// 落库动作（A4：空键判定/诊断的唯一门 = <see cref="Modules.KeySpace.Runtime.KeyWriteGate"/>）。
    /// 生产调用必须经 KeyWriteGate.Write；直接调用本方法视为旁路。
    /// </summary>
    public KeyWriteResult Write(KeyRef key, object? value)
    {
        _context.Parameters[key.Value] = value;
        return new KeyWriteResult(KeyResolution.Resolved, null);
    }
}

/// <summary>
/// 会话字典键存储适配：<see cref="WorkFlowMethod.Execution.Session.DataContext"/> ⇄ 三态键读取。
/// <para>口径保持既有存储语义（键比较大小写不敏感）；本适配器不改写存储比较器。</para>
/// </summary>
public sealed class DataContextKeyStore : IKeyReader, IKeyWriter
{
    private readonly WorkFlowMethod.Execution.Session.DataContext _context;

    public DataContextKeyStore(WorkFlowMethod.Execution.Session.DataContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public KeyValue<object?> Read(KeyRef key)
    {
        if (key.IsEmpty)
        {
            return KeyValue<object?>.NotApplicable(
                KeyReasonCodes.NotDeclared, "空键不参与读取", key);
        }

        return _context.TryGet(key.Value, out var value) && value is not null
            ? KeyValue<object?>.Known(value)
            : KeyValue<object?>.Unknown(
                KeyReasonCodes.DeclaredMissing, $"会话数据未命中: {key.Value}", key);
    }

    /// <summary>落库动作（A4：空键门唯一 = <see cref="Modules.KeySpace.Runtime.KeyWriteGate"/>）。</summary>
    public KeyWriteResult Write(KeyRef key, object? value)
    {
        _context.Set(key.Value, value!);
        return new KeyWriteResult(KeyResolution.Resolved, null);
    }
}
