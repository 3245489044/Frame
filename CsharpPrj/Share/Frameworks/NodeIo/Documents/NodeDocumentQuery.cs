namespace Share.NodeIo;

/// <summary>节点文档读请求（与写请求共用同一作用域；键前缀与显式键互斥）。</summary>
/// <param name="Scope">作用域（域 + loc + 键上下文）。</param>
/// <param name="KeyPrefix">键前缀（可空；给出时按键族枚举，跨容器读须显式声明）。</param>
/// <param name="Round">轮次收窄（可空 = 不限）。</param>
/// <param name="Limit">条数上限（可空 = 不限）。</param>
/// <param name="ExplicitKeys">显式键集（有界读；与 KeyPrefix 互斥）。</param>
/// <param name="Full">显式全量（诊断/库页；仍受域声明位置约束）。</param>
public sealed record NodeDocumentQuery(
    NodeDocumentScope Scope,
    string? KeyPrefix = null,
    int? Round = null,
    int? Limit = null,
    IReadOnlyList<string>? ExplicitKeys = null,
    bool Full = false,
    IReadOnlyList<string>? PlacementIds = null,
    IReadOnlyList<Modules.Node.Projection.NodeProjectionItem>? Overlays = null);
