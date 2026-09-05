
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class EventHandlingTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_CreateEventBus()
        {
            var result = new TestGroupBuilder("CreateEventBus")
                .Add("CreateEventBus_ShouldSucceed", () =>
                {
                    var world = CreateEcsWorld();
                    var eventBus = GraphFrameworkUsage.CreateEventBus(world.EntityManager);
                    return eventBus != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_GraphEventAdapter()
        {
            var result = new TestGroupBuilder("GraphEventAdapter")
                .Add("CreateGraphEventAdapter_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var world = CreateEcsWorld();
                    var eventBus = GraphFrameworkUsage.CreateEventBus(world.EntityManager);
                    var entityMap = GraphFrameworkUsage.CreateEntityMap<string>();
                    var adapter = GraphFrameworkUsage.CreateGraphEventAdapter(eventBus, entityMap, graph);
                    return adapter != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EventSubscription()
        {
            var result = new TestGroupBuilder("EventSubscription")
                .Add("OnEntityCreated_ShouldSubscribe", () =>
                {
                    var world = CreateEcsWorld();
                    var eventBus = GraphFrameworkUsage.CreateEventBus(world.EntityManager);
                    var received = false;
                    eventBus.OnEntityCreated(args => received = true);
                    return true;
                })
                .Add("OnEntityDestroyed_ShouldSubscribe", () =>
                {
                    var world = CreateEcsWorld();
                    var eventBus = GraphFrameworkUsage.CreateEventBus(world.EntityManager);
                    eventBus.OnEntityDestroyed(args => { });
                    return true;
                })
                .Add("OnComponentChanged_ShouldSubscribe", () =>
                {
                    var world = CreateEcsWorld();
                    var eventBus = GraphFrameworkUsage.CreateEventBus(world.EntityManager);
                    eventBus.OnComponentChanged(args => { });
                    return true;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
