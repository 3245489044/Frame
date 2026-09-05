
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class GraphDatabaseStorageTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task GraphDatabaseStorage_AllTests()
    {
        var result = await new TestGroupBuilder("GraphDatabaseStorage")
            .WithIsolatedDir("StoreAndGetResource_ShouldReturnStoredData", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                var resourceId = new ResourceIdentifier { ResourceType = "node", Id = "graph-node-001" };
                await graphStorage.AddResourceAsync(resourceId, new Dictionary<string, object>
                {
                    { "label", "Start" },
                    { "type", "initial" }
                }, "START_NODE");

                var retrieved = await graphStorage.GetResourceAsync(resourceId);
                return retrieved != null && retrieved.Identifier.Id == "graph-node-001";
            })
            .WithIsolatedDir("StoreMultipleNodes_ShouldBeRetrievable", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                var node1 = new ResourceIdentifier { ResourceType = "node", Id = "graph-node-001" };
                var node2 = new ResourceIdentifier { ResourceType = "node", Id = "graph-node-002" };

                await graphStorage.AddResourceAsync(node1, new Dictionary<string, object> { { "label", "Start" } }, "START_NODE");
                await graphStorage.AddResourceAsync(node2, new Dictionary<string, object> { { "label", "End" } }, "END_NODE");

                var retrieved1 = await graphStorage.GetResourceAsync(node1);
                var retrieved2 = await graphStorage.GetResourceAsync(node2);

                return retrieved1 != null && retrieved2 != null;
            })
            .WithIsolatedDir("StoreEdge_ShouldBeRetrievable", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                var node1 = new ResourceIdentifier { ResourceType = "node", Id = "gn-001" };
                var node2 = new ResourceIdentifier { ResourceType = "node", Id = "gn-002" };

                await graphStorage.AddResourceAsync(node1, new() { { "label", "Start" } }, "NODE");
                await graphStorage.AddResourceAsync(node2, new() { { "label", "End" } }, "NODE");

                await graphStorage.AddReferenceAsync(node1, node2, ReferenceType.Dependency, "edge", "连接", 1.0);

                var retrieved1 = await graphStorage.GetResourceAsync(node1);
                var retrieved2 = await graphStorage.GetResourceAsync(node2);
                return retrieved1 != null && retrieved2 != null;
            })
            .WithIsolatedDir("GetResourcesByType_ShouldFilterByType", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                await graphStorage.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "node", Id = "n1" }, new() { { "label", "N1" } }, "NODE");
                await graphStorage.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "node", Id = "n2" }, new() { { "label", "N2" } }, "NODE");
                await graphStorage.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "edge", Id = "e1" }, new() { { "source", "n1" }, { "target", "n2" } }, "EDGE");

                var nodes = await graphStorage.GetResourcesByTypeAsync("node");
                var edges = await graphStorage.GetResourcesByTypeAsync("edge");

                return nodes.Count == 2 && edges.Count == 1;
            })
            .WithIsolatedDir("UpdateResource_ShouldReflectChanges", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                var resourceId = new ResourceIdentifier { ResourceType = "node", Id = "gn-001" };
                await graphStorage.AddResourceAsync(resourceId, new() { { "label", "Original" }, { "type", "initial" } }, "NODE");
                await graphStorage.UpdateResourceAsync(resourceId, new() { { "label", "Updated" }, { "type", "initial" }, { "color", "red" } }, "NODE_V2");

                var retrieved = await graphStorage.GetResourceAsync(resourceId);
                return retrieved != null;
            })
            .WithIsolatedDir("DeleteResource_ShouldRemoveData", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var graphStorage = UniversalReferenceManagerFactory.Create();
                await graphStorage.InitializeAsync();

                var edge = new ResourceIdentifier { ResourceType = "edge", Id = "ge-001" };
                await graphStorage.AddResourceAsync(edge, new() { { "source", "n1" }, { "target", "n2" } }, "EDGE");
                await graphStorage.RemoveResourceAsync(edge);

                var edges = await graphStorage.GetResourcesByTypeAsync("edge");
                return edges.Count == 0;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
