using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core;
using Agent.State;      // AgentLoopFunction
using WorkFlowMethod.Usage;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace Agent.Core.Tests
{
    [TestClass]
    [TestCategory("EndToEnd")]
    public class EndToEndIntegrationTests
    {
        private DynamicWorkflowMerger _merger = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _merger = new DynamicWorkflowMerger();
            WorkFlowMethodUsage.EnsureInitialized();
        }

        [TestMethod]
        public async Task CompleteWorkflowExecution_JSON构建_动态拼接_参数传递_循环_端到端验证()
        {
            var testResults = new Dictionary<string, bool>();

            #region 阶段1: JSON 工作流构建

            var workflowJson = WorkFlowMethodUsage.BuildWorkflow(new List<object>
            {
                WorkFlowMethodUsage.CreateNode(
                    symbol: "start",
                    nodeId: "init",
                    funcName: "echo",
                    parameterKeys: new object[] { "input" },
                    resultKeys: new object[] { "output" }
                ),
                WorkFlowMethodUsage.CreateNode(
                    symbol: "end",
                    nodeId: "finish",
                    funcName: "complete",
                    resultKeys: new object[] { "final_result" }
                )
            });

            Assert.IsFalse(string.IsNullOrEmpty(workflowJson), "工作流 JSON 不应为空");
            testResults["阶段1_JSON工作流构建"] = true;

            #endregion

            #region 阶段2: 动态工作流拼接

            var fragmentJson = @"
            {
                ""workflow_fragment"": {
                    ""nodes"": [
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""dynamic_step"",
                            ""FuncName"": ""echo"",
                            ""ParameterValues"": { ""input"": ""动态拼接测试"" },
                            ""ResultKeys"": [""dynamic_output""]
                        }
                    ],
                    ""KeyMapping"": {
                        ""dynamic_output"": ""processed_result""
                    }
                }
            }";

            var fragment = _merger.ParseFragment(fragmentJson);
            Assert.IsNotNull(fragment, "工作流片段解析不应为 null");
            Assert.AreEqual(1, fragment.Nodes.Count, "应包含 1 个节点");
            testResults["阶段2_动态工作流拼接"] = true;

            #endregion

            #region 阶段3: 参数键传递

            var paramFragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step1",
                        FuncName = "echo",
                        ParameterKeys = new List<string> { "input_key" },
                        ResultKeys = new List<string> { "output_key" }
                    },
                    new FragmentNode
                    {
                        Symbol = "process",
                        NodeId = "step2",
                        FuncName = "echo",
                        ParameterKeys = new List<string> { "output_key" },
                        ResultKeys = new List<string> { "final_key" }
                    }
                }
            };

            var paramJson = _merger.BuildWorkflowJson(paramFragment);
            Assert.IsTrue(paramJson.Contains("ParameterKeys"), "JSON 应包含 ParameterKeys");
            Assert.IsTrue(paramJson.Contains("ResultKeys"), "JSON 应包含 ResultKeys");
            testResults["阶段3_参数键传递"] = true;

            #endregion

            #region 阶段4: 循环节点构建

            var loopFragment = new WorkflowFragment
            {
                Nodes = new List<FragmentNode>
                {
                    new FragmentNode
                    {
                        Symbol = "loop",
                        NodeId = "count_loop",
                        FuncName = "agent_loop",
                        ParameterValues = new Dictionary<string, object>
                        {
                            ["user_input"] = "循环测试",
                            ["max_iterations"] = 3
                        },
                        ResultKeys = new List<string> { "loop_result" },
                        NextNodeConfig = new Dictionary<string, object>
                        {
                            ["loop_condition"] = "continue",
                            ["max_loops"] = 5
                        }
                    }
                }
            };

            var loopJson = _merger.BuildWorkflowJson(loopFragment);
            Assert.IsTrue(loopJson.Contains("\"Symbol\":\"loop\""), "JSON 应包含 loop 符号");
            Assert.IsTrue(loopJson.Contains("max_iterations"), "JSON 应包含循环配置");
            testResults["阶段4_循环节点构建"] = true;

            #endregion

            #region 阶段5: 完整端到端执行

            var context = new WfContext(
                parameters: new Dictionary<string, object> { ["user_input"] = "端到端测试" },
                data: new Dictionary<string, object>(),
                executionId: "e2e_test_001",
                nodeKey: "e2e_node",
                workflowId: "e2e_workflow"
            );

            var executeResult = await _merger.MergeAndExecuteAsync(fragmentJson, context);
            Assert.IsNotNull(executeResult, "执行结果不应为 null");

            var resultDict = executeResult as dynamic;
            Assert.IsNotNull(resultDict, "执行结果应可转换为 dynamic");
            testResults["阶段5_完整端到端执行"] = true;

            #endregion

            foreach (var (phase, passed) in testResults)
            {
                Assert.IsTrue(passed, $"阶段失败: {phase}");
            }
        }

        [TestMethod]
        public async Task DynamicWorkflowExecution_多节点链式执行_参数传递验证()
        {
            var fragmentJson = @"
            {
                ""workflow_fragment"": {
                    ""nodes"": [
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""input_transform"",
                            ""FuncName"": ""transform_input"",
                            ""ParameterKeys"": [""user_input""],
                            ""ParameterValues"": { ""transform_type"": ""uppercase"" },
                            ""ResultKeys"": [""transformed_input""]
                        },
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""data_process"",
                            ""FuncName"": ""process_data"",
                            ""ParameterKeys"": [""transformed_input""],
                            ""ResultKeys"": [""processed_output""]
                        },
                        {
                            ""Symbol"": ""process"",
                            ""NodeId"": ""result_format"",
                            ""FuncName"": ""format_result"",
                            ""ParameterKeys"": [""processed_output""],
                            ""ResultKeys"": [""final_output""]
                        }
                    ],
                    ""KeyMapping"": {
                        ""user_input"": ""original_input"",
                        ""final_output"": ""response""
                    }
                }
            }";

            var fragment = _merger.ParseFragment(fragmentJson);
            Assert.IsNotNull(fragment, "片段解析失败");
            Assert.AreEqual(3, fragment.Nodes.Count, "应包含 3 个节点");

            var json = _merger.BuildWorkflowJson(fragment);
            Assert.IsTrue(json.Contains("\"NodeId\":\"input_transform\""), "应包含第一个节点");
            Assert.IsTrue(json.Contains("\"NodeId\":\"data_process\""), "应包含第二个节点");
            Assert.IsTrue(json.Contains("\"NodeId\":\"result_format\""), "应包含第三个节点");
            Assert.IsTrue(json.Contains("KeyMapping"), "应包含键映射");

            var context = new WfContext(
                parameters: new Dictionary<string, object> { ["user_input"] = "test_data" },
                data: new Dictionary<string, object>(),
                executionId: "chain_test_001",
                nodeKey: "chain_node",
                workflowId: "chain_workflow"
            );

            var result = await _merger.MergeAndExecuteAsync(fragmentJson, context);
            Assert.IsNotNull(result, "执行结果不应为 null");
        }

        [TestMethod]
        public void WorkflowBuilder_多种节点类型_构建验证()
        {
            var nodes = new List<object>();

            nodes.Add(WorkFlowMethodUsage.CreateNode(
                symbol: "start",
                nodeId: "entry",
                funcName: "init",
                parameterKeys: new object[] { "trigger" },
                resultKeys: new object[] { "init_result" }
            ));

            nodes.Add(WorkFlowMethodUsage.CreateNode(
                symbol: "condition",
                nodeId: "decision",
                funcName: "evaluate",
                parameterKeys: new object[] { "init_result" },
                resultKeys: new object[] { "is_valid" }
            ));

            nodes.Add(WorkFlowMethodUsage.CreateNode(
                symbol: "loop",
                nodeId: "retry_loop",
                funcName: "retry",
                parameterKeys: new object[] { "is_valid" },
                resultKeys: new object[] { "retry_result" }
            ));

            nodes.Add(WorkFlowMethodUsage.CreateNode(
                symbol: "parallel",
                nodeId: "parallel_exec",
                funcName: "parallel",
                parameterKeys: new object[] { "retry_result" },
                resultKeys: new object[] { "parallel_result" }
            ));

            nodes.Add(WorkFlowMethodUsage.CreateNode(
                symbol: "end",
                nodeId: "exit",
                funcName: "complete",
                resultKeys: new object[] { "final_output" }
            ));

            var workflowJson = WorkFlowMethodUsage.BuildWorkflow(nodes);

            Assert.IsTrue(workflowJson.Contains("\"Symbol\":\"start\""), "应包含 start 节点");
            Assert.IsTrue(workflowJson.Contains("\"Symbol\":\"condition\""), "应包含 condition 节点");
            Assert.IsTrue(workflowJson.Contains("\"Symbol\":\"loop\""), "应包含 loop 节点");
            Assert.IsTrue(workflowJson.Contains("\"Symbol\":\"parallel\""), "应包含 parallel 节点");
            Assert.IsTrue(workflowJson.Contains("\"Symbol\":\"end\""), "应包含 end 节点");

            using var doc = JsonDocument.Parse(workflowJson);
            Assert.IsNotNull(doc, "JSON 应可解析");
        }

        [TestMethod]
        public void KeyMapping_层次化处理_验证()
        {
            WorkFlowMethod.Skeletons.NodeProcess.KeyMapping.KeyProcessor.HierarchyPrefix = "_";
            WorkFlowMethod.Skeletons.NodeProcess.KeyMapping.KeyProcessor.HierarchySuffix = "_";

            var originalKeys = new List<string> { "input", "output" };
            var processedKeys = WorkFlowMethod.Skeletons.NodeProcess.KeyMapping.KeyProcessor.AddHierarchyToKeys(originalKeys, "c0");

            Assert.AreEqual(2, processedKeys.Count, "应处理所有键");
            Assert.AreEqual("_c0_input", processedKeys[0], "第一个键应添加层次前缀/后缀");
            Assert.AreEqual("_c0_output", processedKeys[1], "第二个键应添加层次前缀/后缀");

            var hierarchy = WorkFlowMethod.Skeletons.NodeProcess.KeyMapping.KeyProcessor.ExtractHierarchyFromNode(new NodeConfig
            {
                basicConfig = new NodeConfig.BasicConfig
                {
                    Callkey = "process_c0"
                }
            });

            Assert.AreEqual("1", hierarchy, "应正确提取层次信息");
        }

    }
}
