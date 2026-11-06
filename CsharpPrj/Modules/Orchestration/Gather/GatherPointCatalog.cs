#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.Gather;

/// <summary>
/// 聚拢点目录（**装配聚拢唯一表**）：把「视图 ↔ 实现」的装配映射收口为数据。
/// <para>
/// 纪律：
/// <list type="bullet">
/// <item>宿主只维护一张登记表；新增/删除聚拢点 = 表加/删一行，调用点与组合根零改动；</item>
/// <item>目录只做查找/校验/装配，不持有运行期状态，也不感知任何领域语义；</item>
/// <item>校验只对照 <see cref="GatherSpecSet"/>（已折叠 effective 视图）与宿主门控白名单。</item>
/// </list>
/// </para>
/// </summary>
public sealed class GatherPointCatalog
{
    private readonly List<GatherPointRegistration> _points;
    private readonly Dictionary<string, GatherPointRegistration> _byId;

    /// <summary>由宿主登记表构造目录（顺序 = 装配/日志顺序）。</summary>
    /// <param name="points">聚拢点登记集。</param>
    public GatherPointCatalog(IEnumerable<GatherPointRegistration> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = new List<GatherPointRegistration>();
        _byId = new Dictionary<string, GatherPointRegistration>(StringComparer.Ordinal);

        foreach (var point in points)
        {
            if (point is null)
            {
                throw new ArgumentException("聚拢点登记不得为 null", nameof(points));
            }

            _points.Add(point);

            // 重复 PointId 由 Validate 判红；查找面保持最后一次登记（确定性：声明序末项）。
            if (!string.IsNullOrWhiteSpace(point.PointId))
            {
                _byId[point.PointId] = point;
            }
        }
    }

    /// <summary>全部登记（声明序）。</summary>
    public IReadOnlyList<GatherPointRegistration> All => _points;

    /// <summary>门控放行后的登记集（顺序不变）。</summary>
    /// <param name="isGateEnabled">门控键 → 是否启用（宿主注入；<c>null</c> = 全部放行）。</param>
    public IReadOnlyList<GatherPointRegistration> Enabled(Func<string, bool>? isGateEnabled = null)
    {
        var enabled = new List<GatherPointRegistration>(_points.Count);
        foreach (var point in _points)
        {
            if (point.IsGateOpen(isGateEnabled))
            {
                enabled.Add(point);
            }
        }

        return enabled;
    }

    /// <summary>按 PointId 查登记（未命中 ⇒ null）。</summary>
    /// <param name="pointId">聚拢点身份。</param>
    public GatherPointRegistration? Find(string pointId)
        => !string.IsNullOrWhiteSpace(pointId) && _byId.TryGetValue(pointId, out var point)
            ? point
            : null;
