using System.Collections.Concurrent;

namespace Modules.Node.Diagnostics;

public enum NodeDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>框架诊断条目（来源可辨 N9：缺口不静默，一律可见化）。</summary>
public sealed record NodeDiagnostic(string Code, NodeDiagnosticSeverity Severity, string Message, string? Subject = null)
{
    /// <summary>诊断码前缀族（对齐既有 <c>port.*</c> / <c>placement.*</c> / <c>write.*</c> / <c>spec.*</c> 约定）。</summary>
    public string Family => Code.Contains('.', StringComparison.Ordinal) ? Code[..Code.IndexOf('.', StringComparison.Ordinal)] : Code;

    public override string ToString() =>
        Subject is null ? $"[{Severity}] {Code}: {Message}" : $"[{Severity}] {Code}: {Message} ({Subject})";
}

/// <summary>诊断出口端口（实例级注入，禁进程静态）。</summary>
public interface INodeDiagnosticsSink
{
    void Report(NodeDiagnostic diagnostic);
}

/// <summary>缺省诊断出口：丢弃（用于纯内存/无观测宿主）。</summary>
public sealed class NullDiagnosticsSink : INodeDiagnosticsSink
{
    public static readonly NullDiagnosticsSink Instance = new();

    private NullDiagnosticsSink()
    {
    }

    public void Report(NodeDiagnostic diagnostic)
    {
    }
}

/// <summary>诊断收集器（实例级；跨模块共享经组合根注入，不使用静态可变状态）。</summary>
public sealed class NodeFrameworkDiagnostics : INodeDiagnosticsSink
{
    private readonly ConcurrentQueue<NodeDiagnostic> _items = new();

    public void Report(NodeDiagnostic diagnostic) => _items.Enqueue(diagnostic);

    public IReadOnlyList<NodeDiagnostic> Items => _items.ToArray();

    public int Count => _items.Count;

    public bool HasErrors => _items.Any(d => d.Severity == NodeDiagnosticSeverity.Error);

    public IReadOnlyList<NodeDiagnostic> OfCode(string code) =>
        _items.Where(d => string.Equals(d.Code, code, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<NodeDiagnostic> OfFamily(string family) =>
        _items.Where(d => string.Equals(d.Family, family, StringComparison.Ordinal)).ToArray();

    public void Clear() => _items.Clear();
}
