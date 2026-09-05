using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Orchestration.SessionWorkflow;
using Agent.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="BoundedSessionGate"/> 有界性 + 串行承诺测试（Y4）：
    /// 容量有上限（注入大量会话，Count ≤ 上限）；同 key 并发仍串行；被持有的锁不可被淘汰（串行不破坏）。
    /// </summary>
    [TestClass]
    public class BoundedSessionGateTests
    {
        [TestMethod]
        public void Acquire_OverCapacity_EvictsUnusedToBound()
        {
            const int capacity = 256;
            var gate = new BoundedSessionGate(capacity);

            for (var i = 0; i < 1000; i++)
            {
                gate.Acquire($"s{i}");
                gate.Release($"s{i}"); // 归还 → 允许被淘汰
            }

            Assert.IsTrue(gate.Count <= capacity, $"锁缓存应有界（Count={gate.Count} ≤ {capacity}）");
        }

        [TestMethod]
        public async Task Acquire_SameKey_SerializesConcurrently()
        {
            var gate = new BoundedSessionGate(1);
            var key = "s1";
            var active = 0;
            var maxActive = 0;
            var gateLock = new object();

            async Task Work()
            {
                var sem = gate.Acquire(key);
                try
                {
                    await sem.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        lock (gateLock) { active++; maxActive = Math.Max(maxActive, active); }
                        await Task.Delay(30);
                        lock (gateLock) { active--; }
                    }
                    finally { sem.Release(); }
                }
                finally { gate.Release(key); }
            }

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Work()));

            Assert.AreEqual(1, maxActive, "同 key 必须串行（任意时刻至多 1 个在临界区）");
        }

        [TestMethod]
        public void Acquire_HeldLock_NotEvicted_PreservesSerialization()
        {
            // 被持有的锁（InUse>0）不可被淘汰：即使容量超限，仍返回同一互斥体，保证同会话串行承诺不破坏。
            var gate = new BoundedSessionGate(1);
            var key = "s1";
            var held = gate.Acquire(key); // 持有（不 Release）
            for (var i = 0; i < 500; i++)
            {
                gate.Acquire($"other{i}");
                gate.Release($"other{i}");
            }

            var again = gate.Acquire(key);
            Assert.AreSame(held, again, "被持有的锁不应被淘汰（同 key 返回同一互斥体，串行不破坏）");
            gate.Release(key);
            gate.Release(key);
        }
    }
}
