using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node;
using Modules.Node.Identity;
using Modules.Node.Materialization;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 内容物化服务（**唯一写门适配层**，域无关）：把领域载荷转成 <see cref="NodeMaterializeRequest"/>
/// 并交给框架唯一写入口 <see cref="NodeFramework.Materializer"/>。
/// <para>
/// 三条纪律（由既有实测契约决定，非风格选择）：
/// ① **必带显式 <c>PlacementIds</c>**：按 <c>Domain</c> 物化会命中键模板取值不在身份中的位置
/// （如 <c>{storage}.{node}</c>），<c>KeyTemplateEvaluator.Evaluate</c> 缺值即抛异常；
/// ② **入参写入者须与位置声明一致**：<c>NodeMaterializer</c> 逐位置校验 <c>WriterId</c>，不一致即拒写；
/// ③ **身份键往返校验**：先按身份模板反解再回代，键不一致即拒（防「键不匹配身份模板时反解出垃圾值 →
/// 位置键静默漂移」）。
/// </para>
/// </summary>
public sealed class NodeContentMaterializeService
{
    private readonly NodeFramework _framework;
    private readonly string _writerId;
    private readonly IReadOnlyList<string> _placementIds;

    /// <param name="framework">所属域的节点框架。</param>
    /// <param name="writerId">写入者 id（须等于目标位置声明的 <c>writer</c>）。</param>
    /// <param name="placementIds">显式位置 id 集（禁留空：留空即退化为按 Domain 物化）。</param>
    public NodeContentMaterializeService(NodeFramework framework, string writerId, IReadOnlyList<string> placementIds)
    {
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        if (string.IsNullOrWhiteSpace(writerId))
        {
            throw new ArgumentException("写入者 id 不可为空（位置声明按 writer 校验写入者）", nameof(writerId));
        }

        if (placementIds is not { Count: > 0 })
        {
            throw new ArgumentException("位置 id 集不可为空（按 Domain 物化会命中取值不在身份中的位置并抛异常）", nameof(placementIds));
        }

        _writerId = writerId;
        _placementIds = placementIds.ToArray();
    }

    /// <summary>所属框架（消费方诊断/读投影复用）。</summary>
    public NodeFramework Framework => _framework;

    /// <summary>位置 id 集（只读）。</summary>
    public IReadOnlyList<string> PlacementIds => _placementIds;

    /// <summary>物化单个节点载荷（仅键往返校验）。</summary>
    /// <param name="key">身份键（须与所属域 identity 模板同形）。</param>
    /// <param name="json">载荷 JSON（恒为 JSON 文档）。</param>
    /// <param name="ct">取消令牌。</param>
    public Task<NodeContentMaterializeOutcome> MaterializeAsync(
        string key, string json, CancellationToken ct = default)
        => MaterializeAsync(key, json, expectedValues: null, ct);

    /// <summary>
    /// 物化单个节点载荷 + **反解值校验**：键往返通过不代表反解到的是本域期望值，故由调用方给出期望值逐键比对
    /// （不等即拒写、可见化，防位置键静默漂移）。
    /// </summary>
    /// <param name="key">身份键（须与所属域 identity 模板同形）。</param>
    /// <param name="json">载荷 JSON。</param>
    /// <param name="expectedValues">身份占位符 → 期望值（<c>null</c> = 只做往返校验）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<NodeContentMaterializeOutcome> MaterializeAsync(
        string key, string json, IReadOnlyDictionary<string, string>? expectedValues, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Reject(key ?? string.Empty, "key.empty");
        }

        // RED-1：同一键**只解析一遍** —— 往返校验与期望值校验共用该值袋（去双倍解析 + 防两处口径漂移）。
        var template = _framework.Identity.Template;
        if (!KeyTemplateEvaluator.TryParseValues(template, key, out var values))
        {
            return Reject(key, $"key.unparsable（不符合身份模板 '{template}'）");
        }

        if (!RoundTripOk(key, values, out var reason))
        {
            return Reject(key, reason);
        }

        if (expectedValues is { Count: > 0 } && !IdentityValuesOk(values, expectedValues, out var mismatch))
        {
            return Reject(key, mismatch);
        }

        var result = await _framework.Materializer
            .MaterializeAsync(
                new NodeMaterializeRequest(key, json ?? string.Empty, PlacementIds: _placementIds, WriterId: _writerId),
                ct)
            .ConfigureAwait(false);

        return Summarize(result);
    }

    /// <summary>
    /// 身份键往返校验（先反解再回代）：不一致说明键与该域身份模板不同形 —— 此时框架仍可能「匹配成功」并
    /// 反解出**垃圾值**，进而产出漂移的位置键。故在此显式拒。
    /// </summary>
    private bool RoundTripOk(string key, IReadOnlyDictionary<string, string> values, out string reason)
    {
        reason = string.Empty;
        var template = _framework.Identity.Template;
        if (!KeyTemplateEvaluator.TryEvaluate(template, values, out var rebuilt, out _) ||
            !string.Equals(rebuilt, key, StringComparison.Ordinal))
        {
            reason = $"key.round-trip-mismatch（identity='{template}' 反解得到非本键的值：{rebuilt}）";
            return false;
        }

        return true;
    }

    private static NodeContentMaterializeOutcome Reject(string key, string reason)
        => new(key, MaterializeStatus.Rejected, 0, 0, 0, 1, 0, new[] { reason });

    /// <summary>
    /// 反解值校验（往返校验的补充，**非替代**）：非贪婪占位符可能把键反解成垃圾值却仍能逐字回代，
    /// 此时位置键会按垃圾值渲染 ⇒ 静默漂移。故由调用方给出期望值逐键 Ordinal 比对。
    /// </summary>
    private bool IdentityValuesOk(
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> expected, out string reason)
    {
        reason = string.Empty;
        var template = _framework.Identity.Template;
        foreach (var pair in expected)
        {
            if (!values.TryGetValue(pair.Key, out var actual))
            {
                reason = $"key.identity-value-missing（身份模板 '{template}' 未产出占位符 '{pair.Key}'）";
                return false;
            }

            if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
            {
                reason = $"key.identity-value-mismatch（'{pair.Key}' 反解为 '{actual}'，期望 '{pair.Value}'："
                         + "位置键会按垃圾值渲染，拒写防漂移）";
                return false;
            }
        }

        return true;
    }

    private static NodeContentMaterializeOutcome Summarize(NodeMaterializationResult result)
    {
        var reasons = result.Placements
            .Where(p => !string.IsNullOrWhiteSpace(p.Reason))
            .Select(p => $"{p.PlacementId}:{p.Reason}")
            .Concat(result.Diagnostics
                .Where(d => d.Severity == Modules.Node.Diagnostics.NodeDiagnosticSeverity.Error)
                .Select(d => d.Code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new NodeContentMaterializeOutcome(
            result.Key,
            result.Status,
            result.Placements.Count(p => p.Status == Modules.Node.Carriers.CarrierWriteStatus.Written),
            result.Placements.Count(p => p.Status == Modules.Node.Carriers.CarrierWriteStatus.Unchanged),
            result.Placements.Count(p => p.Status == Modules.Node.Carriers.CarrierWriteStatus.Conflict),
            result.Placements.Count(p => p.Status == Modules.Node.Carriers.CarrierWriteStatus.Rejected),
            result.Placements.Count(p => p.Status is null),
            reasons);
    }
}
