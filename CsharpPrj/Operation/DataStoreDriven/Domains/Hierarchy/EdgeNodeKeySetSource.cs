using Modules.Node.Read;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 键集来源的**边实现**（唯一可触达引用图的适配器）：task → node / history → task → node。
/// <para>框架（<c>Modules.Node.Read</c>）只认端口；本类在 Operation，禁被框架引用（保持层 2 零 Frame 依赖）。</para>
/// </summary>
public sealed class EdgeNodeKeySetSource : INodeKeySetSource
{
    private readonly Func<IContainerMemberPort?> _references;

    /// <param name="references">关系管理器提供者（未装配 ⇒ null = source-unavailable，不静默空）。</param>
    public EdgeNodeKeySetSource(Func<IContainerMemberPort?> references)
        => _references = references ?? throw new ArgumentNullException(nameof(references));

    /// <inheritdoc />
    public async Task<NodeKeySetResult> ResolveAsync(NodeKeySetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var manager = _references();
        if (manager is null)
        {
            return NodeKeySetResult.Empty(NodeKeySetOrigin.Edge, NodeKeySetReasons.SourceUnavailable);
        }

        var workflowId = request.WorkflowId ?? string.Empty;
        IReadOnlyList<string> keys = request.Scope switch
        {
            NodeKeySetScope.Task => await manager.GetMemberNodeKeysAsync(request.Key, workflowId, ct).ConfigureAwait(false),
            NodeKeySetScope.History => await manager.GetContainerNodeKeysAsync(request.Key, workflowId, ct).ConfigureAwait(false),
            NodeKeySetScope.Node => new[] { request.Key },
            _ => Array.Empty<string>(),
        };

        if (request.Scope is not (NodeKeySetScope.Task or NodeKeySetScope.History or NodeKeySetScope.Node))
        {
            return NodeKeySetResult.Empty(NodeKeySetOrigin.Edge, NodeKeySetReasons.SourceUnavailable);
        }

        return keys.Count == 0
            ? NodeKeySetResult.Empty(NodeKeySetOrigin.Edge, NodeKeySetReasons.NoBinding)
            : new NodeKeySetResult(keys, NodeKeySetOrigin.Edge, Array.Empty<string>());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, NodeKeySetResult>> ResolveManyAsync(
        NodeKeySetScope scope,
        IReadOnlyList<string> keys,
        string? workflowId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<string, NodeKeySetResult>(StringComparer.Ordinal);
        var manager = _references();
        if (manager is null)
        {
            foreach (var key in keys)
            {
                result[key] = NodeKeySetResult.Empty(NodeKeySetOrigin.Edge, NodeKeySetReasons.SourceUnavailable);
            }

            return result;
        }

        if (scope == NodeKeySetScope.Task)
        {
            // 一次图枚举（消 N+1）：生产实现覆盖为 GetReferencesManyAsync。
            var many = await manager
                .GetMemberNodeKeysManyAsync(keys, workflowId ?? string.Empty, ct)
                .ConfigureAwait(false);
            foreach (var key in keys)
            {
                result[key] = many.TryGetValue(key, out var found) && found.Count > 0
                    ? new NodeKeySetResult(found, NodeKeySetOrigin.Edge, Array.Empty<string>())
                    : NodeKeySetResult.Empty(NodeKeySetOrigin.Edge, NodeKeySetReasons.NoBinding);
            }

            return result;
        }

        foreach (var key in keys)
        {
            result[key] = await ResolveAsync(
                    new NodeKeySetRequest(scope, key, workflowId), ct)
                .ConfigureAwait(false);
        }

        return result;
    }
}
