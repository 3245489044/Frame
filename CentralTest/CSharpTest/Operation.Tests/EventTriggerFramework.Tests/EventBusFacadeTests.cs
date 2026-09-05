using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using EventTriggerFramework.EventBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventTriggerFramework.Tests
{
    [TestClass]
    public class EventBusFacadeTests : TestBase
    {
        private EventBusFacade _facade = null!;

        [TestInitialize]
        public void Setup()
        {
            _facade = new EventBusFacade();
        }

        [TestMethod]
        public void RunAll_PublishSubscribe()
        {
            var result = RunTestGroup("PublishSubscribe", builder =>
            {
                builder.Add("Publish_ShouldInvokeSubscribedHandler", () =>
                {
                    object? received = null;
                    _facade.Subscribe("testEvent", data => received = data);
                    _facade.Publish("testEvent", "hello");
                    return received as string == "hello";
                });

                builder.Add("Publish_MultipleHandlers_ShouldInvokeAll", () =>
                {
                    var calls = 0;
                    _facade.Subscribe("multiEvent", _ => calls++);
                    _facade.Subscribe("multiEvent", _ => calls++);
                    _facade.Publish("multiEvent", null);
                    return calls == 2;
                });

                builder.Add("Publish_NoSubscribers_ShouldNotThrow", () =>
                {
                    _facade.Publish("noSubscribers", "data");
                    return true;
                });

                builder.Add("Publish_NullEventName_ShouldThrow", () =>
                {
                    try
                    {
                        _facade.Publish(null, "data");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });

                builder.Add("Publish_EmptyEventName_ShouldThrow", () =>
                {
                    try
                    {
                        _facade.Publish("", "data");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_TypedEventPublishSubscribe()
        {
            var result = RunTestGroup("TypedEventPublishSubscribe", builder =>
            {
                builder.Add("Publish_TypedEvent_ShouldInvokeHandler", () =>
                {
                    TestEvent? received = null;
                    _facade.Subscribe<TestEvent>(this, e => received = e);
                    var sent = new TestEvent { Id = 42, Name = "test" };
                    _facade.Publish(sent);
                    return received != null && received.Id == 42 && received.Name == "test";
                });

                builder.Add("Publish_TypedEvent_MultipleSubscribers_ShouldInvokeAll", () =>
                {
                    var calls = 0;
                    var sub1 = new object();
                    var sub2 = new object();
                    _facade.Subscribe<TestEvent>(sub1, _ => calls++);
                    _facade.Subscribe<TestEvent>(sub2, _ => calls++);
                    _facade.Publish(new TestEvent());
                    return calls == 2;
                });

                builder.Add("Publish_TypedEvent_NoSubscribers_ShouldNotThrow", () =>
                {
                    _facade.Publish(new TestEvent());
                    return true;
                });

                builder.Add("Publish_TypedEvent_NullEvent_ShouldThrow", () =>
                {
                    try
                    {
                        _facade.Publish<TestEvent>(null);
                        return false;
                    }
                    catch (ArgumentNullException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_Unsubscribe()
        {
            var result = RunTestGroup("Unsubscribe", builder =>
            {
                builder.Add("Unsubscribe_NamedEvent_ShouldNotInvokeAfterUnsubscribe", () =>
                {
                    var calls = 0;
                    Action<object?> handler = _ => calls++;
                    _facade.Subscribe("unsubEvent", handler);
                    _facade.Publish("unsubEvent", null);
                    _facade.Unsubscribe("unsubEvent", handler);
                    _facade.Publish("unsubEvent", null);
                    return calls == 1;
                });

                builder.Add("Unsubscribe_TypedEvent_ShouldNotInvokeAfterUnsubscribe", () =>
                {
                    var calls = 0;
                    var sub = new object();
                    _facade.Subscribe<TestEvent>(sub, _ => calls++);
                    _facade.Publish(new TestEvent());
                    _facade.Unsubscribe<TestEvent>(sub);
                    _facade.Publish(new TestEvent());
                    return calls == 1;
                });

                builder.Add("Unsubscribe_NonExistentHandler_ShouldNotThrow", () =>
                {
                    _facade.Unsubscribe("nonExistent", _ => { });
                    return true;
                });

                builder.Add("Unsubscribe_NonExistentSubscriber_ShouldNotThrow", () =>
                {
                    _facade.Unsubscribe<TestEvent>(new object());
                    return true;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_HasSubscribers()
        {
            var result = RunTestGroup("HasSubscribers", builder =>
            {
                builder.Add("HasSubscribers_WithSubscriber_ShouldReturnTrue", () =>
                {
                    _facade.Subscribe("checkEvent", _ => { });
                    return _facade.HasSubscribers("checkEvent");
                });

                builder.Add("HasSubscribers_WithoutSubscriber_ShouldReturnFalse", () =>
                {
                    return !_facade.HasSubscribers("nonExistentEvent");
                });

                builder.Add("HasSubscribers_AfterUnsubscribe_ShouldReturnFalse", () =>
                {
                    Action<object?> handler = _ => { };
                    _facade.Subscribe("tempEvent", handler);
                    _facade.Unsubscribe("tempEvent", handler);
                    return !_facade.HasSubscribers("tempEvent");
                });

                builder.Add("HasSubscribers_EmptyEventName_ShouldThrow", () =>
                {
                    try
                    {
                        _facade.HasSubscribers("");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ClearAll()
        {
            var result = RunTestGroup("ClearAll", builder =>
            {
                builder.Add("ClearAll_ShouldRemoveAllSubscribers", () =>
                {
                    _facade.Subscribe("event1", _ => { });
                    _facade.Subscribe("event2", _ => { });
                    _facade.Subscribe<TestEvent>(new object(), _ => { });
                    _facade.ClearAll();
                    return !_facade.HasSubscribers("event1")
                        && !_facade.HasSubscribers("event2");
                });

                builder.Add("ClearAll_AfterClear_PublishShouldNotThrow", () =>
                {
                    _facade.Subscribe("evt", _ => { });
                    _facade.ClearAll();
                    _facade.Publish("evt", "data");
                    return true;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_AsyncHandler()
        {
            var result = RunTestGroup("AsyncHandler", builder =>
            {
                builder.Add("Subscribe_AsyncHandler_ShouldInvoke", () =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _facade.Subscribe("asyncEvent", async data =>
                    {
                        await Task.Delay(10);
                        tcs.TrySetResult(true);
                    });
                    _facade.Publish("asyncEvent", "data");
                    return tcs.Task.Wait(1000);
                });

                builder.Add("Unsubscribe_AsyncHandler_ShouldNotInvokeAfterUnsubscribe", () =>
                {
                    var calls = 0;
                    Func<object?, Task> asyncHandler = async data =>
                    {
                        await Task.Delay(1);
                        calls++;
                    };
                    _facade.Subscribe("asyncUnsub", asyncHandler);
                    _facade.Publish("asyncUnsub", null);
                    Task.Delay(50).Wait();
                    _facade.Unsubscribe("asyncUnsub", asyncHandler);
                    _facade.Publish("asyncUnsub", null);
                    Task.Delay(50).Wait();
                    return calls == 1;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DefaultEventBusCore()
        {
            var result = RunTestGroup("DefaultEventBusCore", builder =>
            {
                builder.Add("Constructor_WithCustomBus_ShouldUseCustomBus", () =>
                {
                    var customBus = new DefaultEventBusCore();
                    object? received = null;
                    customBus.Subscribe("custom", data => received = data);
                    var facade = new EventBusFacade(customBus);
                    facade.Publish("custom", "viaCustom");
                    return received as string == "viaCustom";
                });

                builder.Add("DefaultEventBusCore_Dispose_ShouldClearHandlers", () =>
                {
                    var bus = new DefaultEventBusCore();
                    bus.Subscribe("disposeTest", _ => { });
                    bus.Dispose();
                    bus.Publish("disposeTest", null);
                    return true;
                });

                builder.Add("DefaultEventBusCore_BindEvent_ShouldThrowNotSupported", () =>
                {
                    var bus = new DefaultEventBusCore();
                    try
                    {
                        bus.BindEvent(new object(), "Click", (EventHandler)((s, e) => { }));
                        return false;
                    }
                    catch (NotSupportedException)
                    {
                        return true;
                    }
                });

                builder.Add("DefaultEventBusCore_UnbindEvent_ShouldThrowNotSupported", () =>
                {
                    var bus = new DefaultEventBusCore();
                    try
                    {
                        bus.UnbindEvent(new object(), "Click");
                        return false;
                    }
                    catch (NotSupportedException)
                    {
                        return true;
                    }
                });
            });

            result.AssertAllPassed();
        }

        public class TestEvent
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
