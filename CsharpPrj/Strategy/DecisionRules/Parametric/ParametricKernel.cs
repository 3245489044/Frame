using System;
using System.Collections.Generic;
using BasicMethod.Declarative.Spec;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 转移/归约算子的输入快照：某一步（step 序号）当前参数态 + 该步可读取的语义变量/信号。
    /// 纯函数语义（LLM-free）：给定 (当前参数态 + 输入)，决定对该步参数的写集。
    /// </summary>
    public readonly record struct ParametricStepInput(
        ParameterState State,
        int Step,
        IReadOnlyDictionary<string, object?> Variables)
    {
        /// <summary>读取该步某语义变量；缺失返回 null。</summary>
        public object? Variable(string key)
            => Variables is not null && Variables.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// ② 参数演进算子（内层计算）：纯函数 <c>参数态 + 步输入 → 参数写集</c>。
    /// 用于把「进度/预算单链推进」「意图达成合取」等裁决表达为参数上的确定性变换（消 if 堆叠）。
    /// </summary>
    public delegate IReadOnlyList<ParameterWrite> ParameterTransfer(ParametricStepInput input);

    /// <summary>
    /// ① 变量→参数 归约算子：把语义变量集按规则换算成参数写集（不做裁决，只落位参数值）。
    /// 典型实现是「参数绑定」：把语义槽位经 <see cref="ParameterSpec.Coerce"/> 落到对应参数。
    /// </summary>
    public delegate IReadOnlyList<ParameterWrite> ParameterReducer(ParametricStepInput input);

    /// <summary>③ 参数→标签 解码器（纯函数）：给定某步参数态，产出对外终值/继续裁决。领域中立，LLM-free。</summary>
    public interface IParametricDecoder<TLabel>
    {
        /// <summary>解码：<see cref="ParametricDecision{TLabel}.IsTerminal"/> = true 时终止闭环并取 <see cref="ParametricDecision{TLabel}.Label"/>。</summary>
        ParametricDecision<TLabel> Decode(ParameterState state, int step);
    }

    /// <summary>解码结果：Label（对外标签）+ IsTerminal（是否终止闭环）。</summary>
    public readonly record struct ParametricDecision<TLabel>(TLabel Label, bool IsTerminal)
    {
        /// <summary>继续（非终止）的便捷构造。</summary>
        public static ParametricDecision<TLabel> Continue(TLabel label) => new(label, false);
        /// <summary>终止的便捷构造。</summary>
        public static ParametricDecision<TLabel> Terminal(TLabel label) => new(label, true);
    }

    /// <summary>一次闭环运行的产出：终值标签 + 最终参数态 + 步数 + 被拒绝的越权写 + 每步轨迹。</summary>
    public sealed record ParametricRun<TLabel>(
        TLabel Label,
        bool Terminated,
        ParameterState FinalState,
        int Steps,
        IReadOnlyList<string> Rejected,
        IReadOnlyList<ParameterState> Trace)
    {
        /// <summary>全程是否存在被拒绝的越权写（Reduce/Policy 企图写强制分量）。</summary>
        public bool HasRejectedWrites => Rejected is { Count: > 0 };
    }

    /// <summary>
    /// 参数化决策内核（本模块地基）：把「决策/混合调度」表达为一次「①变量→参数 → ②参数演进闭环 → ③参数→标签解码」。
    /// <para>核心性质：内部只有对参数态的一次次确定性变换（无 if 时序分支、无外部导演回调旁路）；
    /// 外部（LLM/Config）仅在 <see cref="ParameterWriteSource.Reduce"/>（归约）或 <see cref="ParameterWriteSource.Policy"/>（提议）两条
    /// 非强制写路径上供值，永不触碰 <see cref="ParameterSpec.IsForced"/> 确定性分量（由状态机自身守卫）。</para>
    /// </summary>
    public sealed class ParametricKernel<TLabel>
    {
        private readonly ParameterSchema _schema;
        private readonly int _maxSteps;

        public ParametricKernel(ParameterSchema schema, int maxSteps = 64)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _maxSteps = maxSteps > 0 ? maxSteps : throw new ArgumentOutOfRangeException(nameof(maxSteps));
        }

        public ParameterSchema Schema => _schema;

        /// <summary>
        /// 跑一段参数闭环：从初始参数态出发，逐轮 ②演进 → ③解码，直到解码给终值或达到 <see cref="_maxSteps"/>。
        /// <paramref name="transfer"/>：内层参数演进（引擎确定性写为主，唯一允许触碰强制分量——若写强制须以 Engine 来源）。
        /// <paramref name="reducer"/>：每步 ①变量→参数 归约（可选；写强制会被守卫拒绝）。
        /// <paramref name="policy"/>：每步外部策略提议写（可选；写强制会被守卫拒绝）。
        /// 全部写统一经 <see cref="ParameterState.Apply"/> 施加，越权写计入返回的 <see cref="ParametricRun{TLabel}.Rejected"/>。
        /// </summary>
        public ParametricRun<TLabel> Run(
            IReadOnlyDictionary<string, object?> variables,
            ParameterTransfer transfer,
            ParameterReducer? reducer = null,
            IParametricDecoder<TLabel> decoder = null!,
            ParameterTransfer? policy = null,
            int? maxSteps = null)
        {
            if (transfer is null) throw new ArgumentNullException(nameof(transfer));
            if (decoder is null) throw new ArgumentNullException(nameof(decoder));

            var steps = maxSteps ?? _maxSteps;
            var state = _schema.CreateDefault();
            var trace = new List<ParameterState> { state };
            var rejectedAll = new List<string>();
            var outcome = ParametricDecision<TLabel>.Continue(default!);

            for (var step = 0; step < steps; step++)
            {
                var input = new ParametricStepInput(state, step, variables ?? EmptyVars);

                // ① 归约写（可选）
                if (reducer is not null)
                {
                    state = state.Apply(reducer(input), out var rj); rejectedAll.AddRange(rj);
                }
                // ② 内层演进（引擎确定性写；唯一允许写强制分量）
                state = state.Apply(transfer(input), out var te); rejectedAll.AddRange(te);
                // 外部策略提议（可选）
                if (policy is not null)
                {
                    var policyInput = new ParametricStepInput(state, step, variables ?? EmptyVars);
                    state = state.Apply(policy(policyInput), out var pj); rejectedAll.AddRange(pj);
                }

                trace.Add(state);

                // ③ 解码
                outcome = decoder.Decode(state, step + 1);
                if (outcome.IsTerminal) break;
            }

            return new ParametricRun<TLabel>(
                outcome.Label,
                outcome.IsTerminal,
                state,
                trace.Count - 1,
                rejectedAll,
                trace);
        }

        private static readonly IReadOnlyDictionary<string, object?> EmptyVars =
            new Dictionary<string, object?>();
    }
}
