
using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Validation
{
    /// <summary>
    /// 闸A 执行器：按序运行规则集，把诊断汇集为 <see cref="ConversionResult{T}"/>（产物 = 传入模型）。
    /// <para>
    /// <b>领域无关的编排</b> —— 只负责「有序执行 + 全量收集 + 无异常」；
    /// 判据内容全在各 <see cref="IGateRule{TModel}"/> 内，本类不含任何领域知识。
    /// </para>
    /// </summary>
    /// <typeparam name="TModel">领域模型。</typeparam>
    public sealed class GateEngine<TModel>
    {
        private readonly IReadOnlyList<IGateRule<TModel>> _rules;

        public GateEngine(IEnumerable<IGateRule<TModel>> rules)
        {
            if (rules is null) throw new ArgumentNullException(nameof(rules));
            _rules = new List<IGateRule<TModel>>(rules);
        }

        /// <summary>规则集（执行序 = 构造序）。</summary>
        public IReadOnlyList<IGateRule<TModel>> Rules => _rules;

        /// <summary>
        /// 运行全部规则。诊断按「规则声明序 × 规则内上报序」稳定排列；
        /// 产物恒为传入模型（闸A 只判不改，对齐「本层只校验不执行」）。
        /// </summary>
        public ConversionResult<TModel> Run(TModel model)
        {
            var sink = new DiagnosticSink();
            for (var i = 0; i < _rules.Count; i++)
                _rules[i].Check(model, sink);

            return sink.HasErrors
                ? ConversionResult<TModel>.Failure(sink.Diagnostics, model)
                : ConversionResult<TModel>.Success(model, sink.Diagnostics);
        }
    }
}
