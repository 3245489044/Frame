using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Stages;

/// <summary>
/// 门控阶段装配管线 —— 按注册顺序依次执行各阶段；
/// CanExecute 为 false 的阶段被跳过。阶段注册序即装配语义（位置即语义）。
/// </summary>
/// <typeparam name="TContext">装配上下文。</typeparam>
public sealed class AssemblyStagePipeline<TContext> where TContext : class
{
    private readonly IReadOnlyList<IAssemblyStage<TContext>> _stages;

    public AssemblyStagePipeline(IEnumerable<IAssemblyStage<TContext>> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = new List<IAssemblyStage<TContext>>(stages);
    }

    /// <summary>构造便捷工厂（params 形式）。</summary>
    public static AssemblyStagePipeline<TContext> From(params IAssemblyStage<TContext>[] stages)
        => new(stages);

    /// <summary>顺序执行全部门控阶段。</summary>
    public async Task RunAsync(TContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var stage in _stages)
        {
            if (!stage.CanExecute(context)) continue;
            ct.ThrowIfCancellationRequested();
            await stage.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
    }
}
