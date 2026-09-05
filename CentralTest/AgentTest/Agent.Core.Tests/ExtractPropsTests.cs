using System;
using System.Collections.Generic;
using System.Reflection;
using Agent.Contract;
using Agent.Contract.Nodes;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 节点结果端口提取器（<see cref="NodeExecutionService.ExtractProps"/>）定向测试 —— 锁定 8.23「节点端口空 → 终答空」根因：
    /// 本地 LLM 返回非空（resp 有内容）但 nodePorts=[]，说明 ExtractProps 未把 ResultKeys 写入节点输出端口。
    /// 本测试直接验证 ExtractProps 对 AgentExecutorResult（ClineResult）+ ResultKeys 的定向写入行为。
    /// </summary>
    [TestClass]
    public class ExtractPropsTests
    {
        [TestMethod]
        public void ExtractProps_ClineResult_WritesAllResultKeys()
        {
            var step = new RoundStep("cline_execute", new[] { "system" }, new[] { "act_result" });
            var node = NodeExecutionService.BuildNode(step);
            var result = new ClineResult(true, "本地回答", null);
            var props = InvokeExtractProps(node, result);

            Assert.AreEqual(1, props.Count, "ClineResult 应按 ResultKeys 定向写入一个端口");
            Assert.IsTrue(props.ContainsKey("act_result"), "应写入 act_result 端口");
            Assert.AreEqual("本地回答", ((ClineResult)props["act_result"]!).Text);
        }

        [TestMethod]
        public void ExtractProps_NullResult_ReturnsEmpty()
        {
            var step = new RoundStep("cline_execute", new[] { "system" }, new[] { "act_result" });
            var node = NodeExecutionService.BuildNode(step);
            var props = InvokeExtractProps(node, null);
            Assert.AreEqual(0, props.Count, "null 结果 → 无输出端口（对齐 InitialStateNullity/RepeatedCallStable 语义）");
        }

        [TestMethod]
        public void ExtractProps_NonAgentExecutorResult_ReflectsPublicProps()
        {
            var step = new RoundStep("foo", new[] { "x" }, new[] { "r1" });
            var node = NodeExecutionService.BuildNode(step);
            var props = InvokeExtractProps(node, new SimplePayload("value"));
            Assert.AreEqual("value", props["Text"], "非 AgentExecutorResult 按公开属性名反射");
        }

        private static Dictionary<string, object?> InvokeExtractProps(AgentNode node, object? result)
        {
            var method = typeof(NodeExecutionService).GetMethod("ExtractProps",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ExtractProps 应存在");
            return (Dictionary<string, object?>)method!.Invoke(null, new object?[] { node, result })!;
        }

        private sealed record SimplePayload(string Text);
    }
}
