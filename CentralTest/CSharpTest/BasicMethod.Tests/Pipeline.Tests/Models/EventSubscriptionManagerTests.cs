using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pipeline.Tests.Models
{
    [TestClass]
    public class EventSubscriptionManagerTests : TestBase
    {
        [TestMethod]
        public void RunAll_EventSubscriptionManagerTests()
        {
            var result = RunTestGroup("EventSubscriptionManager", builder =>
            {
                builder.Add("Subscribe_ShouldAddHandler", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    manager.Subscribe("event1", handler);

                    Assert.IsTrue(manager.HasSubscribers("event1"));
                });

                builder.Add("Subscribe_WithMultipleHandlers_ShouldAddAll", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler1 = new EventHandler((sender, args) => { });
                    var handler2 = new EventHandler((sender, args) => { });

                    manager.Subscribe("event1", handler1);
                    manager.Subscribe("event1", handler2);

                    Assert.AreEqual(2, manager.GetSubscriberCount("event1"));
                });

                builder.Add("Unsubscribe_ShouldRemoveHandler", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    manager.Subscribe("event1", handler);
                    Assert.IsTrue(manager.HasSubscribers("event1"));

                    manager.Unsubscribe("event1", handler);
                    Assert.IsFalse(manager.HasSubscribers("event1"));
                });

                builder.Add("Unsubscribe_NonExistentEvent_ShouldNotThrow", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    manager.Unsubscribe("nonexistent", handler);
                });

                builder.Add("Publish_ShouldInvokeHandlers", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var invoked = false;
                    var handler = new EventHandler((sender, args) => invoked = true);

                    manager.Subscribe("event1", handler);
                    manager.Publish("event1", this, EventArgs.Empty);

                    Assert.IsTrue(invoked);
                });

                builder.Add("Publish_WithMultipleHandlers_ShouldInvokeAll", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var invoked1 = false;
                    var invoked2 = false;
                    var handler1 = new EventHandler((sender, args) => invoked1 = true);
                    var handler2 = new EventHandler((sender, args) => invoked2 = true);

                    manager.Subscribe("event1", handler1);
                    manager.Subscribe("event1", handler2);
                    manager.Publish("event1", this, EventArgs.Empty);

                    Assert.IsTrue(invoked1);
                    Assert.IsTrue(invoked2);
                });

                builder.Add("Publish_NonExistentEvent_ShouldNotThrow", () =>
                {
                    var manager = new EventSubscriptionManager();

                    manager.Publish("nonexistent", this, EventArgs.Empty);
                });

                builder.Add("Clear_ShouldRemoveAllSubscriptions", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    manager.Subscribe("event1", handler);
                    manager.Subscribe("event2", handler);

                    manager.Clear();
                    Assert.IsFalse(manager.HasSubscribers("event1"));
                    Assert.IsFalse(manager.HasSubscribers("event2"));
                    Assert.AreEqual(0, manager.TotalSubscriberCount);
                });

                builder.Add("HasSubscribers_NonExistentEvent_ShouldReturnFalse", () =>
                {
                    var manager = new EventSubscriptionManager();

                    Assert.IsFalse(manager.HasSubscribers("nonexistent"));
                });

                builder.Add("GetSubscriberCount_ShouldReturnCorrectCount", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    Assert.AreEqual(0, manager.GetSubscriberCount("event1"));
                    manager.Subscribe("event1", handler);
                    Assert.AreEqual(1, manager.GetSubscriberCount("event1"));
                    manager.Subscribe("event1", handler);
                    Assert.AreEqual(2, manager.GetSubscriberCount("event1"));
                });

                builder.Add("TotalSubscriberCount_ShouldReflectAllEvents", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    Assert.AreEqual(0, manager.TotalSubscriberCount);
                    manager.Subscribe("event1", handler);
                    Assert.AreEqual(1, manager.TotalSubscriberCount);
                    manager.Subscribe("event2", handler);
                    Assert.AreEqual(2, manager.TotalSubscriberCount);
                    manager.Clear();
                    Assert.AreEqual(0, manager.TotalSubscriberCount);
                });

                builder.Add("GetEvents_ShouldReturnAllEventNames", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    manager.Subscribe("event1", handler);
                    manager.Subscribe("event2", handler);

                    var events = manager.GetEvents().ToList();
                    Assert.AreEqual(2, events.Count);
                    Assert.IsTrue(events.Contains("event1"));
                    Assert.IsTrue(events.Contains("event2"));
                });

                builder.Add("Subscribe_WithNullEventName_ShouldThrow", () =>
                {
                    var manager = new EventSubscriptionManager();
                    var handler = new EventHandler((sender, args) => { });

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        manager.Subscribe(null!, handler));
                });

                builder.Add("Subscribe_WithNullHandler_ShouldThrow", () =>
                {
                    var manager = new EventSubscriptionManager();

                    Assert.ThrowsException<ArgumentNullException>(() =>
                        manager.Subscribe("event1", null!));
                });
            });
            result.AssertAllPassed();
        }
    }

    [TestClass]
    public class SessionTests : TestBase
    {
        [TestMethod]
        public void RunAll_SessionTests()
        {
            var result = RunTestGroup("Session", builder =>
            {
                builder.Add("Constructor_ShouldCreateValidSession", () =>
                {
                    var session = new Session("session1");

                    Assert.AreEqual("session1", session.Id);
                    Assert.IsNotNull(session.CreatedAt);
                    Assert.IsFalse(session.IsExpired);
                });

                builder.Add("Set_And_Get_ShouldWork", () =>
                {
                    var session = new Session("session1");
                    session.Set("key1", "value1");

                    Assert.AreEqual("value1", session.Get<string>("key1"));
                });

                builder.Add("Get_NonExistent_ShouldReturnDefault", () =>
                {
                    var session = new Session("session1");

                    Assert.IsNull(session.Get<string>("nonexistent"));
                });

                builder.Add("Has_ShouldReturnCorrectly", () =>
                {
                    var session = new Session("session1");
                    session.Set("key1", "value1");

                    Assert.IsTrue(session.Has("key1"));
                    Assert.IsFalse(session.Has("nonexistent"));
                });

                builder.Add("Remove_ShouldRemoveItem", () =>
                {
                    var session = new Session("session1");
                    session.Set("key1", "value1");

                    Assert.IsTrue(session.Remove("key1"));
                    Assert.IsFalse(session.Has("key1"));
                });

                builder.Add("Remove_NonExistent_ShouldReturnFalse", () =>
                {
                    var session = new Session("session1");

                    Assert.IsFalse(session.Remove("nonexistent"));
                });

                builder.Add("Clear_ShouldRemoveAllItems", () =>
                {
                    var session = new Session("session1");
                    session.Set("key1", "value1");
                    session.Set("key2", "value2");

                    session.Clear();
                    Assert.IsFalse(session.Has("key1"));
                    Assert.IsFalse(session.Has("key2"));
                });

                builder.Add("Expire_ShouldMarkAsExpired", () =>
                {
                    var session = new Session("session1");
                    session.Expire();

                    Assert.IsTrue(session.IsExpired);
                });

                builder.Add("Expire_WithReason_ShouldSetReason", () =>
                {
                    var session = new Session("session1");
                    session.Expire("timeout");

                    Assert.IsTrue(session.IsExpired);
                    Assert.AreEqual("timeout", session.ExpirationReason);
                });

                builder.Add("Touch_ShouldUpdateLastAccessed", () =>
                {
                    var session = new Session("session1");
                    var before = session.LastAccessed;

                    Thread.Sleep(10);
                    session.Touch();

                    Assert.IsTrue(session.LastAccessed > before);
                });

                builder.Add("Touch_OnExpiredSession_ShouldThrow", () =>
                {
                    var session = new Session("session1");
                    session.Expire();

                    Assert.ThrowsException<InvalidOperationException>(() =>
                        session.Touch());
                });

                builder.Add("Set_OnExpiredSession_ShouldThrow", () =>
                {
                    var session = new Session("session1");
                    session.Expire();

                    Assert.ThrowsException<InvalidOperationException>(() =>
                        session.Set("key1", "value1"));
                });

                builder.Add("Get_OnExpiredSession_ShouldThrow", () =>
                {
                    var session = new Session("session1");
                    session.Expire();

                    Assert.ThrowsException<InvalidOperationException>(() =>
                        session.Get<string>("key1"));
                });

                builder.Add("ItemCount_ShouldReflectCount", () =>
                {
                    var session = new Session("session1");

                    Assert.AreEqual(0, session.ItemCount);
                    session.Set("key1", "value1");
                    Assert.AreEqual(1, session.ItemCount);
                    session.Set("key2", "value2");
                    Assert.AreEqual(2, session.ItemCount);
                    session.Remove("key1");
                    Assert.AreEqual(1, session.ItemCount);
                });

                builder.Add("GetKeys_ShouldReturnAllKeys", () =>
                {
                    var session = new Session("session1");
                    session.Set("key1", "value1");
                    session.Set("key2", "value2");

                    var keys = session.GetKeys().ToList();
                    Assert.AreEqual(2, keys.Count);
                    Assert.IsTrue(keys.Contains("key1"));
                    Assert.IsTrue(keys.Contains("key2"));
                });
            });
            result.AssertAllPassed();
        }
    }
}
