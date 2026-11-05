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
                ? Filter(spec, found)
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
                        PortContextFieldClasses.Window, PortContextVolatility.Volatile, "window", item.Text));
                }
            }

            if (memberFields.Count > 0) anyFact = true;

            var emptyCode = !stateAttached
                ? PortContextEmptyCodes.StateProviderNotAttached
                : (memberFields.Count == 0 ? PortContextEmptyCodes.WindowEmpty : null);

            members.Add(new PortContextMember(memberKey, stateFields, windows, emptyCode, truncated));
            merged.AddRange(memberFields);
        }

        // 稳定段在前、易变段在后（同稳定度保声明序；供前缀缓存最大化）。
        var ordered = merged
            .OrderBy(static field => string.Equals(field.Volatility, PortContextVolatility.Stable, StringComparison.Ordinal) ? 0 : 1)
            .ToArray();

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
}
