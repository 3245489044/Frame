
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Storage;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class LiteDBPersistenceTests : UniversalReferenceTestBase
{
    private string _tempDbPath = null!;

    [TestInitialize]
    public void Init()
    {
        _tempDbPath = CleanupHelper.GetTempFilePath(".db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        CleanupHelper.TryDeleteFile(_tempDbPath);
    }

    [TestMethod]
    public async Task LiteDBPersistence_AllTests()
    {
        var result = await new TestGroupBuilder("LiteDBPersistence")
            .WithIsolatedDb("SaveAndLoadNode_ShouldReturnSavedData", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                var nodeData = new Dictionary<string, object>
                {
                    { "name", "Start Node" },
                    { "type", "start" },
                    { "config", new Dictionary<string, object> { { "timeout", 30 } } }
                };
                await persistence.SaveNodeAsync("node-001", nodeData, "START");

                var loaded = await persistence.LoadNodeAsync("node-001");
                return loaded != null && loaded["name"] as string == "Start Node";
            })
            .WithIsolatedDb("SaveAndLoadEdge_ShouldReturnSavedData", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "Node1" } }, "NODE");
                await persistence.SaveNodeAsync("node-002", new Dictionary<string, object> { { "name", "Node2" } }, "NODE");

                var edgeData = new Dictionary<string, object> { { "condition", "status == 'approved'" }, { "priority", 1 } };
                await persistence.SaveEdgeAsync("edge-001", "node-001", "node-002", true, edgeData);

                var loaded = await persistence.LoadEdgeAsync("edge-001");
                return loaded != null;
            })
            .WithIsolatedDb("ContainsNode_ShouldReturnTrueForExistingNode", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "Node1" } }, "NODE");
                return await persistence.ContainsNodeAsync("node-001");
            })
            .WithIsolatedDb("ContainsNode_ShouldReturnFalseForNonExistingNode", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                return !await persistence.ContainsNodeAsync("node-999");
            })
            .WithIsolatedDb("GetAllNodeKeys_ShouldReturnCorrectCount", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "N1" } }, "A");
                await persistence.SaveNodeAsync("node-002", new Dictionary<string, object> { { "name", "N2" } }, "B");
                await persistence.SaveNodeAsync("node-003", new Dictionary<string, object> { { "name", "N3" } }, "C");

                var allNodes = await persistence.GetAllNodeKeysAsync();
                return allNodes.Count == 3;
            })
            .WithIsolatedDb("GetAllEdgeIds_ShouldReturnCorrectCount", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("n1", new Dictionary<string, object>(), "N");
                await persistence.SaveNodeAsync("n2", new Dictionary<string, object>(), "N");
                await persistence.SaveNodeAsync("n3", new Dictionary<string, object>(), "N");

                await persistence.SaveEdgeAsync("e1", "n1", "n2", true, new());
                await persistence.SaveEdgeAsync("e2", "n2", "n3", true, new());

                var allEdges = await persistence.GetAllEdgeIdsAsync();
                return allEdges.Count == 2;
            })
            .WithIsolatedDb("UpdateNode_ShouldReflectChanges", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "Original" } }, "NODE");
                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "Updated" }, { "version", 2 } }, "NODE_V2");

                var loaded = await persistence.LoadNodeAsync("node-001");
                return loaded != null && loaded["name"] as string == "Updated";
            })
            .WithIsolatedDb("RemoveNode_ShouldDecreaseCount", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "N1" } }, "A");
                await persistence.SaveNodeAsync("node-002", new Dictionary<string, object> { { "name", "N2" } }, "B");
                await persistence.SaveNodeAsync("node-003", new Dictionary<string, object> { { "name", "N3" } }, "C");

                await persistence.RemoveNodeAsync("node-003");

                var allNodes = await persistence.GetAllNodeKeysAsync();
                return allNodes.Count == 2;
            })
            .WithIsolatedDb("ClearAll_ShouldRemoveAllData", async (dbPath) =>
            {
                using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
                await persistence.InitializeAsync(dbPath);

                await persistence.SaveNodeAsync("node-001", new Dictionary<string, object> { { "name", "N1" } }, "A");
                await persistence.SaveNodeAsync("node-002", new Dictionary<string, object> { { "name", "N2" } }, "B");

                await persistence.ClearAllAsync();

                var allNodes = await persistence.GetAllNodeKeysAsync();
                return allNodes.Count == 0;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
