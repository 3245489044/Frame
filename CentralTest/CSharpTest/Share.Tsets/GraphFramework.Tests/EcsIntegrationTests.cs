
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class EcsIntegrationTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_CreateRunner()
        {
            var result = new TestGroupBuilder("CreateRunner")
                .Add("CreateRunner_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var runner = GraphFrameworkUsage.CreateRunner(graph);
                    return runner != null;
                })
                .Add("CreateRunnerWithDefaults_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var runner = GraphFrameworkUsage.CreateRunnerWithDefaults(graph);
                    return runner != null;
                })
                .Add("CreateRunnerWithAllSystems_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var runner = GraphFrameworkUsage.CreateRunnerWithAllSystems(graph);
                    return runner != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EntityAdapter()
        {
            var result = new TestGroupBuilder("EntityAdapter")
                .Add("CreateEntityAdapter_ShouldSucceed", () =>
                {
                    var graph = CreatePopulatedStringGraph();
                    var world = CreateEcsWorld();
                    var adapter = GraphFrameworkUsage.CreateEntityAdapter(graph, world.EntityManager);
                    return adapter != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EcsIndexManager()
        {
            var result = new TestGroupBuilder("EcsIndexManager")
                .Add("CreateIndexManager_ShouldSucceed", () =>
                {
                    var world = CreateEcsWorld();
                    var indexManager = GraphFrameworkUsage.CreateIndexManager(world.EntityManager);
                    return indexManager != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EcsRelationManager()
        {
            var result = new TestGroupBuilder("EcsRelationManager")
                .Add("CreateRelationManager_ShouldSucceed", () =>
                {
                    var world = CreateEcsWorld();
                    var relationManager = GraphFrameworkUsage.CreateRelationManager(world.EntityManager);
                    return relationManager != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EcsTransaction()
        {
            var result = new TestGroupBuilder("EcsTransaction")
                .Add("CreateTransaction_ShouldSucceed", () =>
                {
                    var world = CreateEcsWorld();
                    var transaction = GraphFrameworkUsage.CreateTransaction(world, "test");
                    return transaction != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_EntityMap()
        {
            var result = new TestGroupBuilder("EntityMap")
                .Add("CreateEntityMap_ShouldSucceed", () =>
                {
                    var entityMap = GraphFrameworkUsage.CreateEntityMap<string>();
                    return entityMap != null;
                })
                .Add("CreateEntityMap_WithKeyToString_ShouldSucceed", () =>
                {
                    var entityMap = GraphFrameworkUsage.CreateEntityMap<string>(k => k.ToString());
                    return entityMap != null;
                })
                .Run();
            result.AssertAllPassed();
        }
    }
}
