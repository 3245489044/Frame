
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Validation;

namespace Share.LogicComposition.Conversion.Packs
{
    /// <summary>
    /// 领域包契约：把一个 DSL 领域的「身份 + 诊断码段 + 闸A 规则集」绑定为可装配单元。
    /// <para>
    /// 内核只依赖本契约，不依赖任何具体领域。包的表层前端 / 词表白名单 / 发射器分别复用
    /// <c>ISourceFrontEnd&lt;TModel&gt;</c> / <c>OrderedWhitelist</c> / <c>IArtifactEmitter</c>，
    /// 本契约不重复定义它们（避免第二套实现）。
    /// </para>
    /// </summary>
    /// <typeparam name="TModel">领域模型（由本包前端构造）。</typeparam>
    public interface IDomainPack<TModel>
    {
        /// <summary>包标识（如 <c>usharp</c> / <c>nmd</c>）。</summary>
        string Id { get; }

        /// <summary>本包诊断码段（唯一真源声明，如 <c>US</c> / <c>NM</c>）。</summary>
        IReadOnlyList<string> DiagnosticCodePrefixes { get; }

        /// <summary>闸A 规则集（执行序 = 声明序）。</summary>
        IReadOnlyList<IGateRule<TModel>> GateRules { get; }
    }
}
