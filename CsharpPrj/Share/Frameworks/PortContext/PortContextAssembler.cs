using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.PortContexts;

/// <summary>
/// 上下文装配器（唯一实现）：enumerate → window → state → merge。
/// <para>只读派生；失败转 <c>Empty(code)</c> 并到达出口（禁静默）；成员/窗口有界。</para>
/// <para>与 <c>[Flow] TransferContribute</c> 同构：四步语义一致，编排真源仍在声明面。</para>
/// </summary>
public sealed partial class PortContextAssembler : IPortContextPort
{
    private readonly IPortMemberKeySource? _members;
    private readonly IPortStateSource? _states;
    private readonly IPortWindowSource? _windows;

    /// <param name="members">成员键枚举端口（可空 ⇒ Empty(enumerator-not-attached)）。</param>
    /// <param name="states">成员状态供数端口（可空 ⇒ Empty(state-provider-not-attached)）。</param>
    /// <param name="windows">成员窗口供数端口（可空 ⇒ 窗口段空 + Empty(window-empty)）。</param>
    public PortContextAssembler(
        IPortMemberKeySource? members = null,
        IPortStateSource? states = null,
        IPortWindowSource? windows = null)
    {
        _members = members;
        _states = states;
        _windows = windows;
    }

    /// <inheritdoc />
    public Task<PortContext> GetAsync(string containerKey, PortContextSpec spec, CancellationToken ct = default)
        => GetAsync(PortContextKeyAxis.Container, containerKey, spec, ct);

    /// <inheritdoc />
    public async Task<PortContext> GetAsync(
        PortContextKeyAxis axis, string key, PortContextSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // UPC-I5：容器事实只读 ContainerKey；越轴不取数，直接可见化。
        var containerKey = PortContextKeyResolver.Resolve(axis, key);
        if (containerKey is null)
            return PortContext.Empty(key ?? string.Empty, PortContextEmptyCodes.KeyAxisMismatch);

        if (_members is null)
            return PortContext.Empty(containerKey, PortContextEmptyCodes.EnumeratorNotAttached);

        IReadOnlyList<string> enumerated;
        try
        {
            enumerated = await _members.EnumerateAsync(containerKey, spec.EdgeType, ct).ConfigureAwait(false)
                         ?? Array.Empty<string>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return PortContext.Empty(containerKey, PortContextEmptyCodes.EnumeratorNotAttached);
        }

        var memberKeys = Normalize(enumerated);
        if (memberKeys.Count == 0)
            return PortContext.Empty(containerKey, PortContextEmptyCodes.NoOwnedMember);

        // UPC-I6：有界（成员数 ≤ MaxMembers）。
        var truncated = memberKeys.Count > spec.EffectiveMaxMembers;
        var selected = truncated ? memberKeys.Take(spec.EffectiveMaxMembers).ToArray() : memberKeys;

        var stateByMember = await LoadStateAsync(containerKey, selected, ct).ConfigureAwait(false);
        var stateAttached = stateByMember is not null;
        stateByMember ??= new Dictionary<string, IReadOnlyList<PortContextField>>(StringComparer.Ordinal);

        var windowByMember = await LoadWindowAsync(containerKey, selected, spec.EffectiveWindowTake, ct)
            .ConfigureAwait(false);

        var members = new List<PortContextMember>(selected.Count);
        var merged = new List<PortContextField>();
        var anyFact = false;

        foreach (var memberKey in selected)
        {
            var stateFields = stateByMember.TryGetValue(memberKey, out var found)
                ? PortContextOrdering.Order(Filter(spec, found))
                : Array.Empty<PortContextField>();
            var windows = windowByMember.TryGetValue(memberKey, out var window)
                ? window
                : Array.Empty<PortContextWindowItem>();

            var memberFields = new List<PortContextField>(stateFields.Count + windows.Count);
            memberFields.AddRange(stateFields);
            if (Allowed(spec, PortContextFieldClasses.Window))
            {
                foreach (var item in windows)
                {
                    memberFields.Add(new PortContextField(
                        PortContextFieldClasses.Window, PortContextVolatility.Stream, "window", item.Text));
                }
            }

            if (memberFields.Count > 0) anyFact = true;

            var emptyCode = !stateAttached
                ? PortContextEmptyCodes.StateProviderNotAttached
                : (memberFields.Count == 0 ? PortContextEmptyCodes.WindowEmpty : null);

            members.Add(new PortContextMember(memberKey, stateFields, windows, emptyCode, truncated));
            merged.AddRange(memberFields);
        }

        // 变化频率升序（排序单点 = PortContextOrdering：静态/慢变前置、追加流置尾，供前缀缓存最大化）。
        var ordered = PortContextOrdering.Order(merged);

        var contextEmpty = !stateAttached
            ? PortContextEmptyCodes.StateProviderNotAttached
            : (anyFact ? null : PortContextEmptyCodes.WindowEmpty);

        return new PortContext(
            containerKey,
            members,
            ordered,
            contextEmpty,
            Synthetic: false,
            PortContext.Fingerprint(containerKey, contextEmpty, selected),
            Truncated: truncated,
            TruncationCode: truncated ? PortContextEmptyCodes.MemberWindowTruncated : null);
    }

