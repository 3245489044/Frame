using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 配置即数据行的节点库承载（三库收敛：ur_config 并入 ur_nodes，NodeType=<see cref="NodeTypes.Config"/>）。
/// 实现 <see cref="IConfigStore"/>，读写经 <see cref="INodeStore"/> 落为 config 节点：
/// NodeKey=<c>config.{key}</c>、Key=配置键、PayloadJson=配置值（原样字符串）。省去独立 ur_config 表。
/// </summary>
public sealed class NodeConfigStore : IConfigStore
{
    private readonly INodeStore _nodes;

    public NodeConfigStore(INodeStore nodes)
    {
        _nodes = nodes ?? throw new System.ArgumentNullException(nameof(nodes));
    }

    private static string NodeKeyOf(string key) => NodeKeyPrefix.BuildConfig(key);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var node = await _nodes.GetAsync(NodeKeyOf(key), ct).ConfigureAwait(false);
        return node is null ? null : UsharpValueJson.ToPayloadText(node.Payload);
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
        => _nodes.PutAsync(ZoneNodeAdapter.BuildConfig(key, value), ct);

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => _nodes.DeleteAsync(NodeKeyOf(key), ct);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        var nodes = await _nodes.ListAsync(NodeTypes.Config, limit: int.MaxValue, ct: ct).ConfigureAwait(false);
        var map = new Dictionary<string, string>();
        foreach (var n in nodes)
        {
            // B2 防护：Key 缺失时从 NodeKey 剥前缀取配置键，须先校验前缀与长度，否则越界切片/NRE；脏数据跳过而非崩溃。
            var configKey = n.Key;
            if (string.IsNullOrWhiteSpace(n.Key))
            {
                if (n.NodeKey is not null
                    && n.NodeKey.StartsWith(NodeKeyPrefix.Config, StringComparison.Ordinal)
                    && n.NodeKey.Length > NodeKeyPrefix.Config.Length)
                    configKey = n.NodeKey[NodeKeyPrefix.Config.Length..];
                else
                    continue;
            }
            map[configKey] = UsharpValueJson.ToPayloadText(n.Payload);
        }
        return map;
    }
}
