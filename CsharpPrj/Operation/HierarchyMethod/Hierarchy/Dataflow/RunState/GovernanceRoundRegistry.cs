using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow.RunState
{
    /// <summary>
    /// 会话轮次租约 / 在途登记面（C1 · 缺口 G2）：把「派发无租约、不可取消、无在途登记」补成**可判据的事实源**——
    /// 派发必经 <see cref="Acquire"/>（无登记不得进入 running，对齐 <c>[Matrix] RoundDispatch</c>），
    /// 同 task 在途再派发可被 <c>RG1009</c> 判出（禁静默合并 / 禁叠加计费）。
    /// <para>
    /// 边界（三红线）：本类只承载**登记与事实投影**（纯状态 + 判据可见化），不 spawn / 不 kill / 不落库；
    /// 真实进程取消与落库由宿主端口复用既有通道（<c>engine_cancel</c> / <c>outbox_commit</c>）。
    /// </para>
    /// <para>
    /// 接线口径：C1 只落「端口 + 契约 + 登记面」，**不改变**既有会话派发路径（行为替换属 C2 单）；
    /// 故 进程级 ambient 单例在 C2 接线前无生产调用方，在途数仅作观测取数。
    /// </para>
    /// </summary>
    public sealed class GovernanceRoundRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, RoundLease> _byLease = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byTask = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _roundToLease = new(StringComparer.Ordinal);
        private long _sequence;
        private int _generationCursor;

        /// <summary>一条在途轮次租约（登记事实；<see cref="InputDigest"/> 供同输入重复派发对账）。</summary>
        /// <param name="LeaseId">租约键（派发前置；缺租约不得 running）。</param>
        /// <param name="TaskKey">任务键（同 task 在途判定键）。</param>
        /// <param name="RoundId">轮次键（宿主生成权威）。</param>
        /// <param name="Generation">派发代次（越代请求拒绝）。</param>
        /// <param name="Owner">归属方（终止事实可归因）。</param>
        /// <param name="InputDigest">输入摘要（同输入重复派发检出；空 = 尚未派发）。</param>
        /// <param name="PersistSeq">最近一次既有落库序号（本类只登记，不新增写路径）。</param>
        /// <param name="TokensIn">本租约累计输入 token（C2：由行内轮次作用域归集，单一记分牌）。</param>
        /// <param name="TokensOut">本租约累计输出 token（C2）。</param>
        /// <param name="Dispatched">登记时刻。</param>
        /// <param name="ActorKind">执行体类别（Live 运行态 Actor.Kind 受控词；空白 = 未登记）。</param>
        /// <param name="ActorId">执行体标识（如 cline / answer；空白 = 未登记）。</param>
        public sealed record RoundLease(
            string LeaseId,
            string TaskKey,
            string RoundId,
            int Generation,
            string? Owner,
            string InputDigest,
            long PersistSeq,
            long TokensIn,
            long TokensOut,
            DateTimeOffset Dispatched,
            string? ActorKind = null,
            string? ActorId = null)
        {
            /// <summary>是否已进入派发登记（未登记 = 只取到租约、尚未派发）。</summary>
            public bool IsDispatched => !string.IsNullOrEmpty(InputDigest);

            /// <summary>累计 token 用量（in+out；成本机械复算的分子）。</summary>
            public long TokensTotal => TokensIn + TokensOut;

            /// <summary>是否已登记执行体（Live 运行态可给出"谁在执行"）。</summary>
            public bool HasActor => !string.IsNullOrWhiteSpace(ActorKind) && !string.IsNullOrWhiteSpace(ActorId);
        }

        /// <summary>当前在途租约数（观测取数）。</summary>
        public int InFlightCount { get { lock (_gate) return _byLease.Count; } }

        /// <summary>当前在途租约快照（按租约键序；不暴露内部字典）。</summary>
        public IReadOnlyList<RoundLease> InFlight
        {
            get { lock (_gate) return _byLease.Values.OrderBy(l => l.LeaseId, StringComparer.Ordinal).ToList(); }
        }

        /// <summary>
        /// 获取租约（幂等）：同 task 已有在途 ⇒ 返回**既有**租约（重复派发由 <c>RG1009</c> 判出，禁静默合并）。
        /// </summary>
        /// <param name="taskKey">任务键。</param>
        /// <param name="roundId">轮次键（空白 ⇒ 按 task 生成）。</param>
        /// <param name="generation">派发代次（≤0 ⇒ 自动取号）。</param>
        /// <param name="owner">归属方。</param>
        public RoundLease Acquire(string taskKey, string? roundId, int generation, string? owner)
        {
            if (string.IsNullOrWhiteSpace(taskKey)) throw new ArgumentException("task_key 不可为空", nameof(taskKey));

            lock (_gate)
            {
                if (_byTask.TryGetValue(taskKey, out var held) && _byLease.TryGetValue(held, out var existing))
                    return existing;

                var gen = generation > 0 ? generation : ++_generationCursor;
                var seq = ++_sequence;
                var rid = string.IsNullOrWhiteSpace(roundId) ? $"{taskKey}@{seq}" : roundId!;
                var lease = new RoundLease(
                    $"{taskKey}#{gen}#{seq}", taskKey, rid, gen, owner, string.Empty, 0, 0, 0, DateTimeOffset.UtcNow);

                _byLease[lease.LeaseId] = lease;
                _byTask[taskKey] = lease.LeaseId;
                _roundToLease[rid] = lease.LeaseId;
                return lease;
            }
        }

        /// <summary>
        /// 登记该租约的执行体（Live 运行态的"谁在执行"来源；唯一登记点，禁各处另起 Actor 拼装）。
        /// 重复登记 = 以最后一次为准（同租约执行体在轮内不变，重派发会换新租约）。
        /// </summary>
        /// <param name="leaseId">租约键。</param>
        /// <param name="actorKind">执行体类别（受控词，如 <c>agent</c>）。</param>
        /// <param name="actorId">执行体标识（如 cline / answer）。</param>
        /// <returns>租约不存在 ⇒ false（不新增租约、不静默创建）。</returns>
        public bool TryRegisterActor(string? leaseId, string? actorKind, string? actorId)
        {
            if (string.IsNullOrWhiteSpace(leaseId)) return false;
            lock (_gate)
            {
                if (!_byLease.TryGetValue(leaseId!, out var lease)) return false;
                _byLease[leaseId!] = lease with { ActorKind = actorKind, ActorId = actorId };
                return true;
            }
        }

        /// <summary>取该 task 当前在途租约（无 ⇒ null）—— <c>round.active_lease</c> 事实源。</summary>
        /// <param name="taskKey">任务键。</param>
        public RoundLease? ActiveOf(string? taskKey)
        {
            if (string.IsNullOrWhiteSpace(taskKey)) return null;
            lock (_gate)
                return _byTask.TryGetValue(taskKey!, out var leaseId) && _byLease.TryGetValue(leaseId, out var lease)
                    ? lease
                    : null;
        }

        /// <summary>按租约键取租约（无 ⇒ null）。</summary>
        /// <param name="leaseId">租约键。</param>
        public RoundLease? Find(string? leaseId)
        {
            if (string.IsNullOrWhiteSpace(leaseId)) return null;
            lock (_gate) return _byLease.TryGetValue(leaseId!, out var lease) ? lease : null;
        }

        /// <summary>按轮次键取租约（无 ⇒ null）。</summary>
        /// <param name="roundId">轮次键。</param>
        public RoundLease? FindByRound(string? roundId)
        {
            if (string.IsNullOrWhiteSpace(roundId)) return null;
            lock (_gate)
                return _roundToLease.TryGetValue(roundId!, out var leaseId) && _byLease.TryGetValue(leaseId, out var lease)
                    ? lease
                    : null;
        }

        /// <summary>
        /// 登记派发（记输入摘要）：租约不存在 ⇒ false + 原因（无租约派发 = <c>RG1010</c> 事实）。
        /// </summary>
        /// <param name="leaseId">租约键。</param>
        /// <param name="inputDigest">输入摘要（空白 ⇒ 仅登记不比对）。</param>
        /// <param name="reason">out：拒绝原因（成功 = null）。</param>
        public bool TryRegisterDispatch(string? leaseId, string? inputDigest, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(leaseId))
            {
                reason = "缺 lease_id（未经 lease_acquire）——不得派发（RG1010）";
                return false;
            }

            lock (_gate)
            {
                if (!_byLease.TryGetValue(leaseId!, out var lease))
                {
                    reason = $"租约 {leaseId} 不存在（未经 lease_acquire 或已释放）——不得派发（RG1010）";
                    return false;
                }

                if (lease.IsDispatched) return true;   // 幂等：同租约重复登记不产生第二条在途

                _byLease[lease.LeaseId] = lease with { InputDigest = inputDigest ?? string.Empty };
                return true;
            }
        }

        /// <summary>
        /// 取消（代次校验后生效）：越代 ⇒ false + 原因（不执行，防迟到看门狗杀在飞轮次）；有效 ⇒ 释放在途登记。
        /// </summary>
        /// <param name="leaseId">租约键。</param>
        /// <param name="generation">请求代次（≤0 ⇒ 不校验代次）。</param>
        /// <param name="reason">out：拒绝原因（成功 = null）。</param>
        public bool TryCancel(string? leaseId, int generation, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(leaseId))
            {
                reason = "缺 lease_id —— 无法取消（拒绝）";
                return false;
            }

            lock (_gate)
            {
                if (!_byLease.TryGetValue(leaseId!, out var lease))
                {
                    reason = $"租约 {leaseId} 不存在或已释放（重复取消 = 错误）";
                    return false;
                }

                if (generation > 0 && generation != lease.Generation)
                {
                    reason = $"越代取消（请求代次 {generation} ≠ 当前代次 {lease.Generation}）——拒绝执行";
                    return false;
                }

                _byLease.Remove(lease.LeaseId);
                _byTask.Remove(lease.TaskKey);
                _roundToLease.Remove(lease.RoundId);
                return true;
            }
        }

        /// <summary>登记该租约最近的既有落库序号（**不新增写路径**；供 <c>round_terminated.last_persist_seq</c> 取数）。</summary>
        /// <param name="leaseId">租约键。</param>
        /// <param name="persistSeq">既有通道落库序号。</param>
        public void NotePersistSeq(string? leaseId, long persistSeq)
        {
            if (string.IsNullOrWhiteSpace(leaseId)) return;
            lock (_gate)
                if (_byLease.TryGetValue(leaseId!, out var lease)) _byLease[leaseId!] = lease with { PersistSeq = persistSeq };
        }

        /// <summary>
        /// 累计该租约的 token 用量（C2：**单一记分牌**——行内轮次作用域内的唯一累加点；
        /// 全零 / 缺失值不累加，避免把「无遥测」写成 0 推进）。
        /// </summary>
        /// <param name="leaseId">租约键。</param>
        /// <param name="tokensIn">输入 token（null/负 ⇒ 不计）。</param>
        /// <param name="tokensOut">输出 token（null/负 ⇒ 不计）。</param>
        public void NoteTokens(string? leaseId, long? tokensIn, long? tokensOut)
        {
            if (string.IsNullOrWhiteSpace(leaseId)) return;
            if (tokensIn is not > 0 && tokensOut is not > 0) return;

            lock (_gate)
            {
                if (!_byLease.TryGetValue(leaseId!, out var lease)) return;
                _byLease[leaseId!] = lease with
                {
                    TokensIn = lease.TokensIn + Math.Max(0, tokensIn ?? 0),
                    TokensOut = lease.TokensOut + Math.Max(0, tokensOut ?? 0),
                };
            }
        }

        /// <summary>清空登记面（进程收尾 / 用例隔离；不触发任何效果）；**同时复位发号游标**（隔离场景下租约键/轮次键可复现）。</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _byLease.Clear();
                _byTask.Clear();
                _roundToLease.Clear();
                _sequence = 0;
                _generationCursor = 0;
            }
        }

        /// <summary>
        /// 事实投影（§5.4 口径）：把登记面翻成治理判据可消费的扁平点分键 ——
        /// **唯一投影点**，禁各端口自建第二份事实拼装。
        /// </summary>
        /// <param name="taskKey">任务键（空 ⇒ 不投影在途事实）。</param>
        /// <param name="leaseId">本次派发租约键（空 ⇒ 缺省，按缺失语义参与 <c>RG1010</c>）。</param>
        public IReadOnlyList<KeyValuePair<string, string?>> FactsOf(string? taskKey, string? leaseId)
        {
            var active = ActiveOf(taskKey);
            return new List<KeyValuePair<string, string?>>(3)
            {
                new("round.active_lease", active?.LeaseId),
                new("round.lease_id", string.IsNullOrWhiteSpace(leaseId) ? null : leaseId),
                new("round.phase", active is null ? "idle" : "running"),
            };
        }
    }
}
