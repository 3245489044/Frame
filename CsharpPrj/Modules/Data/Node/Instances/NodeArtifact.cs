using Modules.Node.Identity;

namespace Modules.Node.Instances;

/// <summary>归档层条目（中立形态）：来源键 + 原始载荷 + 形态（不解释执行端语义）。</summary>
/// <param name="SourceKey">来源键（归档对象的寻址键）。</param>
/// <param name="PayloadJson">原始载荷全文。</param>
/// <param name="Form">形态（可空；由宿主声明）。</param>
public sealed record NodeArtifactArchive(string SourceKey, string PayloadJson, string? Form = null);

/// <summary>
/// 轮产物（通用形态）：容器 + 轮键 + 子引用 + 终答指针 + 归档。
/// <para>
/// 纪律（R8）：<see cref="Children"/> 只存**子引用地址**，不内联子载荷；三层落点分工由宿主按
/// <see cref="Terminal"/> / <see cref="Archives"/> 决定。
/// </para>
/// </summary>
/// <param name="ContainerKey">容器键。</param>
/// <param name="RoundKey">轮键（宿主轮次 Id 文法）。</param>
/// <param name="Children">子节点地址（只存引用；序 = 到达序）。</param>
/// <param name="Terminal">终答来源节点（可空 = 未归因）。</param>
/// <param name="TerminalText">终答文本（可空 = 无终答）。</param>
/// <param name="Archives">归档条目（可空 = 本轮无归档）。</param>
public sealed record NodeArtifact(
    string ContainerKey,
    string RoundKey,
    IReadOnlyList<NodeAddress> Children,
    NodeAddress? Terminal = null,
    string? TerminalText = null,
    IReadOnlyList<NodeArtifactArchive>? Archives = null)
{
    /// <summary>归档条目集（去 null，空集非 null）。</summary>
    public IReadOnlyList<NodeArtifactArchive> ArchiveItems => Archives ?? Array.Empty<NodeArtifactArchive>();

    /// <summary>是否有终答归因。</summary>
    public bool HasTerminal => Terminal is not null;
}
