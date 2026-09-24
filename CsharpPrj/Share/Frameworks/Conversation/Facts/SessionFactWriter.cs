using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts;

/// <summary>
/// 事实**唯一写入口**（方案 §2.3）—— 键归一 + 序号分配 + 幂等 + 外部引用登记 + 失败可见化，全部经此单点。
/// <para>
/// 不变量：F1 单写者（其他落点禁直连 <c>ISessionFactStore.AppendAsync</c>）；F2 只增不丢（无 update/delete）；
/// F3 序号单调无空洞；F4 幂等（同 Key + Kind + 载荷只落一条）；F5 归属完备（键缺失 / 非法 / run 级 ⇒ 拒收并可见化）；
/// F6 键不可变（轮内恒定，重派发不换键）。
/// </para>
/// <para>
/// 失败语义：写失败 ⇒ <see cref="SessionFactDiagnostics.EmitFailed"/> + 转兜底存储（若装配）+ 可见化，**绝不吞**；
/// 存储未装配 ⇒ 由组合根注入本地 JSONL 兜底并 <see cref="SessionFactDiagnostics.UnwiredVisible"/>。
/// </para>
/// </summary>
public sealed class SessionFactWriter
{
    private readonly ISessionFactStore _store;
    private readonly ISessionFactStore? _fallback;
    private readonly SessionFactDiagnostics _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DocumentState> _states = new(StringComparer.Ordinal);

    /// <summary>构造（唯一写入口）。</summary>
    /// <param name="store">事实存储（未装配真实存储时由组合根给本地 JSONL 兜底）。</param>
    /// <param name="diagnostics">诊断出口（缺省新建）。</param>
    /// <param name="fallback">兜底存储（主存储写失败时降级；null = 无兜底，仅可见化）。</param>
    public SessionFactWriter(ISessionFactStore store, SessionFactDiagnostics? diagnostics = null,
        ISessionFactStore? fallback = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diagnostics = diagnostics ?? new SessionFactDiagnostics();
        _fallback = ReferenceEquals(fallback, store) ? null : fallback;
    }

    /// <summary>诊断面（供组合根 / GUI 查询「存储是否真装配」）。</summary>
    public SessionFactDiagnostics Diagnostics => _diagnostics;

