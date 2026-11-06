using System.Collections.Concurrent;
using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;

namespace Modules.Node.Ports;

/// <summary>
/// 节点端口契约目录（实例级）：键集合 → <see cref="PortContract"/> 的唯一投影入口 + 解析缓存。
/// <para>
/// 声明面由组合根注入（不读进程静态）；缓存键 = callKey | 入键集合 | 出键集合，运行期零重复解析。
/// 与 <see cref="PortContractRules"/> 的关系：本类只负责「缓存与投影」，规则单源仍在 KeySpace。
/// </para>
/// </summary>
public sealed class NodePortContractCatalog
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<PortContract>> _contracts =
        new(StringComparer.Ordinal);

    /// <param name="declaration">键空间声明（端口契约的声明来源）。</param>
    public NodePortContractCatalog(IKeySpaceDeclaration declaration)
        => Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));

    /// <summary>已注入的键空间声明。</summary>
    public IKeySpaceDeclaration Declaration { get; }

    /// <summary>解析端口契约（按 callKey + 已归一键集合缓存；同一节点第二次执行零解析）。</summary>
    /// <param name="callKey">节点调用键（可空；仅参与缓存键）。</param>
    /// <param name="parameterKeys">已归一入参键集合。</param>
    /// <param name="resultKeys">已归一结果键集合。</param>
    public IReadOnlyList<PortContract> Resolve(
        string? callKey,
        IReadOnlyList<string>? parameterKeys,
        IReadOnlyList<string>? resultKeys)
    {
        var cacheKey = string.Concat(
            callKey ?? string.Empty,
            "|",
            JoinKeys(parameterKeys),
            "|",
            JoinKeys(resultKeys));

        return _contracts.GetOrAdd(
            cacheKey,
            _ => PortContractRules.ForKeys(Declaration, parameterKeys, resultKeys));
    }

    /// <summary>清空契约缓存（声明热更新后调用）。</summary>
    public void Clear() => _contracts.Clear();

    private static string JoinKeys(IReadOnlyList<string>? keys)
        => keys is { Count: > 0 } ? string.Join(",", keys) : string.Empty;
}
