#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Abstractions;

/// <summary>读取形态（全量 / 某类元素优先 / 窗口上下文）。</summary>
public enum UiReadShape
{
    Full = 0,
    ToolFirst = 1,

    /// <summary>窗口上下文：可见上下文（过滤内部过程帧），非全量帧。</summary>
    WindowContext = 2,
}

/// <summary>读取结果代数（空 ≠ 失败 ≠ 未装配；禁字符串哨兵）。</summary>
public enum UiReadState
{
    Ok = 0,
    Empty = 1,
    NotAttached = 2,
    Failed = 3,
}

/// <summary>读取结果：状态 + 值 + 显式原因（Ok 时原因为 null）。</summary>
public readonly record struct UiReadOutcome<T>(UiReadState State, T Value, string? Reason);

/// <summary>读取声明（作用域/键/形态/窗口/诊断键/三段文案）。</summary>
public sealed record UiReadSpec(
    string Scope,
    string Key,
    UiReadShape Shape,
    int TakeLast,
    string DiagnosticKey,
    string NotAttachedText,
    string EmptyText,
    string FailedText);

/// <summary>
/// 读取网关（通用件，唯一实现，**载荷无关**）：状态代数 + 形态投影 + 「先合帧、后切窗」单一切片。
/// <para>
/// 端口解析、失败诊断、帧合并、窗口裁剪均由领域以委托注入（本类不含 IO、不引用领域端口/消息类型），
/// 保证主视图 / 卡片窗口 / 浮层三面对同一实体给出同一 <see cref="UiReadState"/>。
/// </para>
/// </summary>
public static class UiReadGateway
{
    /// <summary>未装配（显式原因 = 声明文案）。</summary>
    public static UiReadOutcome<IReadOnlyList<T>> NotAttached<T>(UiReadSpec spec)
        => new(UiReadState.NotAttached, Array.Empty<T>(), spec.NotAttachedText);

    /// <summary>读取失败（原因优先用诊断失败串，缺省用声明文案）。</summary>
    public static UiReadOutcome<IReadOnlyList<T>> Failed<T>(UiReadSpec spec, string? failure)
        => new(UiReadState.Failed, Array.Empty<T>(),
            string.IsNullOrWhiteSpace(failure) ? spec.FailedText : failure);

    /// <summary>读取产物 → 代数（失败优先；无元素 ⇒ Empty）。</summary>
    public static UiReadOutcome<IReadOnlyList<T>> From<T>(UiReadSpec spec, IReadOnlyList<T>? items, string? failure)
    {
        if (items is null) return Failed<T>(spec, failure);
        return items.Count == 0
            ? new(UiReadState.Empty, Array.Empty<T>(), spec.EmptyText)
            : new(UiReadState.Ok, items, null);
    }

    /// <summary>
    /// 窗口代数（**唯一入口**）：可见谓词过滤后归约为 <see cref="From"/>；
    /// 空 ⇒ <paramref name="spec"/> 的 EmptyText（调用方不得再自行赋「窗口为空」原因）。
    /// </summary>
    /// <param name="visible">可见谓词（null ⇒ 不过滤）。</param>
    public static UiReadOutcome<IReadOnlyList<T>> FromWindow<T>(
        UiReadSpec spec, IReadOnlyList<T>? items, Func<T, bool>? visible, string? failure = null)
    {
        if (items is null) return Failed<T>(spec, failure);
        var kept = visible is null ? items : items.Where(visible).ToList();
        return From(spec, kept, failure);
    }

    /// <summary>
    /// 形态投影 + 窗口切片：<see cref="UiReadShape.ToolFirst"/> 且存在匹配元素 ⇒ 只保留匹配项；
    /// 窗口 &gt; 0 ⇒ **先并入观测帧、再裁一次**（禁二次切片）。
    /// <paramref name="preferred"/> = 形态匹配谓词（null ⇒ 不投影）；<paramref name="merge"/> = 帧合并；
    /// <paramref name="trim"/> = 窗口裁剪（域内单源实现）。
    /// </summary>
    public static async Task<UiReadOutcome<IReadOnlyList<T>>> WithShapeAndWindowAsync<T>(
        UiReadOutcome<IReadOnlyList<T>> outcome,
        UiReadSpec spec,
        string normalizedKey,
        Func<T, bool>? preferred,
        Func<IReadOnlyList<T>, string, CancellationToken, Task<IReadOnlyList<T>>> merge,
        Func<IReadOnlyList<T>, int, IReadOnlyList<T>> trim,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(trim);
        if (outcome.State != UiReadState.Ok) return outcome;

        IReadOnlyList<T> items = outcome.Value;
        if (spec.Shape == UiReadShape.ToolFirst && preferred is not null)
        {
            var filtered = items.Where(preferred).ToList();
            if (filtered.Count > 0) items = filtered;
        }
        else if (spec.Shape == UiReadShape.WindowContext && preferred is not null)
        {
            // 窗口上下文 = 可见谓词直接生效（不保留「全空则退全量」分支：空即空，由调用方给原因码）。
            items = items.Where(preferred).ToList();
        }

        if (spec.TakeLast > 0)
        {
            var merged = await merge(items, normalizedKey, ct).ConfigureAwait(false);
            items = trim(merged, spec.TakeLast);
        }

        return items.Count == 0
            ? new(UiReadState.Empty, Array.Empty<T>(), spec.EmptyText)
            : new(UiReadState.Ok, items, null);
    }
}
