namespace Usharp.Semantics;

/// <summary>
/// 图后端端口：Usharp 核心只认端口 —— 环检测 / 可达（影响域）/ 两点路径 / 流完整性由装配方提供；
/// 未装配时为 <see cref="NullGraphBackend"/>（纯声明校验模式：闸A、引用即边、结构化诊断 100% 在核心内，不依赖任何图框架）。
/// <para>签名只用 Usharp 自有类型 + BCL：外部图框架（如 UniversalReference）类型不得进入核心公共签名。</para>
/// </summary>
public interface IUsharpGraphBackend
{
    /// <summary>是否已装配真实图能力（false = 纯声明校验模式，图类判据不产出而是显式可见化）。</summary>
    bool IsAvailable { get; }

    /// <summary>环检测：给定节点集与出边取函数，返回每个环的节点链（无环返回空）。</summary>
    IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IEnumerable<string> nodes, Func<string, IEnumerable<string>> outgoing);

    /// <summary>可达集（影响域）。</summary>
    IReadOnlySet<string> Reachable(string start, Func<string, IEnumerable<string>> outgoing);

    /// <summary>两点是否可达。</summary>
    bool HasPath(string source, string target, Func<string, IEnumerable<string>> outgoing);

    /// <summary>
    /// 流完整性（唯一入口 / 必须有终点 / 节点 ID 唯一 / 边端必须存在）→ 诊断集（空 = 通过）。
    /// <paramref name="isEntry"/> / <paramref name="isExit"/> 由调用方按流的入口/终点声明给出。
    /// </summary>
    IReadOnlyList<UsharpDiagnostic> ValidateFlowIntegrity(
        string flowId,
        IReadOnlyList<string> nodes,
        IReadOnlyList<(string Source, string Target)> edges,
        Func<string, bool> isEntry,
        Func<string, bool> isExit);

    /// <summary>诊断报表渲染（对外字符串报表的单一入口）。</summary>
    string RenderReport(IReadOnlyList<UsharpDiagnostic> diagnostics);
}
