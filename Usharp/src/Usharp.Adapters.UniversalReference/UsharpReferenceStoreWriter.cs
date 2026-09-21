using Plate.DslMethod;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.UniversalReference;

/// <summary>
/// 引用图落库（**宿主显式调用**，编译期不写库）：
/// <list type="bullet">
/// <item><b>资源</b>：键型空间坐标（<c>ResourceType=空间号</c> / <c>Id=坐标键</c>）、流号（<c>workflow</c>）、
/// <c>[Node]</c> 名（<c>node</c>）、兜底（<c>resource</c>）。值型空间坐标**不落图**（属性面）。</item>
/// <item><b>引用</b>：落图边（不含 <c>Over</c>/<c>RowRef</c>），<c>refType</c> = <c>ur=</c> 值 ∨ 边类型原名
/// —— 走 <see cref="IResourceReferenceStore"/> 的 <c>string refType</c> 通道，**不塌缩**。</item>
/// </list>
/// </summary>
public sealed class UsharpReferenceStoreWriter
{
    /// <summary>流资源的资源类型（既有 UR 词汇表成员）。</summary>
    public const string WorkflowResourceType = "workflow";

    /// <summary>节点可执行体的资源类型（既有 UR 词汇表成员）。</summary>
    public const string NodeResourceType = "node";

    /// <summary>未登记端点的兜底资源类型（既有 UR 词汇表成员；避免丢边）。</summary>
    public const string FallbackResourceType = "resource";

    private readonly IResourceReferenceStore _store;

    public UsharpReferenceStoreWriter(IResourceReferenceStore store, UsharpEdgeTypeMap? map = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Map = map ?? new UsharpEdgeTypeMap();
    }

    /// <summary>边类型映射表（宿主可另行调用 <see cref="UsharpEdgeTypeMap.Validate"/> 做映射闸A）。</summary>
    public UsharpEdgeTypeMap Map { get; }

    /// <summary>写入资源与引用；返回写入的引用条数。</summary>
    public async Task<int> WriteAsync(UsharpCompilation compilation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var endpoints = EndpointIndex.Build(compilation);

        foreach (var space in compilation.Spaces.Spaces.Where(s => s.Kind == UsharpSpaceKind.Keys))
            foreach (var axis in space.Axes)
                await _store.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = space.SpaceId, Id = axis.Name }, ct: ct).ConfigureAwait(false);

        foreach (var flow in compilation.Flows)
            await _store.AddResourceAsync(
                new ResourceIdentifier { ResourceType = WorkflowResourceType, Id = flow.FlowId }, ct: ct).ConfigureAwait(false);

        foreach (var node in compilation.Documents.SelectMany(d => d.Declarations).Where(d => d.Kind == DslSymbolKind.Node))
            await _store.AddResourceAsync(
                new ResourceIdentifier { ResourceType = NodeResourceType, Id = node.Name }, ct: ct).ConfigureAwait(false);

        var written = 0;
        foreach (var edge in UsharpEdgeSelection.GraphEdges(compilation))
        {
            await _store.AddReferenceAsync(
                endpoints.Resolve(edge.Source), endpoints.Resolve(edge.Target),
                RefTypeOf(compilation, edge.Type), ct: ct).ConfigureAwait(false);
            written++;
        }

        return written;
    }

    /// <summary>引用类型字符串：<c>ur=</c> 显式映射优先，否则用边类型原名（string 通道不塌缩）。</summary>
    private static string RefTypeOf(UsharpCompilation compilation, string edgeType)
    {
        foreach (var doc in compilation.Documents)
            foreach (var decl in doc.Declarations)
            {
                if (decl.Kind != DslSymbolKind.Edge || !string.Equals(decl.Name, edgeType, StringComparison.Ordinal))
                    continue;

                var ur = decl.Field(UsharpEdgeTypeMap.FieldUr);
                return string.IsNullOrWhiteSpace(ur) ? edgeType : ur.Trim();
            }
        return edgeType;
    }

    /// <summary>端点 → 资源标识（键型空间坐标优先，其次流号 / <c>[Node]</c> 名，最后兜底）。</summary>
    private sealed class EndpointIndex
    {
        private readonly Dictionary<string, ResourceIdentifier> _byToken = new(StringComparer.Ordinal);

        public static EndpointIndex Build(UsharpCompilation compilation)
        {
            var index = new EndpointIndex();

            foreach (var space in compilation.Spaces.Spaces.Where(s => s.Kind == UsharpSpaceKind.Keys))
                foreach (var axis in space.Axes)
                    index.Add(axis.Name, space.SpaceId);

            foreach (var flow in compilation.Flows) index.Add(flow.FlowId, WorkflowResourceType);
            foreach (var node in compilation.Documents.SelectMany(d => d.Declarations).Where(d => d.Kind == DslSymbolKind.Node))
                index.Add(node.Name, NodeResourceType);

            return index;
        }

        public ResourceIdentifier Resolve(string? token)
            => token is not null && _byToken.TryGetValue(token, out var id)
                ? id
                : new ResourceIdentifier { ResourceType = FallbackResourceType, Id = token ?? string.Empty };

        private void Add(string token, string resourceType)
            => _byToken.TryAdd(token, new ResourceIdentifier { ResourceType = resourceType, Id = token });
    }
}
