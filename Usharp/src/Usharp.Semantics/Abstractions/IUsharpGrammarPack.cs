using System.Collections.Generic;
using Plate.DslMethod;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 语法包契约：领域语言面的**完整所有权单元**（在 <see cref="IUsharpDomainPack"/> 之上追加
/// 「前缀 / 符号种类」两项语法所有权）。
/// <para>
/// 编译作用域 = 框架核心前缀 ∪ 已装载包的 <see cref="Prefixes"/>：未装载某包 ⇒ 其前缀不进入作用域 ⇒
/// 对应声明行按 <c>US0013</c> 报错（不静默丢声明）。
/// </para>
/// <para>非语法包（只挂 facet provider / 闸规则）可继续只实现 <see cref="IUsharpDomainPack"/>。</para>
/// </summary>
public interface IUsharpGrammarPack : IUsharpDomainPack
{
    /// <summary>本包私有前缀（核心不感知；未装载 ⇒ 这些前缀不被识别）。</summary>
    IReadOnlyList<DslPrefixDef> Prefixes { get; }

    /// <summary>本包消费的符号种类（供依赖分析 / 冲突检测）。</summary>
    IReadOnlyList<DslSymbolKind> Kinds { get; }
}
