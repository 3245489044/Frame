
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class EventDrivenTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task EventDriven_AllTests()
    {
        var result = await new TestGroupBuilder("EventDriven")
            .Add("AddResource_ShouldWork", async () =>
            {
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resourceId = new ResourceIdentifier { ResourceType = "document", Id = "doc-001" };
                await manager.AddResourceAsync(resourceId, new Dictionary<string, object> { { "title", "测试文档" } });

                var retrieved = await manager.GetResourceAsync(resourceId);
                return retrieved != null && retrieved.Identifier.Id == "doc-001";
            })
            .Add("UpdateResource_ShouldWork", async () =>
            {
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resourceId = new ResourceIdentifier { ResourceType = "document", Id = "doc-001" };
                await manager.AddResourceAsync(resourceId, new Dictionary<string, object> { { "title", "测试文档" } });
                await manager.UpdateResourceAsync(resourceId, new Dictionary<string, object> { { "title", "更新后的文档" } }, "DOC_V2");

                var retrieved = await manager.GetResourceAsync(resourceId);
                return retrieved != null && retrieved.Symbol == "DOC_V2";
            })
            .Add("RemoveResource_ShouldWork", async () =>
            {
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resourceId = new ResourceIdentifier { ResourceType = "document", Id = "doc-001" };
                await manager.AddResourceAsync(resourceId, new Dictionary<string, object> { { "title", "测试文档" } });
                await manager.RemoveResourceAsync(resourceId);

                var retrieved = await manager.GetResourceAsync(resourceId);
                return retrieved == null;
            })
            .Add("MultipleOperations_ShouldWork", async () =>
            {
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var doc1 = new ResourceIdentifier { ResourceType = "document", Id = "doc-001" };
                var doc2 = new ResourceIdentifier { ResourceType = "document", Id = "doc-002" };

                await manager.AddResourceAsync(doc1, new Dictionary<string, object> { { "title", "文档1" } });
                await manager.UpdateResourceAsync(doc1, new Dictionary<string, object> { { "title", "更新文档1" } });
                await manager.AddResourceAsync(doc2, new Dictionary<string, object> { { "title", "文档2" } });
                await manager.RemoveResourceAsync(doc1);

                var allResources = await manager.GetAllResourcesAsync();
                return allResources.Count == 1;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