    /// <summary>
    /// 追加一条事实（唯一写入路径）。
    /// </summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="payloadJson">载荷（浅层 JSON 对象；非法即拒收并可见化）。</param>
    /// <param name="key">归属键（轮级；run 级 / 非法即拒收并可见化）。</param>
    /// <param name="actor">执行体（如 <c>agent/cline@worker-1</c>）。</param>
    /// <param name="source">事实来源。</param>
    /// <param name="authority">权威级。</param>
    /// <param name="externalRefs">外部代号（只作属性）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task EmitAsync(
        string kind,
        string payloadJson,
        FactId key,
        string? actor = null,
        FactSource source = FactSource.Csharp,
        FactAuthority authority = FactAuthority.Primary,
        IReadOnlyList<ExternalRef>? externalRefs = null,
        CancellationToken ct = default)
    {
        // F5 归属完备：键缺失 / 非法 / 未定位到轮 ⇒ 拒收（禁「无归属事实」混入）。
        var keyText = KeyTextOf(key);
        if (keyText is null)
        {
            _diagnostics.Rejected(kind, "<归属键缺失或非法>", "F5 归属完备：事实须有合法轮级键");
            return;
        }

        if (!FactPayload.IsWellFormed(payloadJson))
        {
            _diagnostics.Rejected(kind, keyText, "载荷非法（须为 JSON 对象）—— 不产事实");
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(key, ct).ConfigureAwait(false);
            var fingerprint = FactEnvelope.FingerprintOf(kind, payloadJson);
            if (!state.Fingerprints.Add(fingerprint))
            {
                _diagnostics.Deduplicated(kind, keyText);
                return;
            }

            var seq = state.NextSeq;
            var fact = FactEnvelope.Create(key, kind, seq, payloadJson, actor, source, authority, externalRefs);

            try
            {
                await _store.AppendAsync(fact, ct).ConfigureAwait(false);
                state.NextSeq = seq + 1;
            }
            catch (Exception ex)
            {
                state.Fingerprints.Remove(fingerprint);
                _diagnostics.EmitFailed(kind.ToString(), keyText, ex);
                if (_fallback is null) return;

                try
                {
                    await _fallback.AppendAsync(fact, ct).ConfigureAwait(false);
                    state.NextSeq = seq + 1;
                    _diagnostics.FallbackUsed(kind.ToString(), keyText, ex.Message);
                }
                catch (Exception fallbackError)
                {
                    _diagnostics.EmitFailed(kind.ToString(), keyText, fallbackError);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 同步上下文专用的**非阻塞**写入（观测桥 / 事件回调等无 async 上下文处）：
    /// 仅启动写入任务即返回；失败已在 <see cref="EmitAsync"/> 内可见化兜底（不产生未观察异常）。
    /// <para>顺序说明：序号仍由本类单锁分配，故同文档内的相对顺序与调用顺序一致；落盘完成时刻不保证同步。</para>
    /// </summary>
    /// <param name="kind">事实类别。</param>
    /// <param name="payloadJson">载荷。</param>
    /// <param name="key">归属键（轮级）。</param>
    /// <param name="actor">执行体。</param>
    /// <param name="source">事实来源。</param>
    /// <param name="authority">权威级。</param>
    /// <param name="externalRefs">外部代号。</param>
    public void EmitFireAndForget(
        string kind,
        string payloadJson,
        FactId key,
        string? actor = null,
        FactSource source = FactSource.Csharp,
        FactAuthority authority = FactAuthority.Primary,
        IReadOnlyList<ExternalRef>? externalRefs = null)
        => _ = EmitAsync(kind, payloadJson, key, actor, source, authority, externalRefs);

    /// <summary>按轮键读回该轮事实（主存储优先，失败 / 空则试兜底存储）——供「先写后删」断言与 GUI 取数。</summary>
    /// <param name="key">归属键（轮级）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<FactEnvelope>> LoadAsync(FactId key, CancellationToken ct = default)
    {
        if (KeyTextOf(key) is null) return Array.Empty<FactEnvelope>();

        try
        {
            var facts = await _store.LoadAsync(key, ct).ConfigureAwait(false);
            if (facts is { Count: > 0 }) return facts;
        }
        catch (Exception ex)
        {
            _diagnostics.EmitFailed("Load", key.ToString(), ex);
        }

        if (_fallback is null) return Array.Empty<FactEnvelope>();
        try
        {
            return await _fallback.LoadAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.EmitFailed("Load", key.ToString(), ex);
            return Array.Empty<FactEnvelope>();
        }
    }

    /// <summary>读回并构造文档视图（按轮；无事实时为空文档，**不伪造**）。</summary>
    /// <param name="key">归属键（轮级）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<FactDocument> LoadDocumentAsync(FactId key, CancellationToken ct = default)
        => FactDocument.Of(key, await LoadAsync(key, ct).ConfigureAwait(false));

    /// <summary>键文本（不合法 / 未定位到轮 ⇒ null）。</summary>
    private static string? KeyTextOf(FactId key)
    {
        if (key.IsRunKey) return null;
        return FactIdGrammar.TryParse(key.ToString(), out var parsed) && !parsed.IsRunKey
            ? parsed.ToString()
            : null;
    }

    /// <summary>首次写入该文档时装载既有事实，回填序号与幂等指纹（F3 / F4 的持久化一致性）。</summary>
    private async Task<DocumentState> EnsureStateAsync(FactId key, CancellationToken ct)
    {
        var keyText = key.ToString();
        if (_states.TryGetValue(keyText, out var cached)) return cached;

        var state = new DocumentState();
        foreach (var fact in await LoadAsync(key, ct).ConfigureAwait(false))
        {
            state.Fingerprints.Add(fact.Fingerprint);
            if (fact.Seq >= state.NextSeq) state.NextSeq = fact.Seq + 1;
        }

        _states[keyText] = state;
        return state;
    }

    /// <summary>文档写入状态（序号 + 幂等指纹；进程内缓存，首次访问自存储回填）。</summary>
    private sealed class DocumentState
    {
        /// <summary>下一个可用序号。</summary>
        public long NextSeq { get; set; }

        /// <summary>已落事实指纹（F4 去重集）。</summary>
        public HashSet<string> Fingerprints { get; } = new(StringComparer.Ordinal);
    }
}
