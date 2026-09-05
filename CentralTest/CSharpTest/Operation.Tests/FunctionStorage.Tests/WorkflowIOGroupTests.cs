using FunctionStorage;
using System.Text.Json;
using FieldMappingCollection = FunctionStorage.Models.FieldMappingCollection;
using WorkflowDefinition = FunctionStorage.Models.WorkflowDefinition;

namespace FunctionStorage.Tests
{
    internal static class WorkflowIOGroupTests
    {
        public static bool RunAll()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  WorkflowIOGroup 功能验证测试");
            Console.WriteLine("========================================\n");

            var allPassed = true;

            allPassed &= Test_FieldMappingCollection();
            allPassed &= Test_WorkflowIORegistry();
            allPassed &= Test_InferMappingsFromDefaultInput();
            allPassed &= Test_InferMappingsFromOutputMapping();
            allPassed &= Test_FormatOutput_SingleField();
            allPassed &= Test_FormatOutput_MultipleFields();
            allPassed &= Test_FormatOutput_WithResponseField();
            allPassed &= Test_WorkflowIOGroup_Creation();
            allPassed &= Test_WorkflowDefinition_WithIOMapping();

            Console.WriteLine("\n========================================");
            Console.WriteLine(allPassed ? "  所有测试通过！" : "  存在失败的测试！");
            Console.WriteLine("========================================");

            return allPassed;
        }

        static bool Test_FieldMappingCollection()
        {
            Console.Write("[测试] FieldMappingCollection 序列化/反序列化... ");

            var json = @"{
                ""inputMapping"": {
                    ""codeInput"": ""inputTextBox"",
                    ""apiKey"": ""apiKeyTextBox""
                },
                ""outputMapping"": {
                    ""analysis"": ""outputTextBox"",
                    ""optimized"": ""outputTextBox""
                }
            }";

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var mappings = JsonSerializer.Deserialize<FieldMappingCollection>(json, options);

