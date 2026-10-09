using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.ReferenceSchema;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Schema;
using Usharp.Adapters.UniversalReference;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 引用类型词表的**USharp 单一真源**入口（B）：由 USharp 编译产物构建
/// <see cref="ReferenceTypeRegistry"/>（复用 <see cref="UsharpReferenceSchemaEmitter"/>，不重建解析/校验）。
/// <para>
/// 收敛目标：`reference-schema.dsl.md` 的 `[Node]/[Edge]/[Constraint]` 与 `.ushar` 同语言、同真源；
/// 运行期装配 <c>ReferenceGraphCoordinator</c> 的词表（B3 词表武装）改由本入口产出，消除「两套词表 DSL 并存」。
/// </para>
/// <para>本类零领域逻辑：只把「编译产物 → 既有注册中心」这一步固定为单点（禁各装配处各写一遍）。</para>
/// </summary>
public static class DeclaredReferenceVocabulary
{
    /// <summary>由 USharp 编译产物构建引用类型注册中心（词表权威 = 编译产物）。</summary>
    public static ReferenceTypeRegistry Build(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return new ReferenceTypeRegistry(UsharpReferenceSchemaEmitter.Emit(compilation));
    }

    /// <summary>Schema 面校验（悬空边端节点 / 悬空约束 / 重复 / dep 目标）——复用既有判据。</summary>
    public static IReadOnlyList<ValidationIssue> Validate(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return UsharpReferenceSchemaEmitter.Validate(compilation);
    }

    /// <summary>
    /// **端到端**：声明库（文件库 / 数据库库）→ 编译 → 引用类型注册中心（E：词表由 USharp 入库声明装配）。
    /// <para>收敛目标：<c>ReferenceGraphCoordinator</c> 的词表武装（B3）由本入口产出，取代内嵌 JSON 产物路径
    /// ——词表真源唯一 = 库中的 USharp 声明。</para>
    /// </summary>
    public static async Task<ReferenceTypeRegistry> BuildFromStoreAsync(IDeclarationStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var sources = await UsharpSourceLoader.FromStoreAsync(store, ct).ConfigureAwait(false);
        return Build(UsharpCompilation.CompileSources(sources));
    }
}
