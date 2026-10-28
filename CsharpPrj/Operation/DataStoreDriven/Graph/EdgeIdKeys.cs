namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用边键兼容门面：构造/反解一律转发 <see cref="IGraphKeyGrammar"/>（唯一键文法实现 = <see cref="DeclaredGraphKeyGrammar"/>）。
/// <para>保留本门面仅为既有消费方零改动；新代码请直接使用 <see cref="IGraphKeyGrammar"/>。</para>
/// </summary>
public static class EdgeIdKeys
{
    /// <summary>构造边键（<c>{sourceId}::{targetId}::{refType}</c>）。</summary>
    public static string Build(string sourceId, string targetId, string refType)
        => DeclaredGraphKeyGrammar.Instance.Edge(sourceId, targetId, refType, EdgeIdentity.SourceTargetRef);

    /// <summary>反解边键：支持 <c>Storage</c>（<c>::</c>）与 <c>Legacy</c>（<c>U+0001</c>）两 surface。</summary>
    public static bool TryParse(string? edgeId, out string sourceId, out string targetId, out string refType)
    {
        sourceId = string.Empty;
        targetId = string.Empty;
        refType = string.Empty;
        if (!DeclaredGraphKeyGrammar.Instance.TryParseEdge(edgeId, out var parsed)) return false;
        sourceId = parsed.SourceKey;
        targetId = parsed.TargetKey;
        refType = parsed.RefType;
        return true;
    }

    /// <summary>是否遗留形态（含 <c>U+0001</c>；供迁移/DryRun 报告）。</summary>
    public static bool IsLegacy(string? edgeId) => DeclaredGraphKeyGrammar.Instance.IsLegacyEdge(edgeId);
}