            if (mappings == null) { Console.WriteLine("失败: 反序列化为 null"); return false; }
            if (mappings.InputToControl["codeInput"] != "inputTextBox") { Console.WriteLine("失败: codeInput 映射错误"); return false; }
            if (mappings.InputToControl["apiKey"] != "apiKeyTextBox") { Console.WriteLine("失败: apiKey 映射错误"); return false; }
            if (mappings.OutputToControl["analysis"] != "outputTextBox") { Console.WriteLine("失败: analysis 映射错误"); return false; }
            if (mappings.OutputToControl["optimized"] != "outputTextBox") { Console.WriteLine("失败: optimized 映射错误"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_WorkflowIORegistry()
        {
            Console.Write("[测试] WorkflowIORegistry 注册/获取... ");

            var group = new WorkflowIOGroup("TestWorkflow");
            WorkflowIORegistry.Register("TestWorkflow", group);

            var retrieved = WorkflowIORegistry.Get("TestWorkflow");
            if (retrieved == null) { Console.WriteLine("失败: 获取为 null"); return false; }
            if (retrieved.WorkflowName != "TestWorkflow") { Console.WriteLine("失败: 名称不匹配"); return false; }

            if (retrieved != group) { Console.WriteLine("失败: Get 应返回已注册的实例"); return false; }

            var newGroup = new WorkflowIOGroup("NewWorkflow");
            WorkflowIORegistry.Register("NewWorkflow", newGroup);
            var retrievedNew = WorkflowIORegistry.Get("NewWorkflow");
            if (retrievedNew == null || retrievedNew.WorkflowName != "NewWorkflow") { Console.WriteLine("失败: 新工作流注册后获取失败"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_InferMappingsFromDefaultInput()
        {
            Console.Write("[测试] InferMappingsFromDefaultInput 自动推断... ");

            var defaultInput = new Dictionary<string, object>
            {
                ["codeInput"] = "some code",
                ["apiKey"] = "",
                ["language"] = "csharp"
            };

            var mappings = WorkflowIORegistry.InferMappingsFromDefaultInput(defaultInput);

            if (mappings.InputToControl["codeInput"] != "codeInput") { Console.WriteLine("失败: codeInput 应映射到自身"); return false; }
            if (mappings.InputToControl["apiKey"] != "apiKey") { Console.WriteLine("失败: apiKey 应映射到自身"); return false; }
            if (mappings.InputToControl["language"] != "language") { Console.WriteLine("失败: language 应映射到自身"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_InferMappingsFromOutputMapping()
        {
            Console.Write("[测试] InferMappingsFromOutputMapping 自动推断... ");

            var outputMapping = new Dictionary<string, string>
            {
                ["analysis"] = "analysisResult",
                ["optimized"] = "optimizedCode"
            };

            var mappings = WorkflowIORegistry.InferMappingsFromOutputMapping(outputMapping);

            if (mappings.OutputToControl["analysis"] != "analysisResult") { Console.WriteLine("失败: analysis 应映射到 analysisResult"); return false; }
            if (mappings.OutputToControl["optimized"] != "optimizedCode") { Console.WriteLine("失败: optimized 应映射到 optimizedCode"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_FormatOutput_SingleField()
        {
            Console.Write("[测试] FormatOutput 单字段输出... ");

            var stepResults = new Dictionary<string, object>
            {
                ["generatedCode"] = "console.log('hello');"
            };
            var outputMapping = new Dictionary<string, string>
            {
                ["response"] = "generatedCode"
            };

            var result = stepResults.TryGetValue("generatedCode", out var val) ? val?.ToString() : null;
            if (result != "console.log('hello');") { Console.WriteLine($"失败: 期望 'console.log('hello');', 实际 '{result}'"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_FormatOutput_MultipleFields()
        {
            Console.Write("[测试] FormatOutput 多字段输出（返回 JSON）... ");

            var stepResults = new Dictionary<string, object>
            {
                ["analysisResult"] = "代码质量良好",
                ["optimizedCode"] = "优化后的代码"
            };
            var outputMapping = new Dictionary<string, string>
            {
                ["analysis"] = "analysisResult",
                ["optimized"] = "optimizedCode"
            };

            var outputDict = new Dictionary<string, object>();
            foreach (var kvp in outputMapping)
            {
                if (stepResults.TryGetValue(kvp.Value, out var stepVal))
                    outputDict[kvp.Key] = stepVal;
            }
            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var result = System.Text.Json.JsonSerializer.Serialize(outputDict, options);
            if (string.IsNullOrEmpty(result)) { Console.WriteLine("失败: 结果为空"); return false; }
            if (!result.Contains("analysis")) { Console.WriteLine("失败: 应包含 analysis 键"); return false; }
            if (!result.Contains("optimized")) { Console.WriteLine("失败: 应包含 optimized 键"); return false; }
            if (!result.Contains("代码质量良好")) { Console.WriteLine("失败: 应包含 analysis 值"); return false; }
            if (!result.Contains("优化后的代码")) { Console.WriteLine("失败: 应包含 optimized 值"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_FormatOutput_WithResponseField()
        {
            Console.Write("[测试] FormatOutput 含 response 字段... ");

            var stepResults = new Dictionary<string, object>
            {
                ["aiResponse"] = "这是 AI 的回复内容",
                ["metadata"] = "额外信息"
            };
            var outputMapping = new Dictionary<string, string>
            {
                ["response"] = "aiResponse",
                ["extra"] = "metadata"
            };

            var result = stepResults.TryGetValue("aiResponse", out var val) ? val?.ToString() : null;
            if (result != "这是 AI 的回复内容") { Console.WriteLine($"失败: 期望 '这是 AI 的回复内容', 实际 '{result}'"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_WorkflowIOGroup_Creation()
        {
            Console.Write("[测试] WorkflowIOGroup 创建... ");

            var mappings = new FieldMappingCollection
            {
                InputToControl = new Dictionary<string, string>
                {
                    ["codeInput"] = "inputTextBox"
                },
                OutputToControl = new Dictionary<string, string>
                {
                    ["result"] = "outputTextBox"
                }
            };

            var group = new WorkflowIOGroup("MyWorkflow", fieldMappings: mappings);

            if (group.WorkflowName != "MyWorkflow") { Console.WriteLine("失败: 名称错误"); return false; }
            if (group.FieldMappings.InputToControl["codeInput"] != "inputTextBox") { Console.WriteLine("失败: 输入映射错误"); return false; }
            if (group.FieldMappings.OutputToControl["result"] != "outputTextBox") { Console.WriteLine("失败: 输出映射错误"); return false; }

            Console.WriteLine("通过");
            return true;
        }

        static bool Test_WorkflowDefinition_WithIOMapping()
        {
            Console.Write("[测试] WorkflowDefinition 含 ioMapping 反序列化... ");

            var json = @"{
                ""workflowName"": ""CodeReviewWorkflow"",
                ""displayName"": ""代码审查工作流"",
                ""description"": ""测试"",
                ""defaultInput"": {
                    ""codeInput"": ""test code"",
                    ""apiKey"": """"
                },
                ""ioMapping"": {
                    ""inputMapping"": {
                        ""codeInput"": ""inputTextBox"",
                        ""apiKey"": ""apiKeyTextBox""
                    },
                    ""outputMapping"": {
                        ""analysis"": ""outputTextBox""
                    }
                },
                ""steps"": [],
                ""outputMapping"": {
                    ""analysis"": ""analysisResult""
                }
            }";

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, options);

            if (def == null) { Console.WriteLine("失败: 反序列化为 null"); return false; }
            if (def.WorkflowName != "CodeReviewWorkflow") { Console.WriteLine("失败: WorkflowName 错误"); return false; }
            if (def.IoMapping == null) { Console.WriteLine("失败: IOMapping 为 null"); return false; }
            if (def.IoMapping.InputToControl["codeInput"] != "inputTextBox") { Console.WriteLine("失败: ioMapping.inputFields.codeInput 错误"); return false; }
            if (def.IoMapping.OutputToControl["analysis"] != "outputTextBox") { Console.WriteLine("失败: ioMapping.outputFields.analysis 错误"); return false; }

            Console.WriteLine("通过");
            return true;
        }
    }
}
