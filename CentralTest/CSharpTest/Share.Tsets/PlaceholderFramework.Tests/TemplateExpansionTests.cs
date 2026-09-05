
using System.Text.Json.Nodes;
using BasicMethod.Pipeline;
using CentralConsole.Testing;
using Common.PlaceholderFramework.TemplateExpansion.Abstractions;
using Common.PlaceholderFramework.TemplateExpansion.Converters;
using Common.PlaceholderFramework.TemplateExpansion.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlaceholderFramework.Tests;

[TestClass]
public class TemplateExpansionTests : TestBase
{
    [TestMethod]
    public void RunAll_JsonMerger()
    {
        var result = RunTestGroup("JsonMerger", builder =>
        {
            builder.Add("Merge_ObjectProperties_ShouldOverride", () =>
            {
                var target = JsonNode.Parse("""{"a":1,"b":2}""")!.AsObject();
                var source = JsonNode.Parse("""{"b":3,"c":4}""")!.AsObject();
                JsonMerger.Merge(target, source);
                return target["a"]!.GetValue<int>() == 1
                    && target["b"]!.GetValue<int>() == 3
                    && target["c"]!.GetValue<int>() == 4;
            });

            builder.Add("Merge_NestedObject_ShouldRecurse", () =>
            {
                var target = JsonNode.Parse("""{"nested":{"a":1,"b":2}}""")!.AsObject();
                var source = JsonNode.Parse("""{"nested":{"b":3,"c":4}}""")!.AsObject();
                JsonMerger.Merge(target, source);
                var nested = target["nested"]!.AsObject();
                return nested["a"]!.GetValue<int>() == 1
                    && nested["b"]!.GetValue<int>() == 3
                    && nested["c"]!.GetValue<int>() == 4;
            });

            builder.Add("Merge_ScalarOverrideObject_ShouldReplace", () =>
            {
                var target = JsonNode.Parse("""{"key":{"nested":"value"}}""")!.AsObject();
                var source = JsonNode.Parse("""{"key":"scalar"}""")!.AsObject();
                JsonMerger.Merge(target, source);
                return target["key"]!.GetValue<string>() == "scalar";
            });

            builder.Add("MergeArray_ById_ShouldDeduplicate", () =>
            {
                var target = JsonNode.Parse("""[{"id":"a","val":1},{"id":"b","val":2}]""")!.AsArray();
                var source = JsonNode.Parse("""[{"id":"b","val":3},{"id":"c","val":4}]""")!.AsArray();
                var targetObj = new JsonObject { ["items"] = target };
                var sourceObj = new JsonObject { ["items"] = source };
                JsonMerger.Merge(targetObj, sourceObj);
                var merged = targetObj["items"]!.AsArray();
                return merged.Count == 3;
            });

            builder.Add("MergeArray_ByKey_ShouldDeduplicate", () =>
            {
                var target = JsonNode.Parse("""[{"key":"x","val":1}]""")!.AsArray();
                var source = JsonNode.Parse("""[{"key":"x","val":2},{"key":"y","val":3}]""")!.AsArray();
                var targetObj = new JsonObject { ["items"] = target };
                var sourceObj = new JsonObject { ["items"] = source };
                JsonMerger.Merge(targetObj, sourceObj);
                var merged = targetObj["items"]!.AsArray();
                return merged.Count == 2;
            });

            builder.Add("MergeArray_ByName_ShouldDeduplicate", () =>
            {
                var target = JsonNode.Parse("""[{"name":"n1"},{"name":"n2"}]""")!.AsArray();
                var source = JsonNode.Parse("""[{"name":"n2"},{"name":"n3"}]""")!.AsArray();
                var targetObj = new JsonObject { ["items"] = target };
                var sourceObj = new JsonObject { ["items"] = source };
                JsonMerger.Merge(targetObj, sourceObj);
                var merged = targetObj["items"]!.AsArray();
                return merged.Count == 3;
            });

            builder.Add("MergeArray_NoId_ShouldUseDeepEquals", () =>
            {
                var target = JsonNode.Parse("""[{"val":1},{"val":2}]""")!.AsArray();
                var source = JsonNode.Parse("""[{"val":2},{"val":3}]""")!.AsArray();
                var targetObj = new JsonObject { ["items"] = target };
                var sourceObj = new JsonObject { ["items"] = source };
                JsonMerger.Merge(targetObj, sourceObj);
                var merged = targetObj["items"]!.AsArray();
                return merged.Count == 3;
            });

            builder.Add("MergeArray_Scalar_ShouldDeduplicate", () =>
            {
                var target = JsonNode.Parse("""["a","b"]""")!.AsArray();
                var source = JsonNode.Parse("""["b","c"]""")!.AsArray();
                var targetObj = new JsonObject { ["items"] = target };
                var sourceObj = new JsonObject { ["items"] = source };
                JsonMerger.Merge(targetObj, sourceObj);
                var merged = targetObj["items"]!.AsArray();
                return merged.Count == 3;
            });

            builder.Add("Merge_SkipKeys_ShouldExcludeKeys", () =>
            {
                var target = new JsonObject { ["keep"] = JsonValue.Create(1), ["skip"] = JsonValue.Create(2) };
                var source = new JsonObject { ["keep"] = JsonValue.Create(10), ["skip"] = JsonValue.Create(20) };
                JsonMerger.Merge(target, source, new HashSet<string> { "skip" });
                return target["keep"]!.GetValue<int>() == 10
                    && target["skip"]!.GetValue<int>() == 2;
            });

            builder.Add("GetItemKey_ById_ShouldReturnId", () =>
            {
                var obj = JsonNode.Parse("""{"id":"myId","key":"myKey","name":"myName"}""")!.AsObject();
                return JsonMerger.GetItemKey(obj) == "myId";
            });

            builder.Add("GetItemKey_ByKey_ShouldReturnKey", () =>
            {
                var obj = JsonNode.Parse("""{"key":"myKey","name":"myName"}""")!.AsObject();
                return JsonMerger.GetItemKey(obj) == "myKey";
            });

            builder.Add("GetItemKey_ByName_ShouldReturnName", () =>
            {
                var obj = JsonNode.Parse("""{"name":"myName"}""")!.AsObject();
                return JsonMerger.GetItemKey(obj) == "myName";
            });

            builder.Add("GetItemKey_NoId_ShouldReturnNull", () =>
            {
                var obj = JsonNode.Parse("""{"val":1}""")!.AsObject();
                return JsonMerger.GetItemKey(obj) == null;
            });
        });

        Assert.IsTrue(result.AllPassed, $"JsonMerger tests failed: {string.Join("; ", result.Messages.Where(m => m.StartsWith("[FAIL]") || m.StartsWith("[ERROR]"))) }");
    }

