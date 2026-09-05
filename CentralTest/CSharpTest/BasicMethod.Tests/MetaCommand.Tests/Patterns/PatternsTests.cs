using MetaCommand;
using MetaCommand.Patterns;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Patterns
{
    [TestClass]
    public class ContextManagerTests
    {
        [TestMethod]
        public void Set_And_Current_ReturnsStoredValue()
        {
            var manager = new ContextManager<string>();
            manager.Set("test value");
            Assert.AreEqual("test value", manager.Current);
        }

        [TestMethod]
        public void Clear_RemovesCurrentValue()
        {
            var manager = new ContextManager<string>();
            manager.Set("test value");
            manager.Clear();
            Assert.IsNull(manager.Current);
        }

        [TestMethod]
        public void IsSet_WithValue_ReturnsTrue()
        {
            var manager = new ContextManager<string>();
            manager.Set("test");
            Assert.IsTrue(manager.IsSet);
        }

        [TestMethod]
        public void IsSet_WithoutValue_ReturnsFalse()
        {
            var manager = new ContextManager<string>();
            Assert.IsFalse(manager.IsSet);
        }

        [TestMethod]
        public void Execute_WithAction_SetsContextDuringExecution()
        {
            var manager = new ContextManager<string>();
            string? captured = null;
            manager.Execute("temp", () => { captured = manager.Current; });
            Assert.AreEqual("temp", captured);
            Assert.IsNull(manager.Current);
        }

        [TestMethod]
        public void Execute_WithFunc_ReturnsValueAndRestoresContext()
        {
            var manager = new ContextManager<string>();
            manager.Set("original");
            var result = manager.Execute("temp", () => manager.Current);
            Assert.AreEqual("temp", result);
            Assert.AreEqual("original", manager.Current);
        }

        [TestMethod]
        public void ExecuteAsync_WithAsyncAction_SetsContextDuringExecution()
        {
            var manager = new ContextManager<string>();
            string? captured = null;
            manager.ExecuteAsync("temp", async () =>
            {
                await Task.Yield();
                captured = manager.Current;
            }).GetAwaiter().GetResult();
            Assert.AreEqual("temp", captured);
            Assert.IsNull(manager.Current);
        }

        [TestMethod]
        public void Set_NullValue_ThrowsArgumentNullException()
        {
            var manager = new ContextManager<string>();
            Assert.ThrowsException<ArgumentNullException>(() => manager.Set(null!));
        }
    }

    [TestClass]
    public class EventBusTests
    {
        [TestMethod]
        public void SubscribeAndPublishAsync_HandlerIsCalled()
        {
            var bus = new EventBus();
            var called = false;
            bus.Subscribe<string>(async (msg) => { called = true; await Task.CompletedTask; });
            bus.PublishAsync("test").GetAwaiter().GetResult();
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void PublishAsync_NoSubscribers_DoesNotThrow()
        {
            var bus = new EventBus();
            bus.PublishAsync("test").GetAwaiter().GetResult();
        }

        [TestMethod]
        public void Subscribe_MultipleHandlers_AllCalled()
        {
            var bus = new EventBus();
            var callCount = 0;
            bus.Subscribe<string>(async (msg) => { callCount++; await Task.CompletedTask; });
            bus.Subscribe<string>(async (msg) => { callCount++; await Task.CompletedTask; });
            bus.PublishAsync("test").GetAwaiter().GetResult();
            Assert.AreEqual(2, callCount);
        }

        [TestMethod]
        public void Unsubscribe_HandlerNotCalledAfter()
        {
            var bus = new EventBus();
            var callCount = 0;
            Func<string, Task> handler = async (msg) => { callCount++; await Task.CompletedTask; };
            bus.Subscribe(handler);
            bus.Unsubscribe(handler);
            bus.PublishAsync("test").GetAwaiter().GetResult();
            Assert.AreEqual(0, callCount);
        }

        [TestMethod]
        public void SubscribeSync_HandlerIsCalled()
        {
            var bus = new EventBus();
            var called = false;
            bus.SubscribeSync<string>((msg) => called = true);
            bus.PublishAsync("test").GetAwaiter().GetResult();
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void Clear_RemovesAllSubscriptions()
        {
            var bus = new EventBus();
            var callCount = 0;
            bus.Subscribe<string>(async (msg) => { callCount++; await Task.CompletedTask; });
            bus.Clear();
            bus.PublishAsync("test").GetAwaiter().GetResult();
            Assert.AreEqual(0, callCount);
        }

        [TestMethod]
        public void GetSubscriberCount_ReturnsCorrectCount()
        {
            var bus = new EventBus();
            Assert.AreEqual(0, bus.GetSubscriberCount<string>());
            bus.Subscribe<string>(async (msg) => { await Task.CompletedTask; });
            Assert.AreEqual(1, bus.GetSubscriberCount<string>());
        }
    }

    [TestClass]
    public class ResolverRegistryTests
    {
        [TestMethod]
        public void Register_And_Get_ReturnsResolver()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            registry.Register("key1", (input) => input.ToUpper());
            var resolver = registry.Get("key1");
            Assert.IsNotNull(resolver);
            Assert.AreEqual("HELLO", resolver!("hello"));
        }

        [TestMethod]
        public void Register_DuplicateKey_ReturnsFalse()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            Assert.IsTrue(registry.Register("key1", (input) => "first"));
            Assert.IsFalse(registry.Register("key1", (input) => "second"));
        }

        [TestMethod]
        public void RegisterOrUpdate_OverwritesExisting()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            registry.RegisterOrUpdate("key1", (input) => "first");
            registry.RegisterOrUpdate("key1", (input) => "second");
            Assert.AreEqual("second", registry.Get("key1")!("test"));
        }

        [TestMethod]
        public void Get_NonExistentKey_ReturnsNull()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            Assert.IsNull(registry.Get("nonExistent"));
        }

        [TestMethod]
        public void ContainsKey_ExistingKey_ReturnsTrue()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            registry.Register("key1", (input) => input);
            Assert.IsTrue(registry.ContainsKey("key1"));
        }

        [TestMethod]
        public void Remove_ExistingKey_RemovesAndReturnsTrue()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            registry.Register("key1", (input) => input);
            Assert.IsTrue(registry.Remove("key1"));
            Assert.IsNull(registry.Get("key1"));
        }

        [TestMethod]
        public void Clear_RemovesAllResolvers()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            registry.Register("key1", (input) => input);
            registry.Register("key2", (input) => input);
            registry.Clear();
            Assert.AreEqual(0, registry.Count);
        }

        [TestMethod]
        public void Count_ReturnsCorrectCount()
        {
            var registry = new ResolverRegistry<string, Func<string, string>>();
            Assert.AreEqual(0, registry.Count);
            registry.Register("key1", (input) => input);
            Assert.AreEqual(1, registry.Count);
        }

        [TestMethod]
        public void Get_ByType_ReturnsMatchingResolver()
        {
            var registry = new ResolverRegistry<string, object>();
            registry.Register("key1", "string value");
            registry.Register("key2", 42);
            var strResult = registry.Get<string>();
            Assert.AreEqual("string value", strResult);
        }
    }

    [TestClass]
    public class SessionManagerTests
    {
        [TestMethod]
        public void Create_NewSession_ReturnsTrue()
        {
            var manager = new SessionManager<string>();
            Assert.IsTrue(manager.Create("session1", "value1"));
        }

        [TestMethod]
        public void Create_DuplicateId_ReturnsFalse()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            Assert.IsFalse(manager.Create("session1", "value2"));
        }

        [TestMethod]
        public void Get_ExistingSession_ReturnsValue()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            Assert.AreEqual("value1", manager.Get("session1"));
        }

        [TestMethod]
        public void Get_NonExistentSession_ReturnsNull()
        {
            var manager = new SessionManager<string>();
            Assert.IsNull(manager.Get("nonExistent"));
        }

        [TestMethod]
        public void Update_ExistingSession_ReturnsTrue()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            Assert.IsTrue(manager.Update("session1", "updatedValue"));
            Assert.AreEqual("updatedValue", manager.Get("session1"));
        }

        [TestMethod]
        public void Remove_ExistingSession_ReturnsTrue()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            Assert.IsTrue(manager.Remove("session1"));
            Assert.IsNull(manager.Get("session1"));
        }

        [TestMethod]
        public void Exists_ExistingSession_ReturnsTrue()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            Assert.IsTrue(manager.Exists("session1"));
        }

        [TestMethod]
        public void Clear_RemovesAllSessions()
        {
            var manager = new SessionManager<string>();
            manager.Create("session1", "value1");
            manager.Create("session2", "value2");
            manager.Clear();
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [TestMethod]
        public void ActiveCount_ReturnsCorrectCount()
        {
            var manager = new SessionManager<string>();
            Assert.AreEqual(0, manager.ActiveCount);
            manager.Create("session1", "value1");
            Assert.AreEqual(1, manager.ActiveCount);
        }

        [TestMethod]
        public void CleanupExpired_RemovesExpiredSessions()
        {
            var manager = new SessionManager<string>(TimeSpan.FromMilliseconds(1));
            manager.Create("session1", "value1");
            Thread.Sleep(10);
            var cleaned = manager.CleanupExpired();
            Assert.AreEqual(1, cleaned);
            Assert.IsNull(manager.Get("session1"));
        }
    }

    [TestClass]
    public class StateMachineTests
    {
        [TestMethod]
        public void Register_NewEntity_ReturnsTrue()
        {
            var sm = new StateMachine<TestState>();
            Assert.IsTrue(sm.Register("entity1", TestState.Idle));
        }

        [TestMethod]
        public void Register_DuplicateEntity_ReturnsFalse()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            Assert.IsFalse(sm.Register("entity1", TestState.Running));
        }

        [TestMethod]
        public void GetState_ExistingEntity_ReturnsState()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            Assert.AreEqual(TestState.Idle, sm.GetState("entity1"));
        }

        [TestMethod]
        public void GetState_NonExistentEntity_ReturnsNull()
        {
            var sm = new StateMachine<TestState>();
            Assert.IsNull(sm.GetState("nonExistent"));
        }

        [TestMethod]
        public void TryTransitionTo_ValidTransition_ChangesState()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            Assert.IsTrue(sm.TryTransitionTo("entity1", TestState.Running));
            Assert.AreEqual(TestState.Running, sm.GetState("entity1"));
        }

        [TestMethod]
        public void TryTransitionTo_NonExistentEntity_ReturnsFalse()
        {
            var sm = new StateMachine<TestState>();
            Assert.IsFalse(sm.TryTransitionTo("nonExistent", TestState.Running));
        }

        [TestMethod]
        public void TryTransitionTo_WithCondition_BlocksTransition()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            Assert.IsFalse(sm.TryTransitionTo("entity1", TestState.Running, (current) => false));
            Assert.AreEqual(TestState.Idle, sm.GetState("entity1"));
        }

        [TestMethod]
        public void OnStateChanged_EventFiresCorrectly()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            string? changedId = null;
            TestState? oldState = null;
            TestState? newState = null;
            sm.OnStateChanged += (id, old, @new) => { changedId = id; oldState = old; newState = @new; };
            sm.TryTransitionTo("entity1", TestState.Running);
            Assert.AreEqual("entity1", changedId);
            Assert.AreEqual(TestState.Idle, oldState);
            Assert.AreEqual(TestState.Running, newState);
        }

        [TestMethod]
        public void CountByState_ReturnsCorrectCount()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            sm.Register("entity2", TestState.Running);
            sm.Register("entity3", TestState.Idle);
            Assert.AreEqual(2, sm.CountByState(TestState.Idle));
            Assert.AreEqual(1, sm.CountByState(TestState.Running));
        }

        [TestMethod]
        public void Remove_ExistingEntity_ReturnsTrue()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            Assert.IsTrue(sm.Remove("entity1"));
            Assert.IsNull(sm.GetState("entity1"));
        }

        [TestMethod]
        public void Clear_RemovesAllEntities()
        {
            var sm = new StateMachine<TestState>();
            sm.Register("entity1", TestState.Idle);
            sm.Register("entity2", TestState.Running);
            sm.Clear();
            Assert.AreEqual(0, sm.Count);
        }

        private enum TestState { Idle, Running, Completed, Error }
    }
}
