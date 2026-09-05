
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WdUI;

namespace WdUI.Tests
{
    [TestClass]
    public class WdUIUsageBindingTests : TestBase
    {
        [TestMethod]
        public void RunAll_DataSource()
        {
            var result = new TestGroupBuilder("DataSource")
                .Add("RegisterDataSource_ShouldNotThrow", () =>
                {
                    var source = new TestDataSource { Name = "Test" };
                    WdUIUsage.RegisterDataSource("testSource", source);
                    return true;
                })
                .Add("GetDataSource_Registered_ShouldReturnInstance", () =>
                {
                    var source = new TestDataSource { Name = "TestGet" };
                    WdUIUsage.RegisterDataSource("testGet", source);
                    var retrieved = WdUIUsage.GetDataSource<TestDataSource>("testGet");
                    return retrieved != null && retrieved.Name == "TestGet";
                })
                .Add("GetDataSource_NotRegistered_ShouldReturnNull", () =>
                {
                    var retrieved = WdUIUsage.GetDataSource<TestDataSource>("nonExistent");
                    return retrieved == null;
                })
                .Add("UpdateDataSource_ShouldNotThrow", () =>
                {
                    var source = new TestDataSource { Name = "TestUpdate" };
                    WdUIUsage.RegisterDataSource("testUpdate", source);
                    WdUIUsage.UpdateDataSource("testUpdate", new TestDataSource { Name = "Updated" });
                    return true;
                })
                .Add("UpdateDataSource_NotRegistered_ShouldNotThrow", () =>
                {
                    WdUIUsage.UpdateDataSource("nonExistent", "value");
                    return true;
                })
                .Add("SubscribeToProperty_ShouldNotThrow", () =>
                {
                    var source = new TestDataSource { Name = "TestSub" };
                    WdUIUsage.RegisterDataSource("testSub", source);
                    WdUIUsage.SubscribeToProperty("testSub", "Name", value => { });
                    return true;
                })
                .Run();

            result.AssertAllPassed(printSummary: true);
        }

        [TestMethod]
        public async Task RunAll_EventBus()
        {
            var result = await new TestGroupBuilder("EventBus")
                .Add("SubscribeAndPublish_ShouldTriggerHandler", async () =>
                {
                    var wasCalled = false;
                    WdUIUsage.SubscribeEvent("test.event", async data =>
                    {
                        wasCalled = true;
                        await Task.CompletedTask;
                    });

                    await WdUIUsage.PublishEventAsync("test.event");
                    return wasCalled;
                })
                .Add("PublishWithData_ShouldPassData", async () =>
                {
                    object? receivedData = null;
                    WdUIUsage.SubscribeEvent("test.dataEvent", async data =>
                    {
                        receivedData = data;
                        await Task.CompletedTask;
                    });

                    await WdUIUsage.PublishEventAsync("test.dataEvent", "hello");
                    return receivedData?.ToString() == "hello";
                })
                .Add("Unsubscribe_ShouldNotTrigger", async () =>
                {
                    var callCount = 0;
                    async Task handler(object? data)
                    {
                        callCount++;
                        await Task.CompletedTask;
                    }

                    WdUIUsage.SubscribeEvent("test.unsub", handler);
                    await WdUIUsage.PublishEventAsync("test.unsub");
                    WdUIUsage.UnsubscribeEvent("test.unsub", handler);
                    await WdUIUsage.PublishEventAsync("test.unsub");

                    return callCount == 1;
                })
                .Add("HasEventSubscribers_WithSubscriber_ShouldReturnTrue", () =>
                {
                    WdUIUsage.SubscribeEvent("test.hasSub", async data => await Task.CompletedTask);
                    return WdUIUsage.HasEventSubscribers("test.hasSub");
                })
                .Add("HasEventSubscribers_WithoutSubscriber_ShouldReturnFalse", () =>
                {
                    return !WdUIUsage.HasEventSubscribers("nonExistentEvent");
                })
                .Add("ClearAllEvents_ShouldRemoveAllSubscribers", async () =>
                {
                    var callCount = 0;
                    WdUIUsage.SubscribeEvent("test.clear1", async data =>
                    {
                        callCount++;
                        await Task.CompletedTask;
                    });
                    WdUIUsage.SubscribeEvent("test.clear2", async data =>
                    {
                        callCount++;
                        await Task.CompletedTask;
                    });

                    WdUIUsage.ClearAllEvents();
                    await WdUIUsage.PublishEventAsync("test.clear1");
                    await WdUIUsage.PublishEventAsync("test.clear2");

                    return callCount == 0;
                })
                .Add("PublishToNonExistentEvent_ShouldNotThrow", async () =>
                {
                    await WdUIUsage.PublishEventAsync("nonExistentEvent");
                    return true;
                })
                .RunAsync();

            result.AssertAllPassed(printSummary: true);
        }

        private class TestDataSource : INotifyPropertyChanged
        {
            private string _name = string.Empty;

            public string Name
            {
                get => _name;
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
