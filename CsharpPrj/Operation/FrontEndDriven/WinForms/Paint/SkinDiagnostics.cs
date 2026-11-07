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
    /// <summary>参与色值解析的图元（其余图元用 asset/纯数值，不参与 token 判定）。</summary>
    private static readonly HashSet<string> ColorPrimitives =
        new(StringComparer.Ordinal) { "fill", "gradient", "stroke", "shadow", "innerShadow", "backdrop", "specular" };

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

    /// <summary>绘制记录（每次 owner-draw 绘制；记跳过项与**实际落笔图层数**，不拿处理器次数冒充绘制覆盖）。</summary>
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
    /// 统计「层值无法解析为颜色」的条目（token 未换值的结构指标）。
    /// 判据载体 = 层值文本本身（含 <c>gradient</c> 逗号分隔），不含执行者叙述。
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
            foreach (var layer in material.Layers)
            {
                if (!ColorPrimitives.Contains(layer.Primitive)) continue;
                if (!layer.Params.TryGetValue("value", out var value) || string.IsNullOrWhiteSpace(value)) continue;

                var parts = string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal)
                    ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { value.Trim() };

                foreach (var part in parts)
                {
                    if (IsLiteralColor(part)) continue;
                    unresolved.Add(material.MaterialId + "/" + layer.Role + "=" + part);
                }
            }
        }
    }

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
    /// 是否字面色值（**与运行期解析器同源**：直接委托 <see cref="SurfaceColor"/>，不另写一套判据）。
    /// <para>禁 <see cref="Color.FromName"/>：它会把未换值的 token（如 <c>surface_plain</c>）误判成合法颜色；
    /// 也禁「纯数值算颜色」，数值是宽度/角度等参数，不是色值。</para>
    /// </summary>
    /// <param name="text">层值文本。</param>
    public static bool IsLiteralColor(string? text)
        => !string.IsNullOrWhiteSpace(text) && SurfaceColor.TryParseColor(text.Trim(), out _);
}
