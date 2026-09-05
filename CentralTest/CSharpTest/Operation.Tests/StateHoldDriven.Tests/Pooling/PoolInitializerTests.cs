using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement.Pooling;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// PoolInitializer 单测——启动编排：前置检查 / spawn N 个 / 心跳探活（可重试）。
    /// 传输委托均用测试桩（不真正起进程，验证状态与结果编排）。
    /// </summary>
    [TestClass]
    public class PoolInitializerTests
    {
        private static (StorageEngine Engine, WorkerStateStore Store, PoolInitializer Init) CreateInit()
        {
            var engine = new StorageEngine();
            var store = new WorkerStateStore(engine);
            var init = new PoolInitializer(store);
            init.RegisterWorkerFactory((spec, ct) =>
                Task.FromResult(new WorkerHandle(spec.WorkerId, Random.Shared.Next(1000, 9999), DateTime.UtcNow)));
            return (engine, store, init);
        }

        [TestMethod]
        public async Task Start_Success_ShouldReturnRunningWorkers()
        {
            var (_, store, init) = CreateInit();
            init.RegisterProbe(_ => Task.FromResult(true));

            var result = await init.StartAsync(3);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(3, result.StartedCount);
            Assert.AreEqual(3, result.Workers.Count);
            Assert.AreEqual(3, store.GetWorkerIds().Count);
            foreach (var h in result.Workers)
                Assert.AreEqual(PoolWorkerState.Running, store.Get(h.WorkerId).State);
        }

        [TestMethod]
        public async Task Start_PreconditionFalse_ShouldFail_WithoutSpawning()
        {
            var (_, store, init) = CreateInit();
            init.RegisterPrecondition(() => Task.FromResult(false));

            var result = await init.StartAsync(2);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.StartedCount);
            Assert.AreEqual(0, result.Workers.Count);
            Assert.IsNotNull(result.Error);
            StringAssert.Contains(result.Error!, "前置条件");
        }

        [TestMethod]
        public async Task Start_ProbeFails_ShouldMarkHung_AndFail()
        {
            var (_, store, init) = CreateInit();
            init.RegisterProbe(_ => Task.FromResult(false));
            init.RetryLimit = 0;

            var result = await init.StartAsync(2);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(2, result.Workers.Count);
            foreach (var h in result.Workers)
                Assert.AreEqual(PoolWorkerState.Hung, store.Get(h.WorkerId).State);
        }

        [TestMethod]
        public async Task Start_ProbeTransientFail_ShouldRetry_AndSucceed()
        {
            var (_, store, init) = CreateInit();
            int probes = 0;
            init.RegisterProbe(_ =>
            {
                probes++;
                return Task.FromResult(probes > 1); // 首次失败，重试后成功
            });
            init.RetryLimit = 1;

            var result = await init.StartAsync(1);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(PoolWorkerState.Running, store.Get(result.Workers[0].WorkerId).State);
            Assert.AreEqual(2, probes); // 首探 + 1 次重试
        }

        [TestMethod]
        public async Task Start_WithoutWorkerFactory_ShouldFail()
        {
            var engine = new StorageEngine();
            var store = new WorkerStateStore(engine);
            var init = new PoolInitializer(store); // 未注册工厂

            var result = await init.StartAsync(1);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Error);
        }
    }
}
