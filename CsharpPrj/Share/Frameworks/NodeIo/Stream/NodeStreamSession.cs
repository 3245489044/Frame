using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
using Modules.Node.Stream;

namespace Share.NodeIo;

/// <summary>Stream 会话装配/提交失败（不可用即抛；不伪造空提交）。</summary>
public sealed class NodeDocumentStreamException : InvalidOperationException
{
    public NodeDocumentStreamException(string message) : base(message)
    {
    }
}

/// <summary>Stream 运行容器（位置键已由作用域解析给出，构造期固定）。</summary>
internal sealed class NodeStreamRun : AggregateRun
{
    public NodeStreamRun(string partitionKey, string positionKey)
        : base(partitionKey, "kind", _ => positionKey, 4096, "Share.NodeIo_AppendBeforeRound")
    {
    }
}

/// <summary>Stream 写会话：Begin → Append* → Commit（提交经同一 <see cref="INodeDocumentPort.ApplyAsync"/>）。</summary>
public interface INodeStreamSession
{
    NodeDocumentScope Scope { get; }

    int Round { get; }

    AggregateFrame? Append(
        string kind,
        IReadOnlyDictionary<string, object?>? fields = null,
        DateTimeOffset? atUtc = null);

    Task<NodeMaterializationResult> CommitAsync(CancellationToken cancellationToken = default);
}

internal sealed class NodeStreamSession : INodeStreamSession
{
    private readonly NodeDocumentPort _port;
    private readonly NodeStreamRun _run;
    private readonly string _runKey;

    public NodeStreamSession(NodeDocumentPort port, NodeStreamRun run, NodeDocumentScope scope, string runKey)
    {
        _port = port;
        _run = run;
        _runKey = runKey;
        Scope = scope;
    }

    public NodeDocumentScope Scope { get; }

    public int Round => _run.Round;

    public AggregateFrame? Append(
        string kind,
        IReadOnlyDictionary<string, object?>? fields = null,
        DateTimeOffset? atUtc = null)
        => _run.Append(kind, fields, atUtc);

    public async Task<NodeMaterializationResult> CommitAsync(CancellationToken cancellationToken = default)
    {
        var aggregate = await _run.SnapshotAggregateAsync().ConfigureAwait(false);
        if (aggregate is null)
        {
            return new NodeMaterializationResult(
                MaterializeStatus.Rejected,
                Scope.KeyContext,
                Array.Empty<NodePlacementResult>(),
                new[]
                {
                    new NodeDiagnostic(
                        "stream.empty", NodeDiagnosticSeverity.Warning,
                        NodeDocumentReasons.StreamEmpty, Scope.Domain),
                });
        }

        var result = await _port.ApplyAsync(
                new NodeDocumentChange(Scope, aggregate.PayloadJson, NodeChangeMode.Upsert),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Status is MaterializeStatus.Written or MaterializeStatus.Unchanged)
        {
            _port.ReleaseStream(_runKey);
        }

        return result;
    }
}
