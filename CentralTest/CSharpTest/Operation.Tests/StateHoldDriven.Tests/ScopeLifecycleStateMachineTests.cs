using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.StateManagement;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// ScopeLifecycleStateMachine 单测——P3 生命周期状态机验证：
    /// Empty→Declared→Loading→Loaded / Loading→Failed / Loaded→Destroying→Destroyed / R6 幂等短路。
    /// </summary>
    [TestClass]
    public class ScopeLifecycleStateMachineTests
    {
        [TestMethod]
        public async Task Load_Success_ShouldTransitionToLoaded_WithVersionIncrement()
        {
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
                Task.FromResult(new LoadResult(scope, true, 0, 5, null)));

            var first = await sm.LoadAsync("Side", "MathFunctions.json");
            var state = sm.GetState("Side");

            Assert.IsTrue(first.Succeeded);
            Assert.AreEqual(1, first.ConfigVersion); // 首载 version = 0 + 1
            Assert.AreEqual(5, first.NodeCount);
            Assert.IsNotNull(state);
            Assert.AreEqual(ScopeState.Loaded, state!.State);
            Assert.AreEqual("MathFunctions.json", state.ConfigPath);
        }

        [TestMethod]
        public async Task Load_Failure_ShouldTransitionToFailed_WithLastError()
        {
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
                Task.FromResult(new LoadResult(scope, false, 0, 0, "render failed")));

            var result = await sm.LoadAsync("Side", "MathFunctions.json");
            var state = sm.GetState("Side");

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(state);
            // E1 修复：失败落 Failed 终态（原停留 Loading 导致幂等短路误吞失败）
            Assert.AreEqual(ScopeState.Failed, state!.State);
            Assert.AreEqual("render failed", state.LastError);
        }

        [TestMethod]
        public async Task Load_Failure_ThenSamePathRetry_ShouldNotShortCircuit()
        {
            int renderCalls = 0;
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
            {
                renderCalls++;
                return Task.FromResult(new LoadResult(scope, false, 0, 0, "render failed"));
            });

            await sm.LoadAsync("Side", "MathFunctions.json");
            Assert.AreEqual(ScopeState.Failed, sm.GetState("Side")!.State);

            // E1 修复：Failed 态同路径重试必须走完整事务（幂等短路只对 Loaded 生效），失败不得被吞
            await sm.LoadAsync("Side", "MathFunctions.json");

            Assert.AreEqual(2, renderCalls);
            Assert.AreEqual(ScopeState.Failed, sm.GetState("Side")!.State);
        }

        [TestMethod]
        public async Task Load_LoadedThenFailed_ThenSamePathRetry_ShouldRenderAgain()
        {
            int renderCalls = 0;
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
            {
                renderCalls++;
                var succeeded = renderCalls == 1; // 首次成功，后续失败
                return Task.FromResult(new LoadResult(
                    scope, succeeded, 0, succeeded ? 5 : 0, succeeded ? null : "render failed"));
            });

            await sm.LoadAsync("Side", "MathFunctions.json");                    // Loaded v=1
            await sm.LoadAsync("Side", "MathFunctions.json", forceReload: true); // 强制重载 → 失败 → Failed（v 保留 1）

            Assert.AreEqual(ScopeState.Failed, sm.GetState("Side")!.State);
            Assert.AreEqual(1, sm.GetState("Side")!.ConfigVersion);

            // E1 核心场景：Loaded→Failed 残留 ConfigVersion>0，同路径重试不得被短路吞掉，必须真实渲染
            await sm.LoadAsync("Side", "MathFunctions.json");

            Assert.AreEqual(3, renderCalls);
            Assert.AreEqual(ScopeState.Failed, sm.GetState("Side")!.State); // 第3次仍失败，状态保持 Failed
        }

        [TestMethod]
        public async Task Load_SamePath_ShouldIdempotentShortCircuit_WithoutRendering()
        {
            int renderCalls = 0;
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
            {
                renderCalls++;
                return Task.FromResult(new LoadResult(scope, true, 0, 5, null));
            });

            await sm.LoadAsync("Side", "MathFunctions.json");
            await sm.LoadAsync("Side", "MathFunctions.json"); // 同 path + version>0 → 短路

            Assert.AreEqual(1, renderCalls);
            Assert.AreEqual(1, sm.GetState("Side")!.ConfigVersion); // 版本不递增
        }

        [TestMethod]
        public async Task Load_DifferentPath_ShouldRealRender_WithVersionIncrement()
        {
            int renderCalls = 0;
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
            {
                renderCalls++;
                return Task.FromResult(new LoadResult(scope, true, 0, 5, null));
            });

            await sm.LoadAsync("Side", "MathFunctions.json");
            await sm.LoadAsync("Side", "OtherLayout.json"); // 不同 path → 真实执行

            Assert.AreEqual(2, renderCalls);
            Assert.AreEqual(2, sm.GetState("Side")!.ConfigVersion); // version 递增
            Assert.AreEqual("OtherLayout.json", sm.GetState("Side")!.ConfigPath);
        }

        [TestMethod]
        public async Task Load_ForceReload_ShouldBypassShortCircuit()
        {
            int renderCalls = 0;
            var sm = new ScopeLifecycleStateMachine((scope, path, force) =>
            {
                renderCalls++;
                return Task.FromResult(new LoadResult(scope, true, 0, 5, null));
            });

            await sm.LoadAsync("Side", "MathFunctions.json");
            await sm.LoadAsync("Side", "MathFunctions.json", forceReload: true);

            Assert.AreEqual(2, renderCalls);
            Assert.AreEqual(2, sm.GetState("Side")!.ConfigVersion);
        }

        [TestMethod]
        public async Task Destroy_ShouldTransitionThroughDestroying_AndRemoveScope()
        {
            bool destroyed = false;
            var sm = new ScopeLifecycleStateMachine(
                (scope, path, force) => Task.FromResult(new LoadResult(scope, true, 0, 5, null)),
                destroyDelegate: _ => destroyed = true);

            await sm.LoadAsync("Side", "MathFunctions.json");
            Assert.IsNotNull(sm.GetState("Side"));

            await sm.DestroyAsync("Side");

            Assert.IsTrue(destroyed);
            Assert.IsNull(sm.GetState("Side")); // Destroyed → 移除（Empty 语义）
            Assert.AreEqual(0, sm.GetScopes().Count);
        }

        [TestMethod]
        public async Task ScopeStateChanged_ShouldFireOnTransitions()
        {
            var transitions = new List<ScopeState>();
            var sm = new ScopeLifecycleStateMachine(
                (scope, path, force) => Task.FromResult(new LoadResult(scope, true, 0, 5, null)));
            sm.ScopeStateChanged += info => transitions.Add(info.State);

            await sm.LoadAsync("Side", "MathFunctions.json");
            await sm.DestroyAsync("Side");

            CollectionAssert.AreEqual(new[] { ScopeState.Loading, ScopeState.Loaded, ScopeState.Destroying, ScopeState.Destroyed }, transitions);
        }
    }
}