    [TestMethod]
    public void RunAll_ReferencePrefix()
    {
        var result = RunTestGroup("ReferencePrefix", builder =>
        {
            builder.Add("ResolveName_WithPrefix_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveName("@style.myStyle", "@style.") == "myStyle";
            });

            builder.Add("ResolveName_WithoutPrefix_ShouldReturnOriginal", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveName("myStyle", "@style.") == "myStyle";
            });

            builder.Add("ResolveName_EmptyValue_ShouldReturnEmpty", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveName("", "@style.") == "";
            });

            builder.Add("ResolveStyleName_WithPrefix_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveStyleName("@style.myBtn") == "myBtn";
            });

            builder.Add("ResolveStyleName_WithoutPrefix_ShouldReturnOriginal", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveStyleName("myBtn") == "myBtn";
            });

            builder.Add("ResolveTemplateName_WithPrefix_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveTemplateName("@template.myTpl") == "myTpl";
            });

            builder.Add("ResolveTemplateName_WithoutPrefix_ShouldReturnOriginal", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveTemplateName("myTpl") == "myTpl";
            });

            builder.Add("ResolveExtendsName_WithPrefix_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveExtendsName("@extends.baseForm") == "baseForm";
            });

            builder.Add("ResolveExtendsName_WithoutPrefix_ShouldReturnOriginal", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveExtendsName("baseForm") == "baseForm";
            });

            builder.Add("ResolveDefaultName_WithPrefix_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveDefaultName("@default.myDefault") == "myDefault";
            });

            builder.Add("ResolveDefaultName_WithoutPrefix_ShouldReturnOriginal", () =>
            {
                return TemplateExpansionReferencePrefix.ResolveDefaultName("myDefault") == "myDefault";
            });

            builder.Add("IsStyleReference_True_ShouldReturnTrue", () =>
            {
                return TemplateExpansionReferencePrefix.IsStyleReference("@style.xxx");
            });

            builder.Add("IsStyleReference_False_ShouldReturnFalse", () =>
            {
                return !TemplateExpansionReferencePrefix.IsStyleReference("xxx");
            });

            builder.Add("IsTemplateReference_True_ShouldReturnTrue", () =>
            {
                return TemplateExpansionReferencePrefix.IsTemplateReference("@template.xxx");
            });

            builder.Add("IsExtendsReference_True_ShouldReturnTrue", () =>
            {
                return TemplateExpansionReferencePrefix.IsExtendsReference("@extends.xxx");
            });

            builder.Add("IsDefaultReference_True_ShouldReturnTrue", () =>
            {
                return TemplateExpansionReferencePrefix.IsDefaultReference("@default.xxx");
            });

            builder.Add("ExtractStyleName_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ExtractStyleName("@style.myBtn") == "myBtn";
            });

            builder.Add("ExtractTemplateName_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ExtractTemplateName("@template.myTpl") == "myTpl";
            });

            builder.Add("ExtractExtendsName_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ExtractExtendsName("@extends.baseForm") == "baseForm";
            });

            builder.Add("ExtractDefaultName_ShouldExtract", () =>
            {
                return TemplateExpansionReferencePrefix.ExtractDefaultName("@default.myDefault") == "myDefault";
            });
        });

        Assert.IsTrue(result.AllPassed, $"ReferencePrefix tests failed: {string.Join("; ", result.Messages.Where(m => m.StartsWith("[FAIL]") || m.StartsWith("[ERROR]"))) }");
    }

    [TestMethod]
    public void RunAll_PipelineHelper()
    {
        var result = RunTestGroup("PipelineHelper", builder =>
        {
            builder.Add("ChildControlKeys_ShouldContainExpectedKeys", () =>
            {
                return JsonPipelineHelper.ChildControlKeys.Length == 3
                    && JsonPipelineHelper.ChildControlKeys.Contains("controls")
                    && JsonPipelineHelper.ChildControlKeys.Contains("panel1Controls")
                    && JsonPipelineHelper.ChildControlKeys.Contains("panel2Controls");
            });

            builder.Add("TraverseTopLevel_ViewsControls_ShouldTraverse", () =>
            {
                var json = JsonNode.Parse("""
                {
                    "views": [
                        { "controls": [ { "id": "c1" }, { "id": "c2" } ] },
                        { "controls": [ { "id": "c3" } ] }
                    ]
                }
                """)!.AsObject();

                var visited = new List<string>();
                JsonPipelineHelper.TraverseTopLevel(
                    json,
                    processControl: controls =>
                    {
                        foreach (var c in controls)
                            visited.Add(c!.AsObject()["id"]!.GetValue<string>());
                        return false;
                    });

                return visited.Count == 3
                    && visited[0] == "c1"
                    && visited[1] == "c2"
                    && visited[2] == "c3";
            });

            builder.Add("TraverseTopLevel_Steps_ShouldTraverse", () =>
            {
                var json = JsonNode.Parse("""
                {
                    "steps": [ { "name": "s1" }, { "name": "s2" } ]
                }
                """)!.AsObject();

                var visited = new List<string>();
                JsonPipelineHelper.TraverseTopLevel(
                    json,
                    processItem: items =>
                    {
                        foreach (var item in items)
                            visited.Add(item!.AsObject()["name"]!.GetValue<string>());
                        return false;
                    });

                return visited.Count == 2 && visited[0] == "s1" && visited[1] == "s2";
            });

            builder.Add("TraverseTopLevel_Commands_ShouldTraverse", () =>
            {
                var json = JsonNode.Parse("""
                {
                    "commands": [ { "action": "a1" }, { "action": "a2" } ]
                }
                """)!.AsObject();

                var visited = new List<string>();
                JsonPipelineHelper.TraverseTopLevel(
                    json,
                    processItem: items =>
                    {
                        foreach (var item in items)
                            visited.Add(item!.AsObject()["action"]!.GetValue<string>());
                        return false;
                    });

                return visited.Count == 2 && visited[0] == "a1" && visited[1] == "a2";
            });

            builder.Add("TraverseTopLevel_NoViewsStepsCommands_ShouldNotThrow", () =>
            {
                var json = JsonNode.Parse("""{"config":{"key":"val"}}""")!.AsObject();
                var modified = JsonPipelineHelper.TraverseTopLevel(json);
                return !modified;
            });

            builder.Add("TraverseControls_ShouldRecurseChildren", () =>
            {
                var controls = JsonNode.Parse("""
                [
                    { "id": "parent", "controls": [ { "id": "child1" }, { "id": "child2", "controls": [ { "id": "grandchild" } ] } ] }
                ]
                """)!.AsArray();

                var visited = new List<string>();
                JsonPipelineHelper.TraverseControls(controls, ctrl =>
                {
                    visited.Add(ctrl["id"]!.GetValue<string>());
                    return false;
                });

                return visited.Count == 4
                    && visited.Contains("parent")
                    && visited.Contains("child1")
                    && visited.Contains("child2")
                    && visited.Contains("grandchild");
            });

            builder.Add("TraverseControls_Empty_ShouldNotThrow", () =>
            {
                var controls = new JsonArray();
                var modified = JsonPipelineHelper.TraverseControls(controls, _ => false);
                return !modified;
            });
        });

        Assert.IsTrue(result.AllPassed, $"PipelineHelper tests failed: {string.Join("; ", result.Messages.Where(m => m.StartsWith("[FAIL]") || m.StartsWith("[ERROR]"))) }");
    }

    [TestMethod]
    public void RunAll_PipelineIntegration()
    {
        var result = RunTestGroup("PipelineIntegration", builder =>
        {
            builder.Add("Execute_EmptyPipeline_ShouldReturnInput", () =>
            {
                var pipeline = new JsonPipelineBuilder("test").Build();
                var pipelineResult = pipeline.Execute("""{"key":"value"}""");
                return pipelineResult.IsSuccess
                    && pipelineResult.Data is string data
                    && data.Contains("\"key\"")
                    && data.Contains("\"value\"");
            });

            builder.Add("Execute_NullInput_ShouldFail", () =>
            {
                var pipeline = new JsonPipelineBuilder("test").Build();
                var pipelineResult = pipeline.Execute("");
                return !pipelineResult.IsSuccess;
            });

            builder.Add("Execute_InvalidJson_ShouldFail", () =>
            {
                var pipeline = new JsonPipelineBuilder("test").Build();
                var pipelineResult = pipeline.Execute("{invalid json}");
                return !pipelineResult.IsSuccess;
            });

            builder.Add("Execute_ExtendsOnly_ShouldExpandExtends", () =>
            {
                var storage = new MockTemplateStorage();
                var pipeline = new JsonPipelineBuilder("extends")
                    .UseExtends(storage)
                    .Build();

                var input = """{"extends":"baseForm","extra":"value"}""";
                var pipelineResult = pipeline.Execute(input);

                if (!pipelineResult.IsSuccess || pipelineResult.Data is not string output)
                    return false;

                if (output.Contains("\"extends\""))
                    return false;

                if (!output.Contains("\"method\"") || !output.Contains("\"post\""))
                    return false;

                if (!output.Contains("\"extra\"") || !output.Contains("\"value\""))
                    return false;

                return true;
            });

            builder.Add("Execute_StyleOnly_ShouldExpandStyle", () =>
            {
                var storage = new MockTemplateStorage();
                var pipeline = new JsonPipelineBuilder("style")
                    .UseStyleApply(storage)
                    .Build();

                var input = """{"type":"button","style":"primaryBtn","text":"Click"}""";
                var pipelineResult = pipeline.Execute(input);

                if (!pipelineResult.IsSuccess || pipelineResult.Data is not string output)
                    return false;

                if (output.Contains("\"style\""))
                    return false;

                if (!output.Contains("\"color\"") || !output.Contains("\"#fff\""))
                    return false;

                if (!output.Contains("\"text\"") || !output.Contains("\"Click\""))
                    return false;

                return true;
            });

            builder.Add("Execute_TemplateOnly_ShouldExpandTemplate", () =>
            {
                var storage = new MockTemplateStorage();
                var pipeline = new JsonPipelineBuilder("template")
                    .UseTemplateExpand(storage)
                    .Build();

                var input = """
                {
                    "views": [
                        {
                            "controls": [
                                {
                                    "type": "button",
                                    "template": "labeledInput",
                                    "label": "Name",
                                    "placeholder": "Enter your name"
                                }
                            ]
                        }
                    ]
                }
                """;

                var pipelineResult = pipeline.Execute(input);

                if (!pipelineResult.IsSuccess || pipelineResult.Data is not string output)
                    return false;

                if (output.Contains("\"template\""))
                    return false;

                if (!output.Contains("\"__multi_control\""))
                    return false;

                if (!output.Contains("\"Name\"") || output.Contains("${label}"))
                    return false;

                return true;
            });


            builder.Add("Execute_FullPipeline_ShouldProcessCorrectly", () =>
            {
                var storage = new MockTemplateStorage();

                var pipeline = new JsonPipelineBuilder("full")
                    .UseExtends(storage)
                    .UseStyleApply(storage)
                    .UseTemplateExpand(storage)
                    .UseDefaultsApply(storage)
                    .UseConfigMerge()
                    .UseValidation(false)
                    .Build();

                var input = """
                {
                    "extends": "baseForm",
                    "views": [
                        {
                            "type": "form",
                            "controls": [
                                {
                                    "type": "button",
                                    "style": "primaryBtn",
                                    "template": "labeledInput",
                                    "label": "Name",
                                    "placeholder": "Enter name",
                                    "text": "Submit"
                                }
                            ]
                        }
                    ]
                }
                """;

                var pipelineResult = pipeline.Execute(input);

                if (!pipelineResult.IsSuccess || pipelineResult.Data is not string output)
                    return false;

                if (output.Contains("\"extends\""))
                    return false;

                if (output.Contains("\"style\""))
                    return false;

                if (output.Contains("\"template\""))
                    return false;

                if (!output.Contains("\"Submit\""))
                    return false;

                return true;
            });

            builder.Add("TemplateDefaultsStyle_ShouldBeExpanded", () =>
            {
                // 回归: 模板 defaults 中的 style 字符串引用（如 composites.json toolbar-button → primary-button）
                // 在 TemplateExpand 阶段注入控件后，必须由 StyleApply 阶段展开并移除，不得残留字符串
                // （否则 JsonSerializer.Deserialize<UIConfiguration> 对 ControlConfig.Style(StyleConfig?) 转换失败）。
                var storage = new MockTemplateStorage();
                storage.RegisterStyle("primary-button", JsonNode.Parse("""
                {
                    "description": "主按钮样式",
                    "applyTo": ["button"],
                    "properties": {
                        "backColor": "#2563EB",
                        "foreColor": "White"
                    }
                }
                """)!.AsObject());

                storage.RegisterComposite("toolbar-button", JsonNode.Parse("""
                {
                    "type": "button",
                    "defaults": {
                        "style": "primary-button",
                        "font": { "size": 9 }
                    },
                    "parameters": {
                        "name": { "required": true },
                        "text": { "required": true },
                        "functionName": { "required": true }
                    }
                }
                """)!.AsObject());

                var pipeline = new JsonPipelineBuilder("regression-style")
                    .UseExtends(storage)
                    .UseStyleApply(storage)
                    .UseTemplateExpand(storage)
                    .UseDefaultsApply(storage)
                    .UseParameterResolve()
                    .UseValidation(false)
                    .Build();

                var input = """
                {
                    "views": [
                        {
                            "controls": [
                                {
                                    "template": "toolbar-button",
                                    "name": "loadConfigButton",
                                    "text": "Load Config",
                                    "functionName": "LoadConfig"
                                }
                            ]
                        }
                    ]
                }
                """;

                var pipelineResult = pipeline.Execute(input);

                if (!pipelineResult.IsSuccess || pipelineResult.Data is not string output)
                    return false;

                // 模板展开注入的 style 字符串引用必须被展开并移除
                if (output.Contains("\"style\""))
                    return false;

                // 样式属性必须合并进控件
                if (!output.Contains("\"backColor\"") || !output.Contains("#2563EB"))
                    return false;

                // 控件本体字段保留
                if (!output.Contains("\"loadConfigButton\"") || !output.Contains("\"Load Config\""))
                    return false;

                return true;
            });

            builder.Add("Pipeline_ConverterOrder_ShouldBeCorrect", () =>
            {
                var storage = new MockTemplateStorage();
                var pipeline = new JsonPipelineBuilder("order")
                    .UseExtends(storage)
                    .UseStyleApply(storage)
                    .UseTemplateExpand(storage)
                    .UseDefaultsApply(storage)
                    .UseParameterResolve()
                    .UseConfigMerge()
                    .UseValidation()
                    .Build();

                var converters = pipeline.Converters;
                return converters.Count == 7
                    && converters[0].Order == 50
                    && converters[1].Order == 200
                    && converters[2].Order == 250
                    && converters[3].Order == 300
                    && converters[4].Order == 400
                    && converters[5].Order == 500
                    && converters[6].Order == 900;
            });

            builder.Add("Validation_StrictMode_ShouldRejectInvalidContent", () =>
            {
                var pipeline = new JsonPipelineBuilder("validate")
                    .UseValidation(true)
                    .Build();

                var result = pipeline.Execute("""{"irrelevant":true}""");
                return !result.IsSuccess;
            });


            builder.Add("Converter_Disabled_ShouldBeSkipped", () =>
            {
                var storage = new MockTemplateStorage();
                var pipeline = new JsonPipelineBuilder("disable")
                    .UseStyleApply(storage)
                    .Build();

                foreach (var c in pipeline.Converters)
                {
                    if (c is JsonConverterBase converterBase)
                        converterBase.Enabled = false;
                }

                var input = """{"style":"primaryBtn"}""";
                var result = pipeline.Execute(input);

                return result.IsSuccess && result.Data is string data && data.Contains("\"style\"");
            });
        });

        Assert.IsTrue(result.AllPassed, $"PipelineIntegration tests failed: {string.Join("; ", result.Messages.Where(m => m.StartsWith("[FAIL]") || m.StartsWith("[ERROR]"))) }");
    }

    [TestMethod]
    public void RunAll_BuildEventsFromTemplate()
    {
        var result = RunTestGroup("BuildEventsFromTemplate", builder =>
        {
            builder.Add("TemplateButton_SynthesizesEventsBindings", () =>
            {
                var control = JsonNode.Parse("""
                {
                    "type": "button",
                    "name": "loadDocButton",
                    "functionName": "AgentUi_LoadTemplateDoc",
                    "parameters": [ { "target": "templateName", "source": "jsonTemplateComboBox", "sourceType": "control" } ],
                    "output": "templateDocTextBox"
                }
                """)!.AsObject();

                var changed = TemplateExpandConverter.BuildEventsFromTemplate(control);

                return changed
                    && control["events"] is JsonObject events
                    && events["click"]!.GetValue<string>() == "AgentUi_LoadTemplateDoc"
                    && events["bindings"] is JsonObject bindings
                    && bindings["output"]!.GetValue<string>() == "templateDocTextBox"
                    && bindings["parameters"] is JsonArray parameters
                    && parameters.Count == 1
                    && parameters[0]!["target"]!.GetValue<string>() == "templateName"
                    && !control.ContainsKey("functionName")
                    && !control.ContainsKey("parameters")
                    && !control.ContainsKey("output")
                    && !control.ContainsKey("buildEvents");
            });

            builder.Add("ExplicitEvents_ShouldBePreserved", () =>
            {
                var control = JsonNode.Parse("""
                {
                    "type": "button",
                    "name": "explicitButton",
                    "functionName": "SomeFunc",
                    "events": { "click": "ExplicitFunc", "bindings": { "output": "outBox" } }
                }
                """)!.AsObject();

                var changed = TemplateExpandConverter.BuildEventsFromTemplate(control);

                return !changed
                    && control["events"]!["click"]!.GetValue<string>() == "ExplicitFunc"
                    && control.ContainsKey("functionName");
            });

            builder.Add("FunctionNameWithoutParams_SynthesizesClickOnly", () =>
            {
                var control = JsonNode.Parse("""
                {
                    "type": "button",
                    "name": "statusButton",
                    "functionName": "AgentUi_GetStatus"
                }
                """)!.AsObject();

                var changed = TemplateExpandConverter.BuildEventsFromTemplate(control);

                return changed
                    && control["events"] is JsonObject events
                    && events["click"]!.GetValue<string>() == "AgentUi_GetStatus"
                    && !events.ContainsKey("bindings");
            });

            builder.Add("NoFunctionName_ShouldNotSynthesize", () =>
            {
                var control = JsonNode.Parse("""
                { "type": "label", "name": "titleLabel", "text": "hello" }
                """)!.AsObject();

                var changed = TemplateExpandConverter.BuildEventsFromTemplate(control);

                return !changed && !control.ContainsKey("events");
            });
        });

        Assert.IsTrue(result.AllPassed, $"BuildEventsFromTemplate tests failed: {string.Join("; ", result.Messages.Where(m => m.StartsWith("[FAIL]") || m.StartsWith("[ERROR]"))) }");
    }

    private class MockTemplateStorage : ITemplateStorage
    {
        private readonly Dictionary<string, JsonObject> _styles = new();
        private readonly Dictionary<string, JsonObject> _composites = new();

        public bool IsInitialized => true;
        public string? StylesPath => null;
        public string? CompositesPath => null;

        public MockTemplateStorage()
        {
            _styles["primaryBtn"] = JsonNode.Parse("""
            {
                "description": "Primary button style",
                "properties": {
                    "color": "#fff",
                    "bgColor": "#007bff",
                    "padding": "8px 16px"
                }
            }
            """)!.AsObject();

            _composites["labeledInput"] = JsonNode.Parse("""
            {
                "type": "labeled-input",
                "controls": [
                    { "type": "label", "text": "${label}" },
                    { "type": "input", "placeholder": "${placeholder}" }
                ],
                "parameters": {
                    "label": { "required": true },
                    "placeholder": { "default": "Enter value" }
                }
            }
            """)!.AsObject();

            _composites["baseForm"] = JsonNode.Parse("""
            {
                "type": "form",
                "config": {
                    "method": "post",
                    "action": "/submit"
                }
            }
            """)!.AsObject();

            _composites["_defaults"] = JsonNode.Parse("""
            {
                "width": "100%",
                "height": "auto",
                "visible": true
            }
            """)!.AsObject();

            _composites["_button_defaults"] = JsonNode.Parse("""
            {
                "cursor": "pointer",
                "border": "1px solid #ccc"
            }
            """)!.AsObject();
        }

        public void Initialize(string stylesPath, string compositesPath) { }
        public void Initialize(IEnumerable<(string stylesPath, string compositesPath)> paths) { }

        public JsonObject? GetStyle(string name) =>
            _styles.TryGetValue(name, out var style) ? style : null;

        public JsonObject? GetComposite(string name) =>
            _composites.TryGetValue(name, out var composite) ? composite : null;

        public bool HasStyle(string name) => _styles.ContainsKey(name);
        public bool HasComposite(string name) => _composites.ContainsKey(name);

        public IEnumerable<string> GetStyleNames() => _styles.Keys;
        public IEnumerable<string> GetCompositeNames() => _composites.Keys;

        public TemplateMetadata? GetMetadata(string name) => null;
        public IEnumerable<string> SearchTemplates(string keyword) => Enumerable.Empty<string>();
        public void RefreshIfNeeded() { }

        public void RegisterComposite(string name, JsonObject composite) => _composites[name] = composite;
        public void RegisterStyle(string name, JsonObject style) => _styles[name] = style;
        public void UnregisterComposite(string name) => _composites.Remove(name);
        public void UnregisterStyle(string name) => _styles.Remove(name);

        public void RegisterComposites(Dictionary<string, JsonObject> definitions)
        {
            foreach (var (key, value) in definitions) _composites[key] = value;
        }
        public void RegisterStyles(Dictionary<string, JsonObject> definitions)
        {
            foreach (var (key, value) in definitions) _styles[key] = value;
        }
        public void UnregisterComposites(IEnumerable<string> names)
        {
            foreach (var name in names) _composites.Remove(name);
        }
        public void UnregisterStyles(IEnumerable<string> names)
        {
            foreach (var name in names) _styles.Remove(name);
        }
        public void ClearComposites() => _composites.Clear();
        public void ClearStyles() => _styles.Clear();
    }
}
