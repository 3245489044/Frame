using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Threading.Streaming;

namespace Modules.Threading.Tests
{
    /// <summary>
    /// 通用流式原语回归基线：有界通道批写器 <see cref="BatchedAsyncSink{TRecord}"/> 与
    /// 有界队列消费者 <see cref="DedupQueueConsumer{TItem}"/>（自 Agent.Core EventSink / ReactiveTrigger 提取，单源实现）。
    /// </summary>
    [TestClass]
    [TestCategory("Streaming")]
    public class StreamingTests
    {
        [TestMethod]
        public async Task BatchedAsyncSink_EmitsEventuallyFlushedToSink()
        {
            var flushed = new List<string>();
            await using var sink = new BatchedAsyncSink<string>(
                capacity: 16, drainBatch: 4,
                flush: (batch, ct) => { foreach (var s in batch) flushed.Add(s); return Task.CompletedTask; });
            sink.Emit("a");
            sink.Emit("b");

            await WaitUntilAsync(() => flushed.Count >= 2, TimeSpan.FromSeconds(3));
            Assert.AreEqual(2, flushed.Count);
        }

        [TestMethod]
        public async Task BatchedAsyncSink_TransientFlushFailure_RetriesAndSucceeds()
        {
            var flushed = new List<string>();
            var attempts = 0;
            await using var sink = new BatchedAsyncSink<string>(
                capacity: 16, drainBatch: 4,
                flush: (batch, ct) =>
                {
                    attempts++;
                    if (attempts < 2) throw new InvalidOperationException("transient");
                    foreach (var s in batch) flushed.Add(s);
                    return Task.CompletedTask;
                },
                retryLimit: 3, retryDelay: TimeSpan.FromMilliseconds(5));
            sink.Emit("a");

            await WaitUntilAsync(() => flushed.Count >= 1, TimeSpan.FromSeconds(3));
            Assert.AreEqual(1, flushed.Count);
            Assert.IsTrue(attempts >= 2, $"应发生至少一次重试，实际 {attempts}");
        }

        [TestMethod]
        public async Task DedupQueueConsumer_TryProcessAsync_DedupsByKey()
        {
            var processed = 0;
            DedupQueueConsumer<string> consumer = null!;
            consumer = new DedupQueueConsumer<string>(
                queueCapacity: 16, maxConcurrency: 1,
                keySelector: s => s,
                // 契约：调用方在处理器 finally 标记（对齐 ReactiveTrigger.ProcessAsync 语义）。
                processor: (s, ct) => { processed++; consumer.MarkProcessed(s); return Task.CompletedTask; });

            Assert.IsTrue(await consumer.TryProcessAsync("k1", CancellationToken.None));
            Assert.IsFalse(await consumer.TryProcessAsync("k1", CancellationToken.None)); // 幂等去重
            Assert.IsTrue(await consumer.TryProcessAsync("k2", CancellationToken.None));
            Assert.AreEqual(2, processed);

            await consumer.DisposeAsync();
        }

        [TestMethod]
        public async Task DedupQueueConsumer_Start_ProcessesEnqueuedItems()
        {
            var processed = new List<string>();
            DedupQueueConsumer<string> consumer = null!;
            consumer = new DedupQueueConsumer<string>(
                queueCapacity: 16, maxConcurrency: 1,
                keySelector: s => s,
                processor: (s, ct) => { lock (processed) processed.Add(s); consumer.MarkProcessed(s); return Task.CompletedTask; });

            consumer.Start();
            consumer.Enqueue("a");
            consumer.Enqueue("b");

            await WaitUntilAsync(() => { lock (processed) return processed.Count >= 2; }, TimeSpan.FromSeconds(3));
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, processed);

            await consumer.DisposeAsync();
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate()) return;
                await Task.Delay(20);
            }
            throw new TimeoutException("等待条件超时。");
        }
    }
}
