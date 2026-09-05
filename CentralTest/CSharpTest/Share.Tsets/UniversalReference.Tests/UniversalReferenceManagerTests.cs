
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
public class UniversalReferenceManagerTests : UniversalReferenceTestBase
{
    [TestInitialize]
    public void Init() => CreateTempDir("URManagerTests");

    [TestMethod]
    public async Task UniversalReferenceManager_AllTests()
    {
        var result = await new TestGroupBuilder("UniversalReferenceManager")
            .WithIsolatedDir("AddResource_ShouldStoreResource", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resource = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                await manager.AddResourceAsync(resource, new Dictionary<string, object>
                {
                    { "name", "测试工作流" },
                    { "version", "1.0.0" }
                }, "WORKFLOW");

                var retrieved = await manager.GetResourceAsync(resource);
                return retrieved != null
                    && retrieved.Identifier.ResourceType == "workflow"
                    && retrieved.Identifier.Id == "test-workflow";
            })
            .WithIsolatedDir("ContainsResource_ShouldReturnTrueForExistingResource", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resource = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                await manager.AddResourceAsync(resource, new Dictionary<string, object> { { "name", "test" } }, "WF");

                return manager.ContainsResource(resource);
            })
            .WithIsolatedDir("GetAllResources_ShouldReturnCorrectCount", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                await manager.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "workflow", Id = "wf-1" }, new() { { "name", "WF1" } }, "WF");
                await manager.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "action", Id = "act-1" }, new() { { "name", "ACT1" } }, "ACT");
                await manager.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "template", Id = "tpl-1" }, new() { { "name", "TPL1" } }, "TPL");

                var allResources = await manager.GetAllResourcesAsync();
                return allResources.Count == 3;
            })
            .WithIsolatedDir("AddReference_ShouldCreateRelation", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var workflow = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                var action = new ResourceIdentifier { ResourceType = "action", Id = "test-action" };

                await manager.AddResourceAsync(workflow, new() { { "name", "WF" } }, "WF");
                await manager.AddResourceAsync(action, new() { { "name", "ACT" } }, "ACT");
                await manager.AddReferenceAsync(workflow, action, ReferenceType.Dependency, "step1", "依赖", 0.8);

                var references = await manager.GetReferencesAsync(workflow);
                return references.Count >= 1;
            })
            .WithIsolatedDir("GetDependencies_ShouldReturnDependencies", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var workflow = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                var action = new ResourceIdentifier { ResourceType = "action", Id = "test-action" };
                var template = new ResourceIdentifier { ResourceType = "template", Id = "test-template" };

                await manager.AddResourceAsync(workflow, new() { { "name", "WF" } }, "WF");
                await manager.AddResourceAsync(action, new() { { "name", "ACT" } }, "ACT");
                await manager.AddResourceAsync(template, new() { { "name", "TPL" } }, "TPL");

                await manager.AddReferenceAsync(workflow, action, ReferenceType.Dependency, "step1", "依赖", 0.8);
                await manager.AddReferenceAsync(action, template, ReferenceType.Uses, "process", "使用", 0.9);

                var dependencies = await manager.GetDependenciesAsync(workflow, maxDepth: 2);
                return dependencies.Count >= 1;
            })
            .WithIsolatedDir("GetDependents_ShouldReturnDependents", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var workflow = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                var action = new ResourceIdentifier { ResourceType = "action", Id = "test-action" };
                var template = new ResourceIdentifier { ResourceType = "template", Id = "test-template" };

                await manager.AddResourceAsync(workflow, new() { { "name", "WF" } }, "WF");
                await manager.AddResourceAsync(action, new() { { "name", "ACT" } }, "ACT");
                await manager.AddResourceAsync(template, new() { { "name", "TPL" } }, "TPL");

                await manager.AddReferenceAsync(workflow, action, ReferenceType.Dependency, "step1", "依赖", 0.8);
                await manager.AddReferenceAsync(action, template, ReferenceType.Uses, "process", "使用", 0.9);

                var dependents = await manager.GetDependentsAsync(template);
                return dependents.Count >= 1;
            })
            .WithIsolatedDir("GetResourcesByType_ShouldFilterByType", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                await manager.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "workflow", Id = "wf-1" }, new() { { "name", "WF1" } }, "WF");
                await manager.AddResourceAsync(
                    new ResourceIdentifier { ResourceType = "action", Id = "act-1" }, new() { { "name", "ACT1" } }, "ACT");

                var workflows = await manager.GetResourcesByTypeAsync("workflow");
                return workflows.Count == 1;
            })
            .WithIsolatedDir("UpdateResource_ShouldReflectChanges", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var resource = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                await manager.AddResourceAsync(resource, new() { { "name", "Original" } }, "WF");
                await manager.UpdateResourceAsync(resource, new() { { "name", "Updated" } }, "WF_V2");

                var retrieved = await manager.GetResourceAsync(resource);
                return retrieved != null && retrieved.Symbol == "WF_V2";
            })
            .WithIsolatedDir("RemoveResource_ShouldCascadeDeleteReferences", async (dir) =>
            {
                UniversalReferenceManagerFactory.SetDataDirectory(dir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var workflow = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                var action = new ResourceIdentifier { ResourceType = "action", Id = "test-action" };

                await manager.AddResourceAsync(workflow, new() { { "name", "WF" } }, "WF");
                await manager.AddResourceAsync(action, new() { { "name", "ACT" } }, "ACT");
                await manager.AddReferenceAsync(workflow, action, ReferenceType.Dependency, "step1", "依赖", 0.8);

                await manager.RemoveResourceAsync(action);

                var references = await manager.GetReferencesAsync(workflow);
                return references.Count == 0;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
