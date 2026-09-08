using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Pipeline.StepExec
{
    /// <summary>
    /// 一次步骤函数调用的执行上下文（不可变描述，中间件只读）。承载唯一执行走廊的入参语义：
    /// FuncName/NodeKey 供诊断与门控判定；<see cref="StepTimeout"/> / <see cref="Meta"/> 是各中间件的可插拔策略
    /// （缺省不生效 = 直跑，行为等价）。域专属语义（如「是否 LLM 步」）一律走 <see cref="Meta"/> 通用标签，
    /// 由使用层（Agent 等）中间件自行解释，内核不落入 Agent 域词汇表。
    /// </summary>
    public sealed record StepExecContext(string FuncName, string? NodeKey = null, int Round = 0)
    {
        /// <summary>单步超时策略（&gt;0 生效；null/0 = 不启用单步超时，走直跑，默认行为等价 → 可回退关闭）。</summary>
        public TimeSpan? StepTimeout { get; init; }

        /// <summary>机制中性的门控/横切元数据袋（string→object）。使用层中间件按需读专用键（如 LLM 门读 "requires_llm"）判定；内核不解释任何键。</summary>
        public IReadOnlyDictionary<string, object?>? Meta { get; init; }
    }

    /// <summary>
    /// 中间件签名（onion）。每个中间件：前置横切 → <c>await next(token)</c>（余下中间件 + 末端 handler）→ 后置横切。
    /// <paramref name="next"/> 携带被取代的 <see cref="CancellationToken"/>：中间件可注入 per-step linked-CTS（如单步超时）
    /// 使其真实传导到末端 handler，而非只能空转竞速。
    /// </summary>
    public delegate Task<object?> StepExecNext(CancellationToken token);

    /// <summary>中间件委托（固定签名，供 <see cref="StepExec"/> 按序组合）。</summary>
    public delegate Task<object?> StepExecMiddleware(
        StepExecContext ctx,
        Func<CancellationToken, Task<object?>> handler,
        StepExecNext next,
        CancellationToken ct);

    /// <summary>
    /// StepExec —— 唯一「步骤函数调用」执行走廊（单一路径目标入口，自 Agent.Core 迁入）：
    /// 把一次 handler 调用放入一条**顺序固定、可插拔**的中间件栈；空栈 = 直跑 handler（默认行为等价，零 handler 改动）。
    /// 横切（超时/门控/观测/错误归一）由此下沉为正交中间件，取代散在调用链内联的自成一体逻辑；内核自身不落入任何使用层域语义。
    /// </summary>
    public static class StepExec
    {
        /// <summary>沿中间件栈执行一次 handler 调用。缺省 middlewares（null/空）＝ 直接调用 handler（直跑，等价旧路径）。</summary>
        public static async Task<object?> InvokeAsync(
            StepExecContext ctx,
            Func<CancellationToken, Task<object?>> handler,
            CancellationToken ct,
            IReadOnlyList<StepExecMiddleware>? middlewares = null)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            ct.ThrowIfCancellationRequested();

            // 实参校验放到最前；栈形态稳定后按序递归组合。
            var stack = middlewares as StepExecMiddleware[] ?? middlewares?.ToArray() ?? Array.Empty<StepExecMiddleware>();

            // 末端 = 调用真实 handler（token 由沿途 next 注入的 per-step 取消源决定）。
            Task<object?> Run(int i, CancellationToken token) =>
                i >= stack.Length
                    ? handler(token)
                    : stack[i](ctx, handler, inner => Run(i + 1, inner), token);

            return await Run(0, ct).ConfigureAwait(false);
        }
    }
}