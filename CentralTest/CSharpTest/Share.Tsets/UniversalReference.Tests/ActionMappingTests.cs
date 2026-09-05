
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.ActionMapping.Mappers;
using UniversalWorkflowFramework.ActionMapping.Models;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class ActionMappingTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task ActionMapping_AllTests()
    {
        var result = await new TestGroupBuilder("ActionMapping")
            .Add("Map_Click_ShouldReturnAIClick", async () =>
            {
                var mapper = ActionMapperFactory.Create<UIAction, AIAction>();
                await mapper.InitializeAsync();
                await mapper.AddMappingAsync("click", "ai_click", new Dictionary<string, string>
                {
                    { "buttonId", "elementId" },
                    { "text", "content" }
                });

                var uiAction = new UIAction
                {
                    ActionType = "click",
                    Parameters = new Dictionary<string, object>
                    {
                        { "buttonId", "submit-btn" },
                        { "text", "提交" }
                    }
                };

                var aiAction = await mapper.MapAsync(uiAction);
                return aiAction.ActionType == "ai_click"
                    && aiAction.Parameters["elementId"] as string == "submit-btn";
            })
            .Add("Map_Input_ShouldReturnAIInput", async () =>
            {
                var mapper = ActionMapperFactory.Create<UIAction, AIAction>();
                await mapper.InitializeAsync();
                await mapper.AddMappingAsync("input", "ai_input", new Dictionary<string, string>
                {
                    { "fieldId", "targetElement" }
                });

                var uiAction = new UIAction
                {
                    ActionType = "input",
                    Parameters = new Dictionary<string, object>
                    {
                        { "fieldId", "username" },
                        { "value", "testuser" }
                    }
                };

                var aiAction = await mapper.MapAsync(uiAction);
                return aiAction.ActionType == "ai_input";
            })
            .Add("Map_Submit_ShouldReturnAISubmit", async () =>
            {
                var mapper = ActionMapperFactory.Create<UIAction, AIAction>();
                await mapper.InitializeAsync();
                await mapper.AddMappingAsync("submit", "ai_submit", new Dictionary<string, string>
                {
                    { "formId", "targetForm" },
                    { "data", "payload" }
                });

                var uiAction = new UIAction
                {
                    ActionType = "submit",
                    Parameters = new Dictionary<string, object>
                    {
                        { "formId", "login-form" },
                        { "data", "user-data" }
                    }
                };

                var aiAction = await mapper.MapAsync(uiAction);
                return aiAction.ActionType == "ai_submit"
                    && aiAction.Parameters["targetForm"] as string == "login-form";
            })
            .Add("GetAllMappings_ShouldReturnCorrectCount", async () =>
            {
                var mapper = ActionMapperFactory.Create<UIAction, AIAction>();
                await mapper.InitializeAsync();
                await mapper.AddMappingAsync("click", "ai_click", new());
                await mapper.AddMappingAsync("input", "ai_input", new());
                await mapper.AddMappingAsync("submit", "ai_submit", new());

                var allMappings = await mapper.GetAllMappingsAsync();
                return allMappings.Count == 3;
            })
            .Add("RemoveMapping_ShouldDecreaseCount", async () =>
            {
                var mapper = ActionMapperFactory.Create<UIAction, AIAction>();
                await mapper.InitializeAsync();
                await mapper.AddMappingAsync("click", "ai_click", new());
                await mapper.AddMappingAsync("input", "ai_input", new());

                await mapper.RemoveMappingAsync("click");
                var allMappings = await mapper.GetAllMappingsAsync();
                return allMappings.Count == 1;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
