
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
    [TestCategory("DynamicWorkflowBuilder")]
    public class DynamicWorkflowBuilderTests
    {
        private DynamicWorkflowMerger _merger = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _merger = new DynamicWorkflowMerger();
        }
        [TestMethod]
        public void BuildWorkflow_节点列表_应生成有效JSON()
        {
            var merger = new DynamicWorkflowMerger();
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "start",
                        NodeId = "start",
                        FuncName = "agent_loop",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["user_input"] = "test"
                        },
                        ResultKeys = new List<string> { "result" }
                    },
                    new FragmentNode
                    {
                        Symbol = "end",
                        NodeId = "end",
                        FuncName = "complete",
                        ResultKeys = new List<string> { "final_result" }
                    }
                }
            };

            var json = merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");

            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"start\""),
                $"JSON 应包含 NodeId: start\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"end\""),
                $"JSON 应包含 NodeId: end\n实际 JSON: {json}");

            Assert.IsTrue(jsonString.Contains("\"Symbol\":\"start\""),
                $"JSON 应包含 Symbol: start\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"FuncName\":\"agent_loop\""),
                $"JSON 应包含 FuncName: agent_loop\n实际 JSON: {json}");
        }

        [TestMethod]
        public void MergeFragment_单节点合并_应生成包含该节点的工作流()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "single_step",
                        FuncName = "mcp_call_tool",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["server"] = "filesystem",
                            ["tool"] = "read_file"
                        },
                        ResultKeys = new List<string> { "output" }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"single_step\""),
                $"JSON 应包含 NodeId: single_step\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"FuncName\":\"mcp_call_tool\""),
                $"JSON 应包含 FuncName: mcp_call_tool\n实际 JSON: {json}");
        }


        [TestMethod]
        public void MergeFragment_多节点链式合并_应保持节点顺序()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "start",
                        NodeId = "step_1",
                        FuncName = "input_validate",
                        ResultKeys = new List<string> { "validated" }
                    },
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step_2",
                        FuncName = "data_transform",
                        ResultKeys = new List<string> { "transformed" }
                    },
                    new FragmentNode
                    {
                        Symbol = "end",
                        NodeId = "step_3",
                        FuncName = "output_save",
                        ResultKeys = new List<string> { "saved" }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");

            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"step_1\""),
                $"JSON 应包含 NodeId: step_1\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"step_2\""),
                $"JSON 应包含 NodeId: step_2\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"step_3\""),
                $"JSON 应包含 NodeId: step_3\n实际 JSON: {json}");

            var symbol1Pos = jsonString.IndexOf("\"Symbol\":\"start\"");
            var symbol2Pos = jsonString.IndexOf("\"Symbol\":\"process\"");
            var symbol3Pos = jsonString.IndexOf("\"Symbol\":\"end\"");
            Assert.IsTrue(symbol1Pos >= 0, "应包含 Symbol: start");
            Assert.IsTrue(symbol2Pos >= 0, "应包含 Symbol: process");
            Assert.IsTrue(symbol3Pos >= 0, "应包含 Symbol: end");
            Assert.IsTrue(symbol1Pos < symbol2Pos, "start 应在 process 之前");
            Assert.IsTrue(symbol2Pos < symbol3Pos, "process 应在 end 之前");
        }


        [TestMethod]
        public void MergeFragment_含KeyMapping合并_应保留映射关系()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "read_file",
                        FuncName = "mcp_call_tool",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["server"] = "filesystem",
                            ["tool"] = "read_file"
                        },
                        ResultKeys = new List<string> { "raw_content" }
                    }
                },
                KeyMapping = new Dictionary<string, string>
                {
                    ["raw_content"] = "processed_content",
                    ["file_path"] = "source_path"
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("KeyMapping") || jsonString.Contains("key_mapping"),
                $"JSON 应包含 KeyMapping\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("raw_content"),
                $"JSON 应包含 raw_content 映射键\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("processed_content"),
                $"JSON 应包含 processed_content 映射值\n实际 JSON: {json}");
        }


        [TestMethod]
        public void MergeFragment_含NextNodeConfig合并_应保留配置()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "conditional_step",
                        FuncName = "check_condition",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["input"] = "test_value"
                        },
                        ResultKeys = new List<string> { "check_result" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["on_success"] = "next_step",
                            ["on_failure"] = "error_handler",
                            ["retry_count"] = 3
                        }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("NextNodeConfig") || jsonString.Contains("next_node_config"),
                $"JSON 应包含 NextNodeConfig\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("on_success"),
                $"JSON 应包含 on_success\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("on_failure"),
                $"JSON 应包含 on_failure\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("retry_count"),
                $"JSON 应包含 retry_count\n实际 JSON: {json}");
        }

        [TestMethod]
        public void BuildWorkflowJson_循环节点_应生成有效JSON()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "loop",
                        NodeId = "main_loop",
                        FuncName = "agent_loop",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["user_input"] = "循环处理",
                            ["max_iterations"] = 5
                        },
                        ResultKeys = new List<string> { "loop_result" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["loop_condition"] = "result.status == 'in_progress'",
                            ["max_loops"] = 10
                        }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"Symbol\":\"loop\""),
                $"JSON 应包含 Symbol: loop\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"main_loop\""),
                $"JSON 应包含 NodeId: main_loop\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"FuncName\":\"agent_loop\""),
                $"JSON 应包含 FuncName: agent_loop\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("loop_condition"),
                $"JSON 应包含 loop_condition\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("max_loops"),
                $"JSON 应包含 max_loops\n实际 JSON: {json}");
        }


        [TestMethod]
        public void BuildWorkflowJson_条件分支节点_应生成有效JSON()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "condition",
                        NodeId = "check_status",
                        FuncName = "evaluate_condition",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["input_key"] = "status",
                            ["expected_value"] = "success"
                        },
                        ResultKeys = new List<string> { "is_match" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["true_branch"] = "success_handler",
                            ["false_branch"] = "retry_handler"
                        }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"Symbol\":\"condition\""),
                $"JSON 应包含 Symbol: condition\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"check_status\""),
                $"JSON 应包含 NodeId: check_status\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("true_branch"),
                $"JSON 应包含 true_branch\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("false_branch"),
                $"JSON 应包含 false_branch\n实际 JSON: {json}");
        }


        [TestMethod]
        public void BuildWorkflowJson_并行节点_应生成有效JSON()
        {
            var fragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "parallel",
                        NodeId = "parallel_tasks",
                        FuncName = "execute_parallel",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["tasks"] = new List<object>
                            {
                                new Dictionary<string, object> { ["name"] = "task_a", ["command"] = "scan" },
                                new Dictionary<string, object> { ["name"] = "task_b", ["command"] = "analyze" },
                                new Dictionary<string, object> { ["name"] = "task_c", ["command"] = "report" }
                            },
                            ["max_concurrency"] = 3
                        },
                        ResultKeys = new List<string> { "parallel_results" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["aggregation"] = "merge_results",
                            ["timeout_seconds"] = 60
                        }
                    }
                }
            };

            var json = _merger.BuildWorkflowJson(fragment);

            Assert.IsFalse(string.IsNullOrEmpty(json), "应返回非空字符串");
            using var doc = JsonDocument.Parse(json);

            var jsonString = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.IsTrue(jsonString.Contains("\"Symbol\":\"parallel\""),
                $"JSON 应包含 Symbol: parallel\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("\"NodeId\":\"parallel_tasks\""),
                $"JSON 应包含 NodeId: parallel_tasks\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("max_concurrency"),
                $"JSON 应包含 max_concurrency\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("aggregation"),
                $"JSON 应包含 aggregation\n实际 JSON: {json}");
            Assert.IsTrue(jsonString.Contains("timeout_seconds"),
                $"JSON 应包含 timeout_seconds\n实际 JSON: {json}");
        }
    }
}

