using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.PortContexts;

/// <summary>键轴（四轴分离；容器事实只读 ContainerKey，成员事实只读 MemberKey）。</summary>
public enum PortContextKeyAxis
{
    /// <summary>容器实体键（成员枚举、上下文装配入口）。</summary>
    Container,

    /// <summary>成员实体键（成员窗口/状态读取）。</summary>
    Member,

    /// <summary>运行实例键（仅会话管线内部）。</summary>
    Session,

    /// <summary>分发作用域键（分发/治理，不用于容器上下文读取）。</summary>
    Scope,
}

/// <summary>键轴解析（单源；越轴 ⇒ key-axis-mismatch，禁调用点自选轴）。</summary>
public static class PortContextKeyResolver
{
    /// <summary>该轴是否可承载容器事实（唯一允许的装配入口轴）。</summary>
    /// <param name="axis">键轴。</param>
    public static bool IsContainerFactAxis(PortContextKeyAxis axis)
        => axis == PortContextKeyAxis.Container;

    /// <summary>取轴对应的键（空 ⇒ null，不伪造）。</summary>
    /// <param name="axis">键轴。</param>
    /// <param name="key">键值。</param>
    public static string? Resolve(PortContextKeyAxis axis, string? key)
        => IsContainerFactAxis(axis) && !string.IsNullOrWhiteSpace(key) ? key!.Trim() : null;
}

/// <summary>成员键枚举端口（实现方 = 归属边枚举；本内核零依赖）。</summary>
public interface IPortMemberKeySource
{
    /// <summary>按边类型枚举容器成员键（只读；空 ⇒ 空集，不伪造）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="edgeType">成员归属边类型。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<string>> EnumerateAsync(string containerKey, string edgeType, CancellationToken ct = default);
}

/// <summary>单成员状态行（成员键 + 分类字段）。</summary>
public sealed record PortContextStateRow(string MemberKey, IReadOnlyList<PortContextField> Fields);

/// <summary>成员运行态供数端口（实现方 = 运行态投影；本内核零依赖）。</summary>
public interface IPortStateSource
{
    /// <summary>批量取成员状态字段（只读；缺失成员 ⇒ 不出行，不伪造）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="memberKeys">成员键集。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<PortContextStateRow>> GetStateAsync(
        string containerKey, IReadOnlyList<string> memberKeys, CancellationToken ct = default);
}

/// <summary>成员窗口供数端口（实现方 = 成员窗口读取；本内核零依赖）。</summary>
public interface IPortWindowSource
{
    /// <summary>批量取成员窗口片段（有界 take；无窗口成员 ⇒ 不出行）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="memberKeys">成员键集。</param>
    /// <param name="take">每成员窗口条数（≤0 由实现方取有界缺省）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<PortContextWindowItem>> GetWindowAsync(
        string containerKey, IReadOnlyList<string> memberKeys, int take, CancellationToken ct = default);

    /// <summary>
    /// 单成员分页窗口（尾部跳过 <paramref name="skipFromEnd"/> 条后取 <paramref name="take"/> 条）。
    /// <para>切片实现单源 = 实现方（<c>WindowSlice</c>）；本端口不提供默认实现（禁第二份尾部切片算法）。</para>
    /// </summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="memberKey">成员键（单成员）。</param>
    /// <param name="take">条数（含 hasMore 探测位；调用方决定）。</param>
    /// <param name="skipFromEnd">离尾跳过条数（0 = 首页）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<PortContextWindowItem>> GetWindowPageAsync(
        string containerKey, string memberKey, int take, int skipFromEnd, CancellationToken ct = default);
}

/// <summary>上下文唯一获取口（三出口都只此一个口；禁各自再取数）。</summary>
public interface IPortContextPort
{
    /// <summary>按容器键装配（唯一合法入口轴 = ContainerKey）。</summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="spec">上下文规格。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PortContext> GetAsync(string containerKey, PortContextSpec spec, CancellationToken ct = default);

    /// <summary>带轴装配（非 Container 轴 ⇒ 空因 key-axis-mismatch，不取数）。</summary>
    /// <param name="axis">键轴。</param>
    /// <param name="key">键值。</param>
    /// <param name="spec">上下文规格。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PortContext> GetAsync(
        PortContextKeyAxis axis, string key, PortContextSpec spec, CancellationToken ct = default);

    /// <summary>
    /// 单成员分页（唯一获取口之二：分页不得另起第二读法）。
    /// <para>默认实现回退 <see cref="GetAsync"/> 的该成员窗口（首页口径，<c>HasMore=false</c>）；
    /// 具备尾部下推/游标的实现（装配器）覆盖本方法。</para>
    /// </summary>
    /// <param name="containerKey">容器键。</param>
    /// <param name="request">分页请求（成员键 + 游标 + 条数）。</param>
    /// <param name="spec">上下文规格。</param>
    /// <param name="ct">取消令牌。</param>
    async Task<PortContextPage> GetPageAsync(
        string containerKey, PortContextPageRequest request, PortContextSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await GetAsync(containerKey, spec, ct).ConfigureAwait(false);
        var memberKey = (request.MemberKey ?? string.Empty).Trim();
        var key = containerKey ?? string.Empty;
        if (memberKey.Length == 0)
            return PortContextPage.Empty(key, memberKey, PortContextEmptyCodes.KeyAxisMismatch);

        var member = context.Members.FirstOrDefault(
            m => string.Equals(m.MemberKey, memberKey, StringComparison.Ordinal));
        if (member is null)
            return PortContextPage.Empty(key, memberKey, PortContextEmptyCodes.NoOwnedMember);

        return member.WindowItems.Count == 0
            ? PortContextPage.Empty(key, memberKey, PortContextEmptyCodes.WindowEmpty)
            : new PortContextPage(key, memberKey, member.WindowItems, null, false, null, member.Truncated);
    }
}
