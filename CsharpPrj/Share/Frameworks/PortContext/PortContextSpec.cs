using System;
using System.Collections.Generic;

namespace Share.PortContexts;

/// <summary>上下文规格（声明面派生）：容器层/成员层/边类型/上限/窗口预算/字段族/空策略/消费面。</summary>
/// <param name="ContainerLevel">容器层 Id（如 history）。</param>
/// <param name="MemberLevel">成员层 Id（如 task）。</param>
/// <param name="EdgeType">成员归属边类型（如 history_owns_task）。</param>
/// <param name="MaxMembers">成员数上限（≤0 ⇒ 有界缺省 32）。</param>
/// <param name="WindowTake">每成员窗口条数（≤0 ⇒ 有界缺省 6）。</param>
/// <param name="FieldClasses">字段族（code/semantic/window；空 ⇒ 全族）。</param>
/// <param name="EmptyPolicy">空策略（visible / not-applicable）。</param>
/// <param name="Consumers">消费面 Id（ui.board / prompt.runtime / diag.badge）。</param>
public sealed record PortContextSpec(
    string ContainerLevel,
    string MemberLevel,
    string EdgeType,
    int MaxMembers,
    int WindowTake,
    IReadOnlyList<string> FieldClasses,
    string EmptyPolicy,
    IReadOnlyList<string> Consumers)
{
    /// <summary>有界缺省成员上限。</summary>
    public const int DefaultMaxMembers = 32;

    /// <summary>有界缺省窗口条数。</summary>
    public const int DefaultWindowTake = 6;

    /// <summary>有效成员上限（≤0 ⇒ 缺省；禁无界）。</summary>
    public int EffectiveMaxMembers => MaxMembers > 0 ? MaxMembers : DefaultMaxMembers;

    /// <summary>有效窗口预算（≤0 ⇒ 缺省；禁无界）。</summary>
    public int EffectiveWindowTake => WindowTake > 0 ? WindowTake : DefaultWindowTake;

    /// <summary>未声明规格（调用方须显式失败或显式降级，禁伪造领域值）。</summary>
    public static PortContextSpec Unspecified { get; } = new(
        string.Empty, string.Empty, string.Empty,
        DefaultMaxMembers, DefaultWindowTake,
        Array.Empty<string>(), PortContextEmptyPolicy.Visible, Array.Empty<string>());
}

/// <summary>字段族受控词（单源；词表真源 = Config/nodes/transfer.ushar）。</summary>
public static class PortContextFieldClasses
{
    /// <summary>代码层固定状态。</summary>
    public const string Code = "code";

    /// <summary>语义层派生摘要。</summary>
    public const string Semantic = "semantic";

    /// <summary>窗口层（成员会话窗口片段）。</summary>
    public const string Window = "window";

    /// <summary>全部字段族。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Code, Semantic, Window };
}

/// <summary>稳定度受控词（Stable→前缀 / Volatile→尾部）。</summary>
public static class PortContextVolatility
{
    /// <summary>跨帧逐字稳定。</summary>
    public const string Stable = "stable";

    /// <summary>逐帧变化。</summary>
    public const string Volatile = "volatile";
}

/// <summary>空策略受控词。</summary>
public static class PortContextEmptyPolicy
{
    /// <summary>空必可达（显式占位 + 原因码）。</summary>
    public const string Visible = "visible";

    /// <summary>该层/该声明不适用（⊥，不渲染）。</summary>
    public const string NotApplicable = "not-applicable";
}

/// <summary>空因码（受控词单源；禁在出口侧手写文案充当原因）。</summary>
public static class PortContextEmptyCodes
{
    /// <summary>成员枚举为空（边不存在/未挂载）。</summary>
    public const string NoOwnedMember = "no-owned-member";

    /// <summary>成员枚举端口未装配。</summary>
    public const string EnumeratorNotAttached = "enumerator-not-attached";

    /// <summary>运行态供数未装配。</summary>
    public const string StateProviderNotAttached = "state-provider-not-attached";

    /// <summary>有成员但窗口为空。</summary>
    public const string WindowEmpty = "window-empty";

    /// <summary>仅合成壳（无真实行）。</summary>
    public const string SyntheticOnly = "synthetic-only";

    /// <summary>容器键/成员键轴不匹配。</summary>
    public const string KeyAxisMismatch = "key-axis-mismatch";

    /// <summary>窗口/成员被预算截断（Present 但带截断标记）。</summary>
    public const string MemberWindowTruncated = "member-window-truncated";

    /// <summary>全部空因码（门禁遍历单源）。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        NoOwnedMember, EnumeratorNotAttached, StateProviderNotAttached,
        WindowEmpty, SyntheticOnly, KeyAxisMismatch, MemberWindowTruncated,
    };

    /// <summary>空因码是否受控（未登记 ⇒ 判据不可判定）。</summary>
    /// <param name="code">空因码。</param>
    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && System.Linq.Enumerable.Contains(All, code!, StringComparer.Ordinal);
}
