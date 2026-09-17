using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Spec;

namespace Strategy.DecisionRules
{
    /// <summary>① 变量→参数 归约与 ② 演进、③ 解码的便捷组合工具（薄脚手架，不新增裁决语义）。</summary>
    public static class Parametric
    {
        /// <summary>空归约：不写任何参数。</summary>
        public static readonly ParameterReducer NoReduce = _ => Array.Empty<ParameterWrite>();

        /// <summary>空演进：不写任何参数（用于纯归约/解码演示）。</summary>
        public static readonly ParameterTransfer NoTransfer = _ => Array.Empty<ParameterWrite>();

        /// <summary>
        /// 按「参数名 → 语义变量槽位」绑定构造归约算子：每步把 <paramref name="variables"/> 中该槽位的语义值，
        /// 经对应 <see cref="ParameterSpec.Coerce"/> 换算成 <see cref="ParameterWriteSource.Reduce"/> 写。
        /// </summary>
        public static ParameterReducer ReduceFromVariableMap(
            ParameterSchema schema,
            IReadOnlyDictionary<string, string> parameterToVariableKey)
        {
            if (schema is null) throw new ArgumentNullException(nameof(schema));
            if (parameterToVariableKey is null) throw new ArgumentNullException(nameof(parameterToVariableKey));
            return input =>
            {
                var writes = new List<ParameterWrite>();
                foreach (var (param, variableKey) in parameterToVariableKey)
                {
                    var spec = schema.TryGet(param);
                    if (spec is null) continue;
                    var raw = input.Variable(variableKey);
                    if (raw is null) continue; // 该步无此变量 → 不写（保默认/上一步值）
                    writes.Add(new ParameterWrite(param, spec.Coerce(raw), ParameterWriteSource.Reduce));
                }
                return writes;
            };
        }

        /// <summary>把多个归约算子顺序合成（各自的写并入同一批，统一经守卫施加）。</summary>
        public static ParameterReducer ComposeReduce(params ParameterReducer[] reducers)
            => input => reducers.SelectMany(r => r(input)).ToList();

        /// <summary>把多个演进算子顺序合成。</summary>
        public static ParameterTransfer ComposeTransfer(params ParameterTransfer[] transfers)
            => input => transfers.SelectMany(t => t(input)).ToList();

        /// <summary>把（可选）归约/演进/策略转成一次调用所需的委托形态。</summary>
        public static IParametricDecoder<TLabel> DecoderFrom<TLabel>(
            Func<ParameterState, int, ParametricDecision<TLabel>> decode)
        {
            if (decode is null) throw new ArgumentNullException(nameof(decode));
            return new FunctionDecoder<TLabel>(decode);
        }


        /// <summary>
        /// 把一个已装配的多维 <see cref="ParameterMatrix"/> 包装为 <see cref="ParameterTransfer"/>，
        /// 使其可作为 <see cref="ParametricKernel{TLabel}"/> 的演进算子（Engine 写源）做整向量执行。
        /// </summary>
        public static ParameterTransfer AsTransfer(ParameterMatrix matrix)
        {
            if (matrix is null) throw new ArgumentNullException(nameof(matrix));
            return input => matrix.ApplyTo(input);
        }


        private sealed class FunctionDecoder<TLabel> : IParametricDecoder<TLabel>
        {
            private readonly Func<ParameterState, int, ParametricDecision<TLabel>> _decode;
            public FunctionDecoder(Func<ParameterState, int, ParametricDecision<TLabel>> decode) => _decode = decode;
            public ParametricDecision<TLabel> Decode(ParameterState state, int step) => _decode(state, step);
        }
    }
}
