
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core;
using ParameterResolver.KeyMapping;

namespace Agent.Core.Tests
{
    /// <summary>DynamicWorkflowMerger 旧形态 key_mapping + 三字段形态兼容测试（T8/T9）。</summary>
    [TestClass]
    [TestCategory("DynamicWorkflowMerger")]
    public class DynamicWorkflowMergerShorthandCompatTests
    {
        private DynamicWorkflowMerger _merger = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _merger = new DynamicWorkflowMerger();
        }

        [TestMethod]
        public void T8_OldShorthandKeyMapping_MigratedToGlobalExact()
        {
            var json = @"{
                ""workflow_fragment"": {
                    ""nodes"": [
                        { ""NodeId"": ""n1"", ""FuncName"": ""f"", ""ParameterKeys"": [""p""], ""ResultKeys"": [""r""] }
                    ],
                    ""key_mapping"": { ""r"": ""p"" }
                }
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment);
            Assert.AreEqual(1, fragment.KeyMapping.Count, "旧 key_mapping 应保留");
            Assert.IsNotNull(fragment.GlobalKeyMapping, "旧 key_mapping 应合并为 GlobalKeyMapping");
            var rule = fragment.GlobalKeyMapping!.Rules.SingleOrDefault(r =>
                r.RuleType == MappingRuleType.Exact && r.SourcePattern == "r" && r.TargetPattern == "p");
            Assert.IsNotNull(rule, "应存在 Exact 规则 r→p");
        }

        [TestMethod]
        public void T9_ThreeFieldForm_ParsesPortsAndMappings()
        {
            var json = @"{
                ""nodes"": [
                    {
                        ""NodeId"": ""n1"", ""FuncName"": ""f"",
                        ""ParameterKeys"": [""p""], ""ResultKeys"": [""r""],
                        ""InputMapping"": { ""a"": ""p"" }
                    }
                ],
                ""InputPorts"": [ { ""Key"": ""in"" } ],
                ""OutputPorts"": [ { ""Key"": ""out"", ""SourceKey"": ""r"" } ],
                ""GlobalKeyMapping"": { ""g"": ""gg"" }
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment);
            Assert.IsNotNull(fragment.Nodes[0].InputMapping, "节点 InputMapping 应解析");
            Assert.IsNotNull(fragment.GlobalKeyMapping, "容器 GlobalKeyMapping 应解析");
            Assert.AreEqual(1, fragment.InputPorts!.Count);
            Assert.AreEqual("r", fragment.OutputPorts![0].SourceKey);

            // 绑定契约转换：与三字段形态一致（T9 优先级合并前提）
            Assert.AreSame(fragment.GlobalKeyMapping, fragment.ToBindingContainer().GlobalKeyMapping);
            Assert.AreSame(fragment.Nodes[0].InputMapping, fragment.Nodes[0].ToBindingStep().InputMapping);
        }

        [TestMethod]
        public void T10_GlobalSynonymMapping_DoesNotIntrudeNodes()
        {
            var json = @"{
                ""nodes"": [
                    { ""NodeId"": ""n1"", ""FuncName"": ""cline_execute"", ""ParameterKeys"": [""session_id""], ""ResultKeys"": [""exec_result""] }
                ],
                ""GlobalKeyMapping"": { ""cline_session"": ""session_id"" }
            }";

            var fragment = _merger.ParseFragment(json);

            Assert.IsNotNull(fragment);
            var rule = fragment.GlobalKeyMapping!.Rules.Single(r => r.SourcePattern == "cline_session");
            Assert.AreEqual("session_id", rule.TargetPattern);

            // 全局映射不侵入节点：节点无 InputMapping、参数键原样
            Assert.IsNull(fragment.Nodes[0].InputMapping);
            Assert.AreEqual("session_id", fragment.Nodes[0].ParameterKeys[0]);
        }
    }
}
