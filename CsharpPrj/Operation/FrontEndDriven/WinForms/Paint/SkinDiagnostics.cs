#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>外观运行态快照（纯数据；断言与日志共用的唯一读取面）。</summary>
/// <param name="Enabled">owner-draw 是否启用。</param>
/// <param name="Profile">生效外观档（声明 <c>active_profile</c> 或显式覆盖）。</param>
/// <param name="Carrier">载体 id（WinForms = <c>winforms</c>）。</param>
/// <param name="Fingerprint">表面声明指纹（缓存/写槽失效依据）。</param>
/// <param name="CatalogCount">材质目录条目数（容器键 → 槽 → 材质）。</param>
/// <param name="GlobalMaterial">全局回退材质 id（未命中容器键时使用）。</param>
/// <param name="GlobalLayerCount">全局材质的层数（0 ⇒ 无可绘层）。</param>
/// <param name="UnresolvedTokenCount">无法解析为颜色的层值数（token 未换值的可见指标）。</param>
/// <param name="UnresolvedValues">未解析值明细（材质/层=原值）。</param>
/// <param name="AttachedControls">当前已挂 owner-draw 的控件数。</param>
/// <param name="PaintCount">绘制次数（累计）。</param>
/// <param name="LastSkipped">最近一次绘制的跳过项（图元:角色）。</param>
/// <param name="Revision">装配/挂载版本号（低频事件；供 wait-until 同步）。</param>
public sealed record SkinDiagnosticsSnapshot(
    bool Enabled,
    string Profile,
    string Carrier,
    string Fingerprint,
    int CatalogCount,
    string GlobalMaterial,
    int GlobalLayerCount,
    int UnresolvedTokenCount,
    IReadOnlyList<string> UnresolvedValues,
    int AttachedControls,
    long PaintCount,
    IReadOnlyList<string> LastSkipped,
    long PaintedLayers,
    long Revision);

/// <summary>
/// 外观诊断（进程级·单一写入面）：装配 / 挂载 / 绘制三处的结构事实集中于此。
/// <para>写者唯一：<see cref="SkinAppearanceInstaller"/>（装配）、<see cref="SkinDecorator"/>（挂载/绘制）。</para>
/// <para>只读消费：命令层断言（<c>assert skin-binding</c>）与日志；禁在此做绘制或 IO。</para>
/// </summary>
public static class SkinDiagnostics
{
    private static readonly object Gate = new();
    private static SkinDiagnosticsSnapshot _current = Empty();
    private static int _attached;
    private static long _paintCount;
    private static long _revision;

    /// <summary>当前快照（不可变；读不阻塞）。</summary>
    public static SkinDiagnosticsSnapshot Current => Volatile.Read(ref _current);

    /// <summary>空快照（未装配；禁伪造默认档）。</summary>
    public static SkinDiagnosticsSnapshot Empty() => new(
        false, string.Empty, string.Empty, string.Empty, 0, string.Empty, 0, 0,
        Array.Empty<string>(), 0, 0, Array.Empty<string>(), 0, 0);

    /// <summary>装配记录（<see cref="SkinAppearanceInstaller.Install"/> 唯一调用点）。</summary>
    public static void RecordInstall(
        bool enabled,
        string? profile,
        string? carrier,
        string? fingerprint,
        int catalogCount,
        ResolvedMaterial? global)
    {
        var unresolved = CountUnresolved(SkinMaterialCatalog.Snapshot, global);
        lock (Gate)
        {
            _revision++;
            _current = new SkinDiagnosticsSnapshot(
                enabled,
                profile ?? string.Empty,
                carrier ?? string.Empty,
                fingerprint ?? string.Empty,
                catalogCount,
                global?.MaterialId ?? string.Empty,
                global?.Layers.Count ?? 0,
                unresolved.Count,
                unresolved,
                _attached,
                Interlocked.Read(ref _paintCount),
                Array.Empty<string>(),
                _current.PaintedLayers,
                _revision);
        }
    }

    /// <summary>装配卸载记录（<see cref="SkinAppearanceInstaller.Uninstall"/> 唯一调用点）。</summary>
    public static void RecordUninstall()
    {
        lock (Gate)
        {
            _revision++;
            _attached = 0;
            _current = Empty() with { Revision = _revision };
        }
    }

    /// <summary>挂载计数（新增一条 owner-draw 接线 +1；卸除 -1）。</summary>
    public static void RecordAttach(bool attached)
    {
        lock (Gate)
        {
            _attached = Math.Max(0, _attached + (attached ? 1 : -1));
            _revision++;
            _current = _current with { AttachedControls = _attached, Revision = _revision };
        }
    }

