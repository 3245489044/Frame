using System;
using Share.Conversation.Facts.Stores;

namespace Share.Conversation.Facts;

/// <summary>
/// 事实层诊断（**未装配即失败可见**，方案 §2.7）——事实完整性不得静默丢失。
/// <para>
/// 依赖边界：<c>Agent.Session</c> 不引用日志框架（<c>Agent.Axioms.Diagnostics</c> 在更高层），
/// 故可见化经**静态委托端口** <see cref="Sink"/> 上抛，由组合根（<c>Agent.Launcher</c>）装配接既有日志门面
/// （先例：<c>ClineClientDiagnostics.Emit</c> 同款钩子）。
/// </para>
/// <para>
/// 规则（对齐 <c>AX-CORE.SyncProtocolTriple</c>：Observe 段不可省）：
/// ① 存储未装配 ⇒ 本地 JSONL 兜底 + <see cref="UnwiredVisible"/> 一条可见化（不再「返回 false 静默丢」）；
/// ② 写失败 ⇒ <see cref="EmitFailed"/> + 转兜底存储；③ 归属 / 载荷非法 ⇒ <see cref="Rejected"/>（拒收并可见化）。
/// </para>
/// </summary>
public sealed class SessionFactDiagnostics
{
    /// <summary>可见化出口（宿主装配接日志门面；null = 无出口，此时仅保留本地事实兜底）。</summary>
    public static Action<string, object?>? Sink { get; set; }

    /// <summary>是否已装配真实事实存储（false = 走本地兜底，事件完整性降级但**不失真**）。</summary>
    public bool IsStoreWired { get; private set; }

    /// <summary>清空静态出口（用例隔离；**不**影响已装配状态）。</summary>
    public static void Clear() => Sink = null;

    /// <summary>全局可见化（存储实现内部使用，如破损行丢弃）。</summary>
    /// <param name="eventName">事件名。</param>
    /// <param name="payload">载荷。</param>
    public static void Report(string eventName, object? payload = null) => Sink?.Invoke(eventName, payload);

    /// <summary>标记真实存储已装配。</summary>
    /// <param name="where">装配点（日志定位用）。</param>
    public void MarkStoreWired(string where)
    {
        IsStoreWired = true;
        Report("SessionFact_StoreWired", new { where });
    }

    /// <summary>存储未装配（走本地兜底）可见化。</summary>
    /// <param name="where">发生点。</param>
    public void UnwiredVisible(string where)
    {
        IsStoreWired = false;
        Report("SessionFact_StoreUnwired", new { where, fallback = JsonlSessionFactStore.RootFolderName });
    }

    /// <summary>写入失败可见化（绝不吞）。</summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="key">归属键文本。</param>
    /// <param name="ex">异常。</param>
    public void EmitFailed(string kind, string key, Exception ex)
    {
        IsStoreWired = false;
        Report("SessionFact_EmitFailed",
            new { kind, key, error = ex.Message, type = ex.GetType().Name });
    }

    /// <summary>已转兜底存储可见化。</summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="key">归属键文本。</param>
    /// <param name="reason">降级原因。</param>
    public void FallbackUsed(string kind, string key, string reason)
        => Report("SessionFact_FallbackUsed", new { kind, key, reason });

    /// <summary>事实被拒收可见化（归属不完备 F5 / 载荷非法）。</summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="key">归属键文本（非法时为诊断占位）。</param>
    /// <param name="reason">拒收原因。</param>
    public void Rejected(string kind, string key, string reason)
        => Report("SessionFact_Rejected", new { kind, key, reason });

    /// <summary>幂等去重可见化（同 Key + Kind + 载荷重复 Emit）。</summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="key">归属键文本。</param>
    public void Deduplicated(string kind, string key)
        => Report("SessionFact_Deduplicated", new { kind, key });
}
