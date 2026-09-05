using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class NotifyRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_Events()
    {
        var result = new TestGroupBuilder("NotifyRegistry_Events")
            .Add("ItemRegistered_ShouldFireOnRegister", () =>
            {
                var registry = new ObservableMapRegistry<string, string>();
                string? firedKey = null;
                string? firedValue = null;
                registry.ItemRegistered += (sender, args) =>
                {
                    firedKey = args.Key;
                    firedValue = args.NewValue;
                };
                registry.Register("key", "value");
                return firedKey == "key" && firedValue == "value";
            })
            .Add("ItemUnregistered_ShouldFireOnUnregister", () =>
            {
                var registry = new ObservableMapRegistry<string, string>();
                string? firedKey = null;
                string? firedValue = null;
                registry.ItemUnregistered += (sender, args) =>
                {
                    firedKey = args.Key;
                    firedValue = args.OldValue;
                };
                registry.Register("key", "value");
                registry.Unregister("key");
                return firedKey == "key" && firedValue == "value";
            })
            .Add("RegistryCleared_ShouldFireOnClear", () =>
            {
                var registry = new ObservableMapRegistry<string, string>();
                bool cleared = false;
                registry.RegistryCleared += (sender, args) => { cleared = true; };
                registry.Register("key", "value");
                registry.Clear();
                return cleared;
            })
            .Add("MultipleEvents_ShouldFireIndependently", () =>
            {
                var registry = new ObservableMapRegistry<string, int>();
                int registerCount = 0;
                int unregisterCount = 0;
                registry.ItemRegistered += (_, _) => registerCount++;
                registry.ItemUnregistered += (_, _) => unregisterCount++;
                registry.Register("a", 1);
                registry.Register("b", 2);
                registry.Unregister("a");
                return registerCount == 2 && unregisterCount == 1;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_EventData()
    {
        var result = new TestGroupBuilder("NotifyRegistry_EventData")
            .Add("EventArgs_ShouldContainCorrectData", () =>
            {
                var registry = new ObservableMapRegistry<string, int>();
                RegistryChangedEventArgs<string, int>? captured = null;
                registry.ItemRegistered += (_, args) => captured = args;
                registry.Register("answer", 42);
                return captured != null
                    && captured.Key == "answer"
                    && captured.NewValue == 42
                    && captured.ChangeType == RegistryChangeType.Registered;
            })
            .Run();

        result.AssertAllPassed();
    }
}
