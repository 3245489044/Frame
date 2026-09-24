using System;

namespace Share.Conversation.Ids;

/// <summary>
/// 外部代号受控词（Kind）：**只作属性，禁参与关联**（方案 §1.5 对 ID-2/4/5 的处置口径）。
/// 词表声明面 = <c>Config\session-facts.ushar</c> 的 <c>[Space] ExternalRefKind</c>（P0-5 单源）。
/// </summary>
public static class ExternalRefKinds
{
    /// <summary>TS SDK 内部会话代号（<c>conv_*</c> / 会话时间戳 id）。</summary>
    public const string TsSdk = "TsSdk";

    /// <summary>TS daemon 控制面代号（worker 名等）。</summary>
    public const string TsDaemon = "TsDaemon";

    /// <summary>前端 ECS 操作键（<c>opId</c>；仅幂等位，不进事实归属）。</summary>
    public const string Ecs = "Ecs";

    /// <summary>框架轮次租约代号（<c>{taskKey}#{gen}#{seq}</c>；无轮号信息）。</summary>
    public const string Lease = "Lease";

    /// <summary>是否为已登记受控词（未知词即编程错误，装配期 fail-fast 依据）。</summary>
    /// <param name="kind">代号类别。</param>
    public static bool IsKnown(string? kind)
        => kind is TsSdk or TsDaemon or Ecs or Lease;
}

/// <summary>
/// 外部代号容器（值对象）：承载「不属于本轴、但需留痕」的外部标识。
/// <para>
/// 纪律：外部代号**不具备标识资格** —— 会话键恒为框架会话键（<c>RunKey == TaskKey == HistoryId</c>），
/// 外部代号只进 <c>SessionFactEnvelope.ExternalRefs</c> 供跨介质对照（如 hooks 的 <c>taskId</c> 与
/// 框架 <c>RoundKey</c> 在同一文档内 join）。
/// </para>
/// </summary>
/// <param name="Kind">代号类别（<see cref="ExternalRefKinds"/> 受控词）。</param>
/// <param name="Value">代号原值（原样保留，不改写）。</param>
public readonly record struct ExternalRef(string Kind, string Value)
{
    /// <summary>是否形态完备（类别与值均非空）。</summary>
    public bool IsWellFormed => !string.IsNullOrWhiteSpace(Kind) && !string.IsNullOrWhiteSpace(Value);

    /// <summary>构造（未登记类别即抛：把「第六套 ID」挡在入口）。</summary>
    /// <param name="kind">代号类别（受控词）。</param>
    /// <param name="value">代号值（非空）。</param>
    /// <exception cref="ArgumentException">类别未登记或值为空。</exception>
    public static ExternalRef Create(string kind, string value)
    {
        if (!ExternalRefKinds.IsKnown(kind))
            throw new ArgumentException($"未登记的外部代号类别 '{kind}'（受控词见 ExternalRefKinds）", nameof(kind));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("外部代号值不得为空", nameof(value));
        return new ExternalRef(kind, value);
    }

    /// <summary>TS SDK 内部会话代号。</summary>
    /// <param name="value">代号值。</param>
    public static ExternalRef TsSdk(string value) => Create(ExternalRefKinds.TsSdk, value);

    /// <summary>TS daemon 控制面代号。</summary>
    /// <param name="value">代号值。</param>
    public static ExternalRef TsDaemon(string value) => Create(ExternalRefKinds.TsDaemon, value);

    /// <summary>前端 ECS 操作键。</summary>
    /// <param name="value">代号值。</param>
    public static ExternalRef Ecs(string value) => Create(ExternalRefKinds.Ecs, value);

    /// <summary>框架轮次租约代号。</summary>
    /// <param name="value">代号值。</param>
    public static ExternalRef Lease(string value) => Create(ExternalRefKinds.Lease, value);

    /// <summary>诊断串（<c>Kind=Value</c>）。</summary>
    public override string ToString() => $"{Kind}={Value}";
}
