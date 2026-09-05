
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Managers;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.ReferenceManagement.Managers;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class ReferenceManagementTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task ReferenceManagement_AllTests()
    {
        var result = await new TestGroupBuilder("ReferenceManagement")
            .Add("EstablishReference_ShouldCreateRelation", async () =>
            {
                var innerManager = new DefaultUniversalReferenceManager();
                await innerManager.InitializeAsync();
                var referenceManager = new GraphReferenceManager(innerManager, NullLogger<GraphReferenceManager>.Instance);

                var workflowResource = new ResourceIdentifier { ResourceType = "workflow", Id = "order-processing" };
                var actionResource = new ResourceIdentifier { ResourceType = "action", Id = "send-email" };

                await referenceManager.EstablishReferenceAsync(workflowResource, actionResource, "uses");
                var references = await referenceManager.GetReferencesAsync(workflowResource, "uses");

                return references.Count == 1;
            })
            .Add("EstablishReference_MultipleRelations_ShouldWork", async () =>
            {
                var innerManager = new DefaultUniversalReferenceManager();
                await innerManager.InitializeAsync();
                var referenceManager = new GraphReferenceManager(innerManager, NullLogger<GraphReferenceManager>.Instance);

                var workflowResource = new ResourceIdentifier { ResourceType = "workflow", Id = "order-processing" };
                var actionResource = new ResourceIdentifier { ResourceType = "action", Id = "send-email" };
                var templateResource = new ResourceIdentifier { ResourceType = "template", Id = "email-template" };

                await referenceManager.EstablishReferenceAsync(workflowResource, actionResource, "uses");
                await referenceManager.EstablishReferenceAsync(actionResource, templateResource, "depends_on");

                var workflowRefs = await referenceManager.GetReferencesAsync(workflowResource, "uses");
                var actionRefs = await referenceManager.GetReferencesAsync(actionResource, "depends_on");

                return workflowRefs.Count == 1 && actionRefs.Count == 1;
            })
            .Add("RemoveAllReferences_ShouldClearRelations", async () =>
            {
                var innerManager = new DefaultUniversalReferenceManager();
                await innerManager.InitializeAsync();
                var referenceManager = new GraphReferenceManager(innerManager, NullLogger<GraphReferenceManager>.Instance);

                var workflowResource = new ResourceIdentifier { ResourceType = "workflow", Id = "order-processing" };
                var actionResource = new ResourceIdentifier { ResourceType = "action", Id = "send-email" };

                await referenceManager.EstablishReferenceAsync(workflowResource, actionResource, "uses");
                await referenceManager.RemoveAllReferencesAsync(workflowResource);

                var references = await referenceManager.GetReferencesAsync(workflowResource, "uses");
                return references.Count == 0;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
