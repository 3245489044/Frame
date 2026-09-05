
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
    [TestCategory("AgentLoopFunction")]
    public class AgentLoopFunctionTests
    {
        [TestMethod]
        public void IsTaskComplete_含完成标记_应判定完成()
        {
            var json1 = @"{ ""workflow_fragment"": { ""status"": ""complete"" } }";
            Assert.IsTrue(AgentLoopFunction.IsTaskComplete(json1),
                "workflow_fragment.status=complete 应判定为完成");

            var json2 = @"{ ""status"": ""complete"" }";
            Assert.IsTrue(AgentLoopFunction.IsTaskComplete(json2),
                "直接 status=complete 应判定为完成");

            var json3 = @"{ ""workflow_fragment"": { ""status"": ""in_progress"" } }";
            Assert.IsFalse(AgentLoopFunction.IsTaskComplete(json3),
                "status=in_progress 不应判定为完成");

            var text = "普通文本回复";
            Assert.IsFalse(AgentLoopFunction.IsTaskComplete(text),
                "普通文本回复不应判定为完成");

            Assert.IsTrue(AgentLoopFunction.IsTaskComplete(""),
                "空字符串应判定为完成");
        }

        [TestMethod]
        public void IsJsonWorkflowFragment_含节点数组_应识别为片段()
        {
            var json1 = @"{ ""workflow_fragment"": { ""nodes"": [{ ""NodeId"": ""step_1"", ""FuncName"": ""mcp_call_tool"" }] } }";
            Assert.IsTrue(AgentLoopFunction.IsJsonWorkflowFragment(json1),
                "workflow_fragment.nodes 应识别为片段");

            var json2 = @"{ ""nodes"": [{ ""NodeId"": ""step_1"" }] }";
            Assert.IsTrue(AgentLoopFunction.IsJsonWorkflowFragment(json2),
                "直接 nodes 数组应识别为片段");

            var json3 = @"{ ""workflow_fragment"": { ""nodes"": [] } }";
            Assert.IsFalse(AgentLoopFunction.IsJsonWorkflowFragment(json3),
                "空 nodes 数组不应识别为片段");

            Assert.IsFalse(AgentLoopFunction.IsJsonWorkflowFragment("普通文本"),
                "普通文本不应识别为片段");

            Assert.IsFalse(AgentLoopFunction.IsJsonWorkflowFragment(""),
                "空字符串不应识别为片段");
        }

        [TestMethod]
        public void ShouldCompressContext_刚好10条_不应触发压缩()
        {
            var history = new List<Dictionary<string, object>>();
            for (int i = 0; i < 10; i++)
            {
                history.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = $"第 {i + 1} 轮回复"
                });
            }

            var result = AgentLoopFunction.ShouldCompressContext(history);

            Assert.IsFalse(result, "刚好 10 条历史记录不应触发压缩");
        }

        [TestMethod]
        public void ShouldCompressContext_11条_应触发压缩()
        {
            var history = new List<Dictionary<string, object>>();
            for (int i = 0; i < 11; i++)
            {
                history.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = $"第 {i + 1} 轮回复"
                });
            }

            var result = AgentLoopFunction.ShouldCompressContext(history);

            Assert.IsTrue(result, "11 条历史记录应触发压缩");
        }

        [TestMethod]
        public void CompressHistory_空列表_应返回空列表()
        {
            var history = new List<Dictionary<string, object>>();

            var compressed = AgentLoopFunction.CompressHistory(history);

            Assert.IsNotNull(compressed, "压缩结果不应为 null");
            Assert.AreEqual(1, compressed.Count, "空列表压缩后应包含 1 条摘要记录");
            Assert.AreEqual("system", compressed[0]["role"], "摘要记录的 role 应为 system");
            Assert.IsTrue(compressed[0]["content"]!.ToString()!.Contains("[上下文摘要]"),
                "摘要记录应包含 [上下文摘要] 标记");
        }

        [TestMethod]
        public void CompressHistory_3条记录_应保留全部并添加摘要()
        {
            var history = new List<Dictionary<string, object>>();
            for (int i = 0; i < 3; i++)
            {
                history.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = $"第 {i + 1} 轮回复"
                });
            }

            var compressed = AgentLoopFunction.CompressHistory(history);

            Assert.IsNotNull(compressed, "压缩结果不应为 null");
            Assert.AreEqual(4, compressed.Count, "3 条记录压缩后应为 4 条（1 摘要 + 3 原始）");
            Assert.AreEqual("system", compressed[0]["role"], "第一条记录的 role 应为 system");
            Assert.IsTrue(compressed[0]["content"]!.ToString()!.Contains("[上下文摘要]"),
                "第一条记录应包含 [上下文摘要] 标记");
        }

        [TestMethod]
        public void CompressHistory_20条记录_应压缩到6条以内()
        {
            var history = new List<Dictionary<string, object>>();
            for (int i = 0; i < 20; i++)
            {
                history.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = $"第 {i + 1} 轮回复"
                });
            }

            var compressed = AgentLoopFunction.CompressHistory(history);

            Assert.IsNotNull(compressed, "压缩结果不应为 null");
            Assert.IsTrue(compressed.Count <= 6,
                $"20 条记录压缩后 ({compressed.Count}) 应不超过 6 条");

            Assert.AreEqual("system", compressed[0]["role"], "第一条记录的 role 应为 system");
            Assert.IsTrue(compressed[0]["content"]!.ToString()!.Contains("[上下文摘要]"),
                "第一条记录应包含 [上下文摘要] 标记");

            for (int i = 1; i < compressed.Count; i++)
            {
                Assert.AreEqual("assistant", compressed[i]["role"],
                    $"第 {i + 1} 条记录的 role 应为 assistant");
            }
        }
    }
}