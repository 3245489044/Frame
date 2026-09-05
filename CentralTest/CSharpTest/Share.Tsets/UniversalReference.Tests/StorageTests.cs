
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Storage.Local;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class StorageTests : UniversalReferenceTestBase
{
    [TestInitialize]
    public void Init() => CreateTempDir("StorageTests");

    [TestMethod]
    public async Task Storage_AllTests()
    {
        var result = await new TestGroupBuilder("Storage")
            .WithIsolatedDir("StoreAndRetrieve_ShouldReturnStoredData", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                var data = new Dictionary<string, object>
                {
                    { "name", "Test Workflow" },
                    { "version", "1.0.0" },
                    { "active", true }
                };

                await storage.StoreAsync("workflow-001", data);
                var retrieved = await storage.RetrieveAsync("workflow-001");

                return retrieved != null
                    && retrieved["name"] as string == "Test Workflow"
                    && retrieved["version"] as string == "1.0.0";
            })
            .WithIsolatedDir("Exists_ShouldReturnTrueForExistingData", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                await storage.StoreAsync("workflow-001", new Dictionary<string, object> { { "name", "test" } });

                return await storage.ExistsAsync("workflow-001");
            })
            .WithIsolatedDir("Exists_ShouldReturnFalseForNonExistingData", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                return !await storage.ExistsAsync("workflow-999");
            })
            .WithIsolatedDir("GetAllWorkflowIds_ShouldReturnCorrectCount", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                await storage.StoreAsync("wf-1", new Dictionary<string, object> { { "name", "WF1" } });
                await storage.StoreAsync("wf-2", new Dictionary<string, object> { { "name", "WF2" } });

                var allIds = await storage.GetAllWorkflowIdsAsync();
                return allIds.Count == 2;
            })
            .WithIsolatedDir("UpdateData_ShouldReflectChanges", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                await storage.StoreAsync("workflow-001", new Dictionary<string, object> { { "name", "Original" } });
                await storage.StoreAsync("workflow-001", new Dictionary<string, object> { { "name", "Updated" }, { "version", "2.0" } });

                var retrieved = await storage.RetrieveAsync("workflow-001");
                return retrieved != null && retrieved["name"] as string == "Updated";
            })
            .WithIsolatedDir("DeleteData_ShouldRemoveData", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                await storage.StoreAsync("workflow-001", new Dictionary<string, object> { { "name", "test" } });
                await storage.DeleteAsync("workflow-001");

                return !await storage.ExistsAsync("workflow-001");
            })
            .WithIsolatedDir("DeleteAll_ShouldClearAllData", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<Dictionary<string, object>>(dir);
                await storage.StoreAsync("wf-1", new Dictionary<string, object> { { "name", "WF1" } });
                await storage.StoreAsync("wf-2", new Dictionary<string, object> { { "name", "WF2" } });

                await storage.DeleteAsync("wf-1");
                await storage.DeleteAsync("wf-2");

                var allIds = await storage.GetAllWorkflowIdsAsync();
                return allIds.Count == 0;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
