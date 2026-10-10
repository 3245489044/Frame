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
}
