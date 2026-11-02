namespace Share.NodeIo;

/// <summary>文档变更模式（写侧唯一枚举）。</summary>
public enum NodeChangeMode
{
    /// <summary>整文档写入（幂等 upsert）。</summary>
    Upsert,

    /// <summary>字段级补丁（锁内 read → patch → upsert）。</summary>
    Patch,

    /// <summary>整文档追加语义（Stream 帧追加请走 <see cref="INodeStreamSession"/>；此处等价 Upsert）。</summary>
    Append,

    /// <summary>按键删除。</summary>
    Delete,
}

/// <summary>
/// 节点文档变更请求：<see cref="PayloadJson"/> 为不透明 JSON（语言无关），
/// 由语言适配层（ushar/CLR）编译；本层只做契约校验与落库。
/// </summary>
/// <param name="Scope">作用域（域 + loc + 键上下文）。</param>
/// <param name="PayloadJson">整文档载荷（Upsert/Append 必填；Patch/Delete 可空）。</param>
/// <param name="Mode">变更模式。</param>
/// <param name="Patches">字段补丁集（Patch 模式必填）。</param>
/// <param name="ExpectedDigest">期望摘要（乐观并发；可空 = 不校验）。</param>
public sealed record NodeDocumentChange(
    NodeDocumentScope Scope,
    string? PayloadJson = null,
    NodeChangeMode Mode = NodeChangeMode.Upsert,
    IReadOnlyList<NodeDocumentPatch>? Patches = null,
    string? ExpectedDigest = null,
    Modules.Node.Carriers.CarrierWriteMeta? RecordMeta = null,
    IReadOnlyList<string>? PlacementIds = null);
