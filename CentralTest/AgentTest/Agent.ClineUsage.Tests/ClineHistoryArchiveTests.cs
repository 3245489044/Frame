using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules;
using Share.Conversation;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// Cline 对话统一接入历史容器（重构方案）核心单元测试：
    /// <see cref="ClineMessageMapper"/>（ClineMessage → ConversationMessage 纯函数映射）与
    /// <see cref="HistoryClineMessageArchive"/>（基于 ur_histories / IHistoryStore 的归档，含 resume 原文保留、幂等重归档、轮次推导）。
    /// 不依赖真实 SQLite / node，离线运行（用内存版 IHistoryStore 假实现）。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class ClineHistoryArchiveTests
    {
        // ---------- ClineMessageMapper：§5 角色映射 ----------

        [TestMethod]
        public void Map_user消息_映射为会话级User()
        {
            var mapped = ClineMessageMapper.Map(new[] { new ClineMessage("user", "你好", 1_600_000_000_000L, null, null, null, null) }, 3);

            var m = Assert.Single(mapped);
            Assert.AreEqual(ConversationRole.User, m.Role);
            Assert.IsNull(m.Stage, "user 外部输入 Stage 应为 null");
            Assert.IsNull(m.SubAgent, "user 外部输入 SubAgent 应为 null");
            Assert.AreEqual("你好", m.Text);
            Assert.AreEqual(3, m.Round, "Round = 主循环轮次");
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_600_000_000_000L), m.AtUtc);
        }

        [TestMethod]
        public void Map_assistant与say_映射为Assistant_cline_execute()
        {
            var mapped = ClineMessageMapper.Map(
                new[]
                {
                    new ClineMessage("assistant", "终答", 1L, null, null, null, null),
                    new ClineMessage("say", "状态", 2L, "say", null, null, null),
                }, 5);

            Assert.AreEqual(2, mapped.Count);
            foreach (var m in mapped)
            {
                Assert.AreEqual(ConversationRole.Assistant, m.Role);
                Assert.AreEqual(ClineMessageMapper.ClinesStage, m.Stage);
                Assert.AreEqual(ClineMessageMapper.ClinesSubAgent, m.SubAgent);
            }
        }

        [TestMethod]
        public void Map_工具与思考_折叠为Tool_cline_execute()
        {
            var mapped = ClineMessageMapper.Map(
                new[]
                {
                    new ClineMessage("use_tool", "tool", 1L, null, null, null, null),
                    new ClineMessage("tool_result", "result", 2L, null, null, null, null),
                    new ClineMessage("reasoning", "思考", 3L, null, null, null, null),
                    new ClineMessage("ask", "询问", 4L, null, null, null, null),
                }, 1);

            Assert.AreEqual(4, mapped.Count);
            foreach (var m in mapped)
            {
                Assert.AreEqual(ConversationRole.Tool, m.Role);
                Assert.AreEqual(ClineMessageMapper.ClinesStage, m.Stage);
                Assert.AreEqual(ClineMessageMapper.ClinesSubAgent, m.SubAgent);
            }
        }

        [TestMethod]
        public void Map_保留原文载荷Payload()
        {
            var mapped = ClineMessageMapper.Map(new[] { new ClineMessage("assistant", "x", 1L, null, null, null, null) }, 1);

            Assert.IsFalse(string.IsNullOrWhiteSpace(mapped[0].Payload), "映射应保留 ClineMessage 原文（resume 重建兜底）");
        }

        // ---------- HistoryClineMessageArchive：ur_histories 归档 ----------

        [TestMethod]
        public async Task ArchiveAsync_归档后_LoadAsync可取回原文()
        {
            var archive = new HistoryClineMessageArchive(() => new FakeHistoryStore());
            const string json = """{"messages":[{"type":"user","text":"你好"},{"type":"assistant","text":"你好！"}]}""";

            await archive.ArchiveAsync("sess-1", 1, json, "completed", CancellationToken.None);

            var loaded = await archive.LoadAsync("sess-1", 1, CancellationToken.None);
            Assert.AreEqual(json, loaded, "LoadAsync 应取回该轮完整 messages.json 原文（resume 精确重建）");
        }

        [TestMethod]
        public async Task ArchiveAsync_同轮重复归档_幂等不重复()
        {
            var store = new FakeHistoryStore();
            var archive = new HistoryClineMessageArchive(() => store);

            await archive.ArchiveAsync("sess-1", 1, "r1-a", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 1, "r1-b", "completed", CancellationToken.None);

            var rounds = await archive.ListRoundsAsync("sess-1", CancellationToken.None);
            Assert.AreEqual(1, rounds.Count, "同轮重复归档应仅 1 个轮次");
            var latest = await archive.LoadLatestAsync("sess-1", CancellationToken.None);
            Assert.AreEqual("r1-b", latest, "幂等重归档应覆盖为最新载荷");
        }

        [TestMethod]
        public async Task ArchiveAsync_多轮归档_ListRounds升序_LoadLatest取最大轮_NextRound推导()
        {
            var archive = new HistoryClineMessageArchive(() => new FakeHistoryStore());

            await archive.ArchiveAsync("sess-1", 1, "r1", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 3, "r3", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 2, "r2", "completed", CancellationToken.None);

            var rounds = await archive.ListRoundsAsync("sess-1", CancellationToken.None);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rounds.ToList(), "轮次应按升序排列");

            var latest = await archive.LoadLatestAsync("sess-1", CancellationToken.None);
            Assert.AreEqual("r3", latest, "最近一轮应为最大 round");

            var next = await archive.NextRoundAsync("sess-1", CancellationToken.None);
            Assert.AreEqual(4, next, "下一归档轮次 = 已归档最大 + 1");
        }

        [TestMethod]
        public async Task LoadLatestAsync_无归档_返回null_NextRound为1()
        {
            var archive = new HistoryClineMessageArchive(() => new FakeHistoryStore());

            var latest = await archive.LoadLatestAsync("sess-empty", CancellationToken.None);
            Assert.IsNull(latest, "无归档时应返回 null");

            var next = await archive.NextRoundAsync("sess-empty", CancellationToken.None);
            Assert.AreEqual(1, next, "无归档时下一轮应为 1");
        }

        [TestMethod]
        public async Task ArchiveAsync_空sessionId_回退默认taskKey()
        {
            var store = new FakeHistoryStore();
            var archive = new HistoryClineMessageArchive(() => store);

            await archive.ArchiveAsync("", 1, "r1", "completed", CancellationToken.None);

            Assert.IsNotNull(store.Get("default"), "空 sessionId 应落默认 taskKey=default（对齐 send 空 taskKey 语义）");
            Assert.IsNull(store.Get(""), "不应落到空键");
        }

        [TestMethod]
        public async Task ArchiveAsync_应用taskKeyMapper_映射到历史键()
        {
            var store = new FakeHistoryStore();
            // 恒等之外的显式映射：sessionId → taskKey（历史聚合键）。
            var archive = new HistoryClineMessageArchive(() => store, sessionId => $"task-{sessionId}");

            await archive.ArchiveAsync("sess-1", 1, "r1", "completed", CancellationToken.None);

            Assert.IsNotNull(store.Get("task-sess-1"), "应按 taskKeyMapper 写入 historyId=taskKey");
            Assert.IsNull(store.Get("sess-1"), "不应按原始 sessionId 落键");
        }

        /// <summary>内存版 IHistoryStore（按 sessionId 维度存 ConversationLog）。</summary>
        private sealed class FakeHistoryStore : IHistoryStore
        {
            private readonly Dictionary<string, ConversationLog> _logs = new();

            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
            {
                _logs[sessionId] = log;
                return Task.CompletedTask;
            }

            public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(_logs.TryGetValue(sessionId, out var log) ? log : null);

            public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(_logs.Keys.ToList());

            public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(_logs.Remove(sessionId));

            public ConversationLog? Get(string sessionId)
                => _logs.TryGetValue(sessionId, out var log) ? log : null;
        }
    }
}

