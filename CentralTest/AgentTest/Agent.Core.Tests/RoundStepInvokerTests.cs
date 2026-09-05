using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes.Execution;
using Share.HierarchySupport;

namespace Agent.Core.Tests
{
    /// <summary>
    /// <see cref="RoundStepInvoker"/> 定向测试（D2 横切持久化，经框架 <see cref="NodeRoundContext"/>）：
    /// 验证「步骤体 RecordStep → onAfterStep OnAfterStepAsync TryGetStep」的数据通道，与未执行步骤（CanExecute 跳过）的
    /// 持久化安全早退（无产物不触存储、不抛）。
    /// </summary>
    [TestClass]
    public class RoundStepInvokerTests
    {
        private static NodeExecutionRecord NewRecord(string nodeKey)
            => new(nodeKey, "", new Dictionary<string, object?>(), new Dictionary<string, (string Node, string Port)>());

        [TestMethod]
        public async Task OnAfterStep_StepNotExecuted_NoOpWithoutThrowing()
        {
            // D2 横切持久化：步骤未执行（CanExecute 跳过）→ TryGetStep 早退，无产物不触存储、不抛。
            var invoker = new RoundStepInvoker();
            var ctx = new NodeRoundContext(new DataSpace(validate: false));

            await invoker.OnAfterStepAsync(ctx, NewRecord("never_executed"), CancellationToken.None);
        }

        [TestMethod]
        public void RecordStep_TryGetStep_RoundTripsNodePropsAndInput()
        {
            // 步骤体 RecordStep → onAfterStep TryGetStep 回读产物+溯源（横切持久化的数据通道，消除溯源后置丢失）。
            var ctx = new NodeRoundContext(new DataSpace(validate: false));
            var step = new RoundStep("collect", new[] { "user_input" }, new[] { "collect_result" });
            var node = NodeExecutionService.BuildNode(step);
            var props = new Dictionary<string, object?> { ["collect_result"] = "产物" };
            var input = new NodeInputSnapshot(
                new Dictionary<string, object?> { ["user_input"] = "输入" },
                new Dictionary<string, (string Node, string Port)>());

            ctx.RecordStep("collect", node, props, input);

            Assert.IsTrue(ctx.TryGetStep("collect", out var gotNode, out var gotProps, out var gotInput), "已记录步骤应可回读");
            Assert.IsNotNull(gotNode, "节点应回读");
            Assert.AreEqual("产物", gotProps["collect_result"], "产物应原样回读");
            Assert.IsNotNull(gotInput, "入参溯源应回读");
            Assert.IsFalse(ctx.TryGetStep("missing", out _, out _, out _), "未记录步骤应返回 false");
        }

        [TestMethod]
        public async Task OnAfterStep_Success_MarksPortsPersisted()
        {
            // R1 双写消除：步骤横切持久化成功后，登记该步骤 ResultKeys 对应端口到 ctx，
            // 终态信封据此跳过重复写（IsPortPersisted 判定）。无 store 装配（增强路径未启用）时
            // PersistAsync 各分支安全早退但成功返回，登记循环照常执行。
            var invoker = new RoundStepInvoker();
            var ctx = new NodeRoundContext(new DataSpace(validate: false));
            var step = new RoundStep("collect", new[] { "user_input" }, new[] { "collect_result" });
            var node = NodeExecutionService.BuildNode(step);
            var props = new Dictionary<string, object?> { ["collect_result"] = "产物" };
            var input = new NodeInputSnapshot(
                new Dictionary<string, object?>(),
                new Dictionary<string, (string Node, string Port)>());
            ctx.RecordStep("collect", node, props, input);

            await invoker.OnAfterStepAsync(ctx, NewRecord("collect"), CancellationToken.None);

            Assert.IsTrue(ctx.IsPortPersisted("collect", "collect_result"), "横切成功后端口应登记已持久化");
            Assert.IsFalse(ctx.IsPortPersisted("collect", "other"), "未登记端口不应误报已持久化");
        }

        [TestMethod]
        public void PortPersisted_MarkAndQuery_IsPerPortIsolated()
        {
            // 端口登记判定为「节点.端口」粒度，互不污染：guard_output.safe_output 已写不阻断 guard_output.output 补写。
            var ctx = new NodeRoundContext(new DataSpace(validate: false));
            Assert.IsFalse(ctx.IsPortPersisted("guard_output", "safe_output"), "初始未登记");
            ctx.MarkPersistedPort("guard_output", "safe_output");
            Assert.IsTrue(ctx.IsPortPersisted("guard_output", "safe_output"), "登记后应已持久化");
            Assert.IsFalse(ctx.IsPortPersisted("guard_output", "output"), "output 非 ResultKeys，横切不写，不应被误登记");
        }
    }
}
