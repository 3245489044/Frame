
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Validation
{
    /// <summary>
    /// 闸A 规则：对领域模型做一次「声明集内可解析」校验，把违规写入诊断汇集器。
    /// <para>
    /// <b>领域无关</b> —— 规则只依赖内核诊断契约（<see cref="IConversionDiagnosticSink"/>），
    /// 不依赖任何具体领域类型；领域模型由各领域包的前端自行构造，内核不解释其内容。
    /// </para>
    /// <para>
    /// 约定：<b>不抛异常</b>（对齐「全量失败可见」原则，一条规则可报多条诊断）；
    /// <see cref="Name"/> 作为诊断归因/去重键。
    /// </para>
    /// </summary>
    /// <typeparam name="TModel">领域模型（如 Usharp 的空间/矩阵/流/引用/观测装配体）。</typeparam>
    public interface IGateRule<TModel>
    {
        /// <summary>规则名（诊断归因与去重键）。</summary>
        string Name { get; }

        /// <summary>执行校验；违规经 <paramref name="sink"/> 上报，不抛异常。</summary>
        void Check(TModel model, IConversionDiagnosticSink sink);
    }
}
