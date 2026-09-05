using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Llm;
using Agent.State;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Axioms.Tests
{
    /// <summary>
    /// 统一网关治理层（<see cref="LlmRoutingClient"/>）测试 —— 覆盖本次架构修复核心「LLM 调用治理下沉」：
    /// ① 挂起后端 → 网关层统一超时内返回结构化失败（含后端/耗时），**不再静默空等**（对应真实模式 analyze 挂起）；
    /// ② 成功/异常后端 → 正确归因 + 全后端 CallLog 记录（对应真实模式可观测缺口）。
    /// </summary>
    [TestClass]
    public class LlmRoutingClientGuardTests
    {
        /// <summary>可控测试后端：OnChat 未设则返回成功 "ok"；可注入挂起/异常。</summary>
        private sealed class StubLlm : ILlmClient
        {
            public string Name { get; init; } = "stub";
            public Func<CancellationToken, Task<LlmResult>>? OnChat;

            public Task<LlmResult> ChatAsync(
                string system, string user, IReadOnlyList<LlmMessage>? history = null, CancellationToken ct = default)
                => OnChat is not null ? OnChat(ct) : Task.FromResult(new LlmResult("ok"));
        }

        [TestMethod]
        public void Guard_HangingBackend_ReturnsTimeoutFailure_BeforeDefaultTimeout()
        {
            // isLocalOnly=false → 路由到 resolveDefault（挂起后端）；短治理超时 200ms 验证「不再空等 60s」。
            var local = new LocalLlmClient();
            var hanging = new StubLlm { Name = "hanging", OnChat = async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); return new LlmResult("x"); } };
            var routing = new LlmRoutingClient(local, () => hanging, () => false, TimeSpan.FromMilliseconds(200));

            var sw = Stopwatch.StartNew();
            var result = routing.ChatAsync("sys", "user").GetAwaiter().GetResult();
            sw.Stop();

            Assert.IsFalse(result.IsSuccess, "挂起后端应在治理超时内返回失败，而非静默空等");
            StringAssert.Contains(result.Error, "超时");
            StringAssert.Contains(result.Error, "hanging");
            Assert.IsTrue(sw.ElapsedMilliseconds < 3000,
                $"应在治理超时（200ms）附近归因返回，而非默认 60s（实际 {sw.ElapsedMilliseconds}ms）");
            Assert.AreEqual(1, routing.CallLog.Count, "治理层应记录该次调用（全后端 CallLog）");
            Assert.AreEqual("hanging", routing.CallLog[0].Backend);
            Assert.AreEqual(false, routing.CallLog[0].IsSuccess);
            Assert.IsTrue(routing.CallLog[0].ElapsedMs > 0);
        }

        [TestMethod]
        public void Guard_SuccessBackend_ReturnsOk_AndRecords()
        {
            var local = new LocalLlmClient();
            var ok = new StubLlm { Name = "stub", OnChat = _ => Task.FromResult(new LlmResult("回答")) };
            var routing = new LlmRoutingClient(local, () => ok, () => false);

            var result = routing.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("回答", result.Content);
            Assert.AreEqual(1, routing.CallLog.Count);
            Assert.AreEqual(true, routing.CallLog[0].IsSuccess);
            Assert.IsNull(routing.CallLog[0].Error);
            Assert.AreEqual("chat", routing.CallLog[0].CallKind);
        }

        [TestMethod]
        public void Guard_ThrowingBackend_ReturnsFailure_AndRecords()
        {
            var local = new LocalLlmClient();
            var bad = new StubLlm { Name = "bad", OnChat = _ => throw new InvalidOperationException("boom") };
            var routing = new LlmRoutingClient(local, () => bad, () => false);

            var result = routing.ChatAsync("sys", "user").GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess, "异常后端应归因失败而非抛到调用方");
            StringAssert.Contains(result.Error, "bad");
            StringAssert.Contains(result.Error, "boom");
            Assert.AreEqual(1, routing.CallLog.Count);
            Assert.AreEqual(false, routing.CallLog[0].IsSuccess);
        }

        [TestMethod]
        public void RecordExternal_GovernanceSeam_RecordsIntoAllCallLog()
        {
            // 治理接缝（阶段4）：非 ILlmClient 契约的执行端（Cline daemon collect）经 RecordExternal 补统一观测，
            // 使真实与本地 collect 记录同形；超时模型由调用方（ClineClient 预算单一时钟）裁决，本入口只补观测/归因。
            var local = new LocalLlmClient();
            var routing = new LlmRoutingClient(local, () => new StubLlm(), () => false);

            routing.RecordExternal("cline", "cline-daemon", 42, ok: true, error: null);
            routing.RecordExternal("cline", "cline-daemon", 7, ok: false, error: "daemon 超时");

            Assert.AreEqual(2, routing.CallLog.Count, "治理接缝记录应进入统一 AllCallLog");
            Assert.AreEqual("cline", routing.CallLog[0].CallKind);
            Assert.AreEqual("cline-daemon", routing.CallLog[0].Backend);
            Assert.AreEqual(42, routing.CallLog[0].ElapsedMs);
            Assert.IsTrue(routing.CallLog[0].IsSuccess);

            Assert.AreEqual(false, routing.CallLog[1].IsSuccess);
            Assert.AreEqual("daemon 超时", routing.CallLog[1].Error);
        }
    }
}
