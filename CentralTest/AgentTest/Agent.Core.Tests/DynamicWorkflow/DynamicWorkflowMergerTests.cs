
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core;
using Agent.State;      // AgentLoopFunction

namespace Agent.Core.Tests
{

    [TestClass]
    [TestCategory("DynamicWorkflowMerger")]
    public class DynamicWorkflowMergerTests
    {
        private DynamicWorkflowMerger _merger = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _merger = new DynamicWorkflowMerger();
        }

        [TestMethod]
        public void ParseFragment_标准workflow_fragment格式_应成功解析()
        {
            var json = @"
            {
                ""workflow_fragment"": {
                    ""nodes"": [
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""step_1"",
                            ""FuncName"": ""mcp_call_tool"",
                            ""ParameterValues"": {
                                ""server"": ""filesystem"",
                                ""tool"": ""read_file"",
                                ""arguments"": { ""path"": ""/workspace/main.py"" }
                            },
                            ""ResultKeys"": [""file_content""]
                        }
                    ],
                    ""KeyMapping"": {
                        ""file_content"": ""analysis_input""
                    }
                }
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment, "应返回非 null 的 WorkflowFragment 实例");
            Assert.AreEqual(1, fragment!.Nodes.Count, "Nodes.Count 应为 1");
            Assert.AreEqual("step_1", fragment.Nodes[0].NodeId, "NodeId 应为 step_1");
            Assert.AreEqual("mcp_call_tool", fragment.Nodes[0].FuncName, "FuncName 应为 mcp_call_tool");
            Assert.IsTrue(fragment.KeyMapping.ContainsKey("file_content"), "KeyMapping 应包含 file_content");
            Assert.AreEqual("analysis_input", fragment.KeyMapping["file_content"], "KeyMapping 值应为 analysis_input");
        }

        [TestMethod]
        public void ParseFragment_直接nodes数组格式_应成功解析()
        {
            var json = @"
            {
                ""nodes"": [
                    {
                        ""Symbol"": ""process"",
                        ""NodeId"": ""step_1"",
                        ""FuncName"": ""sk_chat"",
                        ""ParameterValues"": { ""input"": ""分析代码"" },
                        ""ResultKeys"": [""analysis""]
                    }
                ]
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment, "应返回非 null 的 WorkflowFragment 实例");
            Assert.AreEqual(1, fragment!.Nodes.Count, "Nodes.Count 应为 1");
            Assert.AreEqual("sk_chat", fragment.Nodes[0].FuncName, "FuncName 应为 sk_chat");
        }

        [TestMethod]
        public void ParseFragment_无效JSON_应返回null()
        {
            var json = @"{ ""invalid"": ""data"" }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNull(fragment, "无效 JSON 应返回 null");
        }

        [TestMethod]
        public void BuildWorkflowJson_单节点_应生成有效JSON()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step_1",
                        FuncName = "mcp_call_tool",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["server"] = "filesystem",
                            ["tool"] = "read_file",
                            ["arguments"] = new Dictionary<string, object> { ["path"] = "test.txt" }
                        },
                        ResultKeys = new List<string> { "content" }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"step_1\""),
                $"JSON 应包含 NodeId: step_1\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"FuncName\":\"mcp_call_tool\""),
                $"JSON 应包含 FuncName: mcp_call_tool\n实际 JSON: {json}");
        }

        [TestMethod]
        public void BuildWorkflowJson_多节点链式_应生成有效JSON()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "read",
                        FuncName = "mcp_call_tool",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["server"] = "filesystem",
                            ["tool"] = "read_file",
                            ["arguments"] = new Dictionary<string, object> { ["path"] = "test.txt" }
                        },
                        ResultKeys = new List<string> { "content" }
                    },
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "analyze",
                        FuncName = "sk_chat",
                        ParameterValues = new Dictionary<string, object> { ["input"] = "分析内容" },
                        ResultKeys = new List<string> { "analysis" }
                    }
                },
                KeyMapping = new Dictionary<string, string> { ["content"] = "analysis_input" }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            
            using var doc = JsonDocument.Parse(json);
            
            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"read\""),
                $"JSON 应包含 NodeId: read\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"analyze\""),
                $"JSON 应包含 NodeId: analyze\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("KeyMapping") || jsonString.Contains("key_mapping"),
                $"JSON 应包含 KeyMapping 信息\n实际 JSON: {json}");
        }

        [TestMethod]
        public async Task MergeAndExecuteAsync_无效JSON_应返回error状态()
        {
            var invalidJson = "这不是有效的 JSON";

            var context = new WorkFlowMethod.Orchestration.Context.Models.WorkflowContext(
                parameters: new Dictionary<string, object>(),
                data: new Dictionary<string, object>(),
                executionId: "test_error_001",
                nodeKey: "test_node",
                workflowId: "test_workflow"
            );

            var result = await _merger.MergeAndExecuteAsync(invalidJson, context);

            Assert.IsNotNull(result, "结果不应为 null");
            var resultJson = JsonSerializer.Serialize(result);
            Assert.IsTrue(resultJson.Contains("\"status\":\"error\""),
                $"无效 JSON 应返回 error 状态\n实际结果: {resultJson}");
        }

        [TestMethod]
        public async Task MergeAndExecuteAsync_空节点列表_应返回error状态()
        {
            var json = @"{ ""workflow_fragment"": { ""nodes"": [] } }";

            var context = new WorkFlowMethod.Orchestration.Context.Models.WorkflowContext(
                parameters: new Dictionary<string, object>(),
                data: new Dictionary<string, object>(),
                executionId: "test_empty_001",
                nodeKey: "test_node",
                workflowId: "test_workflow"
            );

            var result = await _merger.MergeAndExecuteAsync(json, context);

            Assert.IsNotNull(result, "结果不应为 null");
            var resultJson = JsonSerializer.Serialize(result);
            Assert.IsTrue(resultJson.Contains("\"status\":\"error\""),
                $"空节点列表应返回 error 状态\n实际结果: {resultJson}");
        }

        [TestMethod]
        public void ParseFragment_嵌套fragment字段_应成功解析()
        {
            var json = @"
            {
                ""fragment"": {
                    ""nodes"": [
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""step_frag"",
                            ""FuncName"": ""mcp_call_tool"",
                            ""ParameterValues"": {
                                ""server"": ""filesystem"",
                                ""tool"": ""list_files""
                            },
                            ""ResultKeys"": [""files""]
                        }
                    ],
                    ""KeyMapping"": {
                        ""files"": ""file_list""
                    }
                }
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment, "应返回非 null 的 WorkflowFragment 实例");
            Assert.AreEqual(1, fragment!.Nodes.Count, "Nodes.Count 应为 1");
            Assert.AreEqual("step_frag", fragment.Nodes[0].NodeId, "NodeId 应为 step_frag");
            Assert.AreEqual("mcp_call_tool", fragment.Nodes[0].FuncName, "FuncName 应为 mcp_call_tool");
            Assert.IsTrue(fragment.KeyMapping.ContainsKey("files"), "KeyMapping 应包含 files");
            Assert.AreEqual("file_list", fragment.KeyMapping["files"], "KeyMapping 值应为 file_list");
        }

        [TestMethod]
        public void BuildWorkflowJson_含NextNodeConfig_应包含配置()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step_with_next",
                        FuncName = "mcp_call_tool",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["server"] = "filesystem",
                            ["tool"] = "read_file"
                        },
                        ResultKeys = new List<string> { "content" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["condition"] = "content != null",
                            ["next_node_id"] = "step_after"
                        }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            
            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("NextNodeConfig") || jsonString.Contains("next_node_config"),
                $"JSON 应包含 NextNodeConfig\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("condition"),
                $"JSON 应包含 condition 字段\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("next_node_id"),
                $"JSON 应包含 next_node_id 字段\n实际 JSON: {json}");
        }

        [TestMethod]
        public void BuildWorkflowJson_含ParameterKeys_应包含配置()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step_with_keys",
                        FuncName = "sk_chat",
                        ParameterKeys = new List<string> { "input", "context" },
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["input"] = "分析数据",
                            ["context"] = "来自上一步"
                        },
                        ResultKeys = new List<string> { "analysis" }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            
            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("ParameterKeys") || jsonString.Contains("parameter_keys"),
                $"JSON 应包含 ParameterKeys\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"input\""),
                $"JSON 应包含 input 参数键\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"context\""),
                $"JSON 应包含 context 参数键\n实际 JSON: {json}");
        }
    }


}

