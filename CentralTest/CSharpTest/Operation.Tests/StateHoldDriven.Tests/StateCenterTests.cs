using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;
using Share.StateManagement;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// StateCenter 单测——P2 中心核心验证：scope 分区隔离 / Read-Write / 生命周期 Provider 占位抛异常。
    /// </summary>
    [TestClass]
    public class StateCenterTests
    {
        private static StateCenter CreateCenter() => new(new StorageCell("center"));

        [TestMethod]
        public void GetOrCreateScope_ShouldReturnSameInstance_AndIsolate()
        {
            var center = CreateCenter();
            var a = center.GetOrCreateScope("A");
            var b = center.GetOrCreateScope("B");
            var a2 = center.GetOrCreateScope("A");

            Assert.AreSame(a, a2);

            a.Set("k", "a-value");
            b.Set("k", "b-value");

            Assert.AreEqual("a-value", a.Get("k"));
            Assert.AreEqual("b-value", b.Get("k"));
        }

        [TestMethod]
        public void Write_ThenRead_ShouldRoundTrip()
        {
            var center = CreateCenter();
            center.Write(StateKey.Container("S"), "c1");

            Assert.AreEqual("c1", center.Read<string>(StateKey.Container("S")));
        }

        [TestMethod]
        public void Write_ShouldPublishToSubscribers()
        {
            var center = CreateCenter();
            int published = 0;
            center.Subscriptions.Subscribe("container", null, (_, _) => published++);

            center.Write(StateKey.Container("S"), "c1");

            Assert.AreEqual(1, published);
        }

        [TestMethod]
        public void UnregisteredLifecycleProvider_ShouldThrow()
        {
            var center = CreateCenter();
            Assert.ThrowsException<InvalidOperationException>(() => center.Scopes);
            Assert.ThrowsException<InvalidOperationException>(() => center.Composition);
            Assert.ThrowsException<InvalidOperationException>(() => center.Containers);
        }

        [TestMethod]
        public void RegisterLifecycleProviders_ShouldResolve()
        {
            var center = CreateCenter();
            var scope = new FakeScopeLifecycle();
            var composition = new FakeCompositionLifecycle();
            var container = new FakeContainerLifecycle();

            center.RegisterScopeLifecycle(scope);
            center.RegisterCompositionLifecycle(composition);
            center.RegisterContainerLifecycle(container);

            Assert.AreSame(scope, center.Scopes);
            Assert.AreSame(composition, center.Composition);
            Assert.AreSame(container, center.Containers);
        }

        [TestMethod]
        public void BeginTransaction_WithScope_ShouldWriteToScopeCell_NotGlobal()
        {
            var center = CreateCenter();

            using (var tx = center.BeginTransaction("S"))
            {
                tx.Set(StateKey.Container("S"), "scoped-value");
                tx.Commit();
            }

            // scope 分区可读（同一 scope cell）
            Assert.AreEqual("scoped-value", center.GetOrCreateScope("S").Get("container:S:state"));
            // 全局（根 cell）不可见：scope 隔离生效（原实现恒写根 cell，此处会断言失败）
            Assert.IsFalse(center.GlobalStore.Has("container:S:state"));
            Assert.IsNull(center.Read<string>(StateKey.Container("S")));
        }

#pragma warning disable CS0067 // 测试桩事件无订阅者（接口成员占位）
        private sealed class FakeScopeLifecycle : IScopeLifecycle
        {
            public event Action<ScopeLifecycleInfo>? ScopeStateChanged;

            public Task<LoadResult> LoadAsync(string scope, string configPath, bool forceReload = false)
                => Task.FromResult(new LoadResult(scope, true, 0, 0));

            public Task DestroyAsync(string scope) => Task.CompletedTask;

            public ScopeLifecycleInfo? GetState(string scope) => null;

            public IReadOnlyCollection<string> GetScopes() => Array.Empty<string>();
        }
#pragma warning restore CS0067

        private sealed class FakeCompositionLifecycle : ICompositionLifecycle
        {
            public void Register(string id, object composition, object gate) { }
            public void ScopeDestroyed(string scope) { }
            public void AppShutdown() { }
        }

        private sealed class FakeContainerLifecycle : IContainerLifecycle
        {
            public void Register(string scope, string hostKey, string source = "code-registered") { }
            public void Unregister(string scope) { }
            public string? ResolveHostKey(string scopeOrHostKey) => null;
            public string? ResolveScope(string hostKey) => null;
            public bool IsBound(string scope) => false;
            public void MarkMounted(string scopeOrHostKey, bool mounted) { }
            public ContainerMountState GetMountState(string scopeOrHostKey) => ContainerMountState.Registered;
        }
    }
}
