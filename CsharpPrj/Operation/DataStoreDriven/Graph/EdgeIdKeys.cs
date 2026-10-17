using Modules.Parsing.Keys;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用边键文法（K3 形式化）：<c>{sourceId}::{targetId}::{refType}</c>（分离符 <c>::</c>）。
/// <para>取代 <c>ReferenceGraphCoordinator.BuildEdgeId</c> 的手写拼串；构造恒经本单源文法。</para>
/// <para>注：边键非节点库键族，不注册进节点库注册表（避免与端点键族混用消歧）。</para>
/// </summary>
public static class EdgeIdKeys
{
    /// <summary>边键文法（族前缀缺省：仅用于构造/反解，不参与族归属消歧）。</summary>
    public static readonly KeyGrammar EdgeGrammar =
        KeyGrammar.Create("graph.edge", "{sourceId}::{targetId}::{refType}");

    /// <summary>构造边键（<c>{sourceId}::{targetId}::{refType}</c>）。</summary>
    public static string Build(string sourceId, string targetId, string refType)
        => EdgeGrammar.BuildLenient(sourceId, targetId, refType);

    /// <summary>反解边键（段数不符 / 空段 ⇒ false）。</summary>
    public static bool TryParse(string? edgeId, out string sourceId, out string targetId, out string refType)
    {
        sourceId = string.Empty;
        targetId = string.Empty;
        refType = string.Empty;
        if (string.IsNullOrWhiteSpace(edgeId)) return false;
        var parts = edgeId.Split("::", System.StringSplitOptions.None);
        if (parts.Length != 3) return false;
        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
            return false;
        sourceId = parts[0];
        targetId = parts[1];
        refType = parts[2];
        return true;
    }
}