    /// <summary>
    /// 单成员分页（尾部下推 + 无状态游标）：解码失败 ⇒ <c>cursor-invalid</c>；条数夹到规格上限；
    /// <c>HasOlder/HasNewer</c> 与双向游标由「多读 1 条」探测（不二次取数）。
    /// </summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="request">分页请求。</param>
    /// <param name="spec">上下文规格。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<PortContextPage> GetPageAsync(
        string containerKey, PortContextPageRequest request, PortContextSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(request);

        var container = PortContextKeyResolver.Resolve(PortContextKeyAxis.Container, containerKey);
        if (container is null)
            return PortContextPage.Empty(containerKey ?? string.Empty, request.MemberKey ?? string.Empty,
                PortContextEmptyCodes.KeyAxisMismatch);

        var memberKey = (request.MemberKey ?? string.Empty).Trim();
        if (memberKey.Length == 0)
            return PortContextPage.Empty(container, string.Empty, PortContextEmptyCodes.KeyAxisMismatch);

        if (!PortContextCursor.TryDecode(request.Cursor, memberKey, out var skipFromEnd))
            return PortContextPage.Empty(container, memberKey, PortContextEmptyCodes.CursorInvalid);

        if (_windows is null)
            return PortContextPage.Empty(container, memberKey, PortContextEmptyCodes.StateProviderNotAttached);

        var take = request.Take > 0 ? Math.Min(request.Take, spec.EffectiveWindowTake) : spec.EffectiveWindowTake;

        IReadOnlyList<PortContextWindowItem> fetched;
        try
        {
            // 多读 1 条只用于 HasOlder 判定（多出的首条在更早一侧，窗口本体须 Skip(1)）。
            fetched = await _windows
                .GetWindowPageAsync(container, memberKey, take + 1, skipFromEnd, ct)
                .ConfigureAwait(false) ?? Array.Empty<PortContextWindowItem>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return PortContextPage.Empty(container, memberKey, PortContextEmptyCodes.WindowEmpty);
        }

        // 多读 1 条在更早一侧（Older 探测）：多出的首条须跳过，窗口本体 = Skip(1).Take(take)。
        var hasOlderProbe = fetched.Count > take;
        var hasOlder = hasOlderProbe
                       && skipFromEnd + take <= PortContextCursor.MaxOffsetFromEnd;
        var items = hasOlderProbe ? fetched.Skip(1).Take(take).ToArray() : fetched;
        var hasNewer = skipFromEnd > 0;
        var olderCursor = hasOlder ? PortContextCursor.Encode(memberKey, skipFromEnd + take) : null;
        var newerCursor = hasNewer
            ? PortContextCursor.Encode(memberKey, Math.Max(0, skipFromEnd - take))
            : null;

        if (items.Count == 0)
        {
            // 首页空 ⇒ 空因可见；翻页到底 ⇒ 正常结束（非错误；保留更晚游标以便回退）。
            return skipFromEnd == 0
                ? PortContextPage.Empty(container, memberKey, PortContextEmptyCodes.WindowEmpty)
                : new PortContextPage(container, memberKey, Array.Empty<PortContextWindowItem>(),
                    null, newerCursor, false, hasNewer, skipFromEnd, take, null, false);
        }

        return new PortContextPage(
            container,
            memberKey,
            items,
            olderCursor,
            newerCursor,
            hasOlder,
            hasNewer,
            skipFromEnd,
            take,
            EmptyCode: null,
            Truncated: hasOlder);
    }
}