    /// <summary>
    /// 一帧绘制记录（K6 唯一写入口）：PaintCount 每帧只 +1；PaintedLayers/LastSkipped 取自合并报告。
    /// <para>静态底图重建不再单独计数（此前一帧会 +2，命中帧的 PaintedLayers 被动态层覆盖成 1）。</para>
    /// </summary>
    /// <param name="report">本帧静态 + 动态合并报告。</param>
    public static void RecordFrame(SkinPaintReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Interlocked.Increment(ref _paintCount);
        lock (Gate)
        {
            _current = _current with
            {
                PaintCount = Interlocked.Read(ref _paintCount),
                PaintedLayers = report.PaintedLayers,
                LastSkipped = report.Details.Count > 0 ? report.Details.ToArray() : Array.Empty<string>(),
            };
        }
    }

    /// <summary>
    /// 旧版分段绘制记录（**兼容保留**，勿在新代码调用；用 <see cref="RecordFrame"/> 以免一帧计两次）。
    /// </summary>
    /// <param name="skipped">跳过/降级项。</param>
    /// <param name="paintedLayers">本次实际落笔的图层数（静态+动态合计）。</param>
    public static void RecordPaint(IReadOnlyList<string>? skipped, int paintedLayers = -1)
    {
        Interlocked.Increment(ref _paintCount);
        lock (Gate)
        {
            _current = _current with
            {
                PaintCount = Interlocked.Read(ref _paintCount),
                PaintedLayers = paintedLayers >= 0 ? paintedLayers : _current.PaintedLayers,
                LastSkipped = skipped is { Count: > 0 } ? skipped.ToArray() : _current.LastSkipped,
            };
        }
    }

    /// <summary>
    /// 统计非法/未换值的声明项（token 未换值的结构指标）。
    /// <para>判据载体 = K1 强类型解析输出的 issues（**与运行期同源**）：扫描全部声明参数（色值与 width/radius 等数值），
    /// 不只 <c>value</c>；不另写「像不像颜色」的第三套判定。</para>
    /// </summary>
    /// <param name="catalog">材质目录（按材质 id 去重）。</param>
    /// <param name="global">全局回退材质（可为空）。</param>
    public static IReadOnlyList<string> CountUnresolved(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? catalog,
        ResolvedMaterial? global)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();

        if (catalog is not null)
        {
            foreach (var materials in catalog.Values)
            {
                foreach (var material in materials.Values) Collect(material);
            }
        }

        Collect(global);
        return unresolved;

        void Collect(ResolvedMaterial? material)
        {
            if (material is null || !seen.Add(material.MaterialId)) return;

            // 解析结果按层栈缓存（K1）：诊断与绘制读同一份 issues，禁二次解析。
            var specSet = ResolvedLayerSpecProjection.ParseCached(material.Layers);
            foreach (var spec in specSet.Specs)
            {
                foreach (var issue in spec.Issues)
                {
                    if (!IsUnresolvedIssue(issue.Code)) continue;
                    unresolved.Add(issue.Detail.StartsWith(spec.Role + ":", StringComparison.Ordinal)
                        ? material.MaterialId + "/" + issue.Detail
                        : material.MaterialId + "/" + spec.Role + "=" + issue.Detail);
                }
            }
        }
    }

    /// <summary>计入「未换值/非法声明」的问题码（缺失参数不算 token 未换值，单独由层落地判定处理）。</summary>
    private static bool IsUnresolvedIssue(string code) => code switch
    {
        "unresolved-value" => true,
        "color-invalid" => true,
        "param-invalid" => true,
        "border-color-invalid" => true,
        "border-width-invalid" => true,
        _ => false,
    };

    /// <summary>单行稳定描述（断言失败证据 / 日志；键序固定）。</summary>
    public static string Describe()
    {
        var snapshot = Current;
        return string.Format(
            "enabled={0} profile={1} carrier={2} catalog={3} global={4} layers={5} unresolvedTokens={6} attached={7} painted={8} revision={9}",
            snapshot.Enabled ? "true" : "false",
            Value(snapshot.Profile),
            Value(snapshot.Carrier),
            snapshot.CatalogCount,
            Value(snapshot.GlobalMaterial),
            snapshot.GlobalLayerCount,
            snapshot.UnresolvedTokenCount,
            snapshot.AttachedControls,
            snapshot.PaintCount,
            snapshot.Revision);
    }

    /// <summary>未解析值明细（单行；断言失败时追加）。</summary>
    public static string DescribeUnresolved()
    {
        var values = Current.UnresolvedValues;
        return values.Count == 0 ? string.Empty : "unresolved=[" + string.Join(",", values.Take(6)) + "]";
    }

    private static string Value(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text;

    /// <summary>
    /// 是否字面色值（**兼容别名，非第二套判定**）：直接委托 <see cref="SurfaceColor.TryParseColor"/>，
    /// 与运行期/诊断同一实现。禁 <c>Color.FromName</c>（会放行 token 且系统色表与 CSS 不一致）。
    /// </summary>
    /// <param name="text">层值文本。</param>
    public static bool IsLiteralColor(string? text)
        => !string.IsNullOrWhiteSpace(text) && SurfaceColor.TryParseColor(text.Trim(), out _);
}
