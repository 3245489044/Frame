using Modules.Node.Materialization;
using Modules.Node.Read;

namespace Share.NodeIo;

/// <summary>
/// **节点文档读写唯一门面**：读/写共用同一作用域解析、位置键求值、载荷契约与诊断词表；
/// 差异仅在最后一步（<c>carrier.UpsertAsync</c> vs <c>carrier.ReadAsync/EnumerateKeysAsync</c> + 投影）。
/// </summary>
public interface INodeDocumentPort
{
    /// <summary>读：loc → 位置集 → 位置键 → 载体读 → 选择/投影（空必带原因）。</summary>
    Task<NodeReadResult> QueryAsync(NodeDocumentQuery query, CancellationToken cancellationToken = default);

    /// <summary>写：loc → 位置集 → 位置键 → 载荷校验 → 单写门 → 载体 upsert/patch/delete。</summary>
    Task<NodeMaterializationResult> ApplyAsync(NodeDocumentChange change, CancellationToken cancellationToken = default);

    /// <summary>Stream 会话（Begin/Append/Commit）；提交走同一 <see cref="ApplyAsync"/>。</summary>
    INodeStreamSession BeginStream(NodeDocumentScope scope, int round);

    /// <summary>整键 → 作用域（声明反解；未命中 ⇒ false + 原因，禁回退默认载体）。</summary>
    bool TryResolveByKey(string domain, string key, out NodeDocumentScope scope, out string reason);
}
