using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>组合根装配相位；枚举声明序 = 执行序（顺序即语义）。</summary>
    public enum AssemblyPhase
    {
        /// <summary>引导外部承载/单根（先于一切绑定与解析）。</summary>
        Boot,

        /// <summary>绑定承载并冻结（必须先于任何槽解析）。</summary>
        Bind,

        /// <summary>段装配（按段注册序执行）。</summary>
        Assemble,

        /// <summary>装配产物发布。</summary>
        Publish,
    }

    /// <summary>
    /// 相位编排器：按相位序（枚举声明序）执行注册步骤；同实例并发调用互斥（<see cref="SemaphoreSlim"/>(1,1) 串行门，
    /// 语义由本类内建，调用方不再自持门）。
    /// <para>异常原样上抛（不吞、不降级）；串行门在 <c>finally</c> 释放，取消/失败路径不泄漏。</para>
    /// <para>通用件：零领域类型（步骤一律由调用方以委托注入）。</para>
    /// </summary>
    /// <typeparam name="TCtx">相位状态载体（跨相位传递数据的可变载体）。</typeparam>
    public sealed class AssemblyPhaseRunner<TCtx> where TCtx : class
    {
        private static readonly AssemblyPhase[] PhaseOrder = Enum.GetValues<AssemblyPhase>();

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<AssemblyPhase, List<Func<TCtx, CancellationToken, Task>>> _steps = new();
        private int _stepCount;

        /// <summary>登记某相位的步骤；同相位多步骤按登记序执行（相位之间按枚举声明序）。</summary>
        public AssemblyPhaseRunner<TCtx> On(AssemblyPhase phase, Func<TCtx, CancellationToken, Task> step)
        {
            ArgumentNullException.ThrowIfNull(step);

            if (!_steps.TryGetValue(phase, out var list))
                _steps[phase] = list = new List<Func<TCtx, CancellationToken, Task>>();
            list.Add(step);
            _stepCount++;
            return this;
        }

        /// <summary>已注册步骤数（诊断）。</summary>
        public int StepCount => _stepCount;

        /// <summary>串行执行全相位（<see cref="SemaphoreSlim"/>(1,1)）；异常原样上抛（不吞、不降级）。</summary>
        public async Task RunAsync(TCtx ctx, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var phase in PhaseOrder)
                {
                    if (!_steps.TryGetValue(phase, out var steps)) continue;
                    foreach (var step in steps)
                    {
                        ct.ThrowIfCancellationRequested();
                        await step(ctx, ct).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
