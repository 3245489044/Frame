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

    /// <summary>
    /// 目录合格性（装配期判据；空 = 闭合）。
    /// <para>
    /// GP1 身份（命名/唯一）/ GP2 视图（非空/唯一/已声明）/ GP3 归属 /
    /// GP4 门控已登记 / GP6 同一视图不得被两个聚拢点声明（装配歧义）。
    /// </para>
    /// </summary>
    /// <param name="views">已折叠 effective 视图声明集。</param>
    /// <param name="isGateRegistered">门控键是否已在宿主开关表登记（<c>null</c> = 跳过该检）。</param>
    public IReadOnlyList<GatherPointIssue> Validate(
        GatherSpecSet views,
        Func<string, bool>? isGateRegistered = null)
    {
        ArgumentNullException.ThrowIfNull(views);

        var issues = new List<GatherPointIssue>();
        var pointIds = new HashSet<string>(StringComparer.Ordinal);
        var viewOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var point in _points)
        {
            var pointId = point.PointId ?? string.Empty;

            // GP1：身份（与视图/声明共用同一命名判据，防「同名同错」通过）。
            if (!GatherSchema.IsValidName(pointId))
            {
                issues.Add(new GatherPointIssue(
                    "GP1", pointId, $"PointId 非法（须匹配 {GatherSchema.NamePattern}，形如 <owner>.<feature>）"));
            }

            if (!pointIds.Add(pointId))
            {
                issues.Add(new GatherPointIssue("GP1", pointId, "PointId 重复登记（同一身份只允许一处权威）"));
            }

            // GP2：视图（非空 / 不重复 / 必须已在声明面登记）。
            if (point.Views is null || point.Views.Count == 0)
            {
                issues.Add(new GatherPointIssue("GP2", pointId, "未声明任何视图（装配映射无落点）"));
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var view in point.Views)
                {
                    if (string.IsNullOrWhiteSpace(view))
                    {
                        issues.Add(new GatherPointIssue("GP2", pointId, "视图键为空"));
                        continue;
                    }

                    if (!seen.Add(view))
                    {
                        issues.Add(new GatherPointIssue("GP2", pointId, $"视图 '{view}' 重复登记"));
                    }

                    if (views.Find(view) is null)
                    {
                        issues.Add(new GatherPointIssue(
                            "GP2", pointId, $"视图 '{view}' 未在声明面登记（gather-views.generated.json）"));
                    }

                    if (viewOwners.TryGetValue(view, out var owner) && !string.Equals(owner, pointId, StringComparison.Ordinal))
                    {
                        issues.Add(new GatherPointIssue(
                            "GP6", pointId, $"视图 '{view}' 已被聚拢点 '{owner}' 声明（装配歧义）"));
                    }
                    else
                    {
                        viewOwners[view] = pointId;
                    }
                }
            }

            // GP3：归属（依赖方向门禁归因键，禁空）。
            if (string.IsNullOrWhiteSpace(point.Owner))
            {
                issues.Add(new GatherPointIssue("GP3", pointId, "Owner 为空（无法归因归属工程）"));
            }

            // GP4：门控必须在宿主开关表内（null = 常开，不检）。
            if (!string.IsNullOrWhiteSpace(point.GateKey)
                && isGateRegistered is not null
                && !isGateRegistered(point.GateKey!))
            {
                issues.Add(new GatherPointIssue(
                    "GP4", pointId, $"门控键 '{point.GateKey}' 未在宿主开关表登记"));
            }
        }

        return issues;
    }

    /// <summary>
    /// 装配一个聚拢点：解析登记 → 校验视图归属 → 调工厂构造模块。
    /// <para>任一不满足 ⇒ <see cref="GatherClosureViolationException"/>（fail-closed，禁静默返回空模块）。</para>
    /// </summary>
    /// <param name="pointId">聚拢点身份。</param>
    /// <param name="view">已解析的 effective 视图声明。</param>
    /// <param name="context">宿主上下文（由工厂解释；无上下文模块传 <c>null</c>）。</param>
    public GatherPointAssembly Assemble(string pointId, GatherViewSpec view, object? context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pointId);
        ArgumentNullException.ThrowIfNull(view);

        var point = Find(pointId)
            ?? throw new GatherClosureViolationException(
                $"[GP1] 未登记的聚拢点 '{pointId}'（装配映射必须经目录单表）");

        var claimed = false;
        foreach (var candidate in point.Views)
        {
            if (string.Equals(candidate, view.View, StringComparison.Ordinal))
            {
                claimed = true;
                break;
            }
        }

        if (!claimed)
        {
            throw new GatherClosureViolationException(
                $"[GP2] 聚拢点 '{pointId}' 未声明视图 '{view.View}'（已声明: {string.Join(",", point.Views)}）");
        }

        if (point.Create is null)
        {
            throw new GatherClosureViolationException(
                $"[GP5] 聚拢点 '{pointId}' 缺装配工厂（仅声明登记不可用于运行期装配）");
        }

        IGatherModule? module;
        try
        {
            module = point.Create(view, context);
        }
        catch (GatherClosureViolationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GatherClosureViolationException(
                $"[GP5] 聚拢点 '{pointId}' 工厂失败（视图 '{view.View}'）：{exception.Message}");
        }

        if (module is null)
        {
            throw new GatherClosureViolationException(
                $"[GP5] 聚拢点 '{pointId}' 工厂返回 null（视图 '{view.View}'）");
        }

        return new GatherPointAssembly(point, view, module);
    }
}