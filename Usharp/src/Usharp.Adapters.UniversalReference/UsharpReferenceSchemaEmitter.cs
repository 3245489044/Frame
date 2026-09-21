using System.Text.Json;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Schema;
using Usharp.Semantics;

namespace Usharp.Adapters.UniversalReference;

/// <summary>
/// 引用 Schema 产出：<c>[Node]</c>/<c>[Edge]</c>/<c>[Constraint]</c> 声明 →
/// <see cref="ReferenceSchema"/>（复用既有 <see cref="ReferenceSchemaRegistry"/>，不重建解析/校验）。
/// <para>词表单一真源 = 书写面（Usharp 声明）；本产物为**编译产出**（禁止手改）；
/// 编译期只产 Schema / 边集快照，**不写库**（写库由宿主经 <see cref="UsharpReferenceStoreWriter"/> 显式触发）。</para>
/// </summary>
public static class UsharpReferenceSchemaEmitter
{
    /// <summary>建注册中心（装载全部文档并提取节点/边/约束）。</summary>
    public static ReferenceSchemaRegistry BuildRegistry(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var registry = new ReferenceSchemaRegistry();
        registry.Load(compilation.Documents);
        return registry;
    }

    /// <summary>编译为可序列化 Schema（运行时消费的单一产物形态）。</summary>
    public static ReferenceSchema Emit(UsharpCompilation compilation)
        => BuildRegistry(compilation).ToSchema();

    /// <summary>Schema 面校验（悬空边端节点 / 悬空约束 / 重复 / dep 目标）——复用既有判据。</summary>
    public static IReadOnlyList<ValidationIssue> Validate(UsharpCompilation compilation)
        => BuildRegistry(compilation).Validate();

    /// <summary>Schema → JSON（编译产物落盘用；缩进默认开启以便 diff）。</summary>
    public static string ToJson(ReferenceSchema schema, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = indented });
    }
}
