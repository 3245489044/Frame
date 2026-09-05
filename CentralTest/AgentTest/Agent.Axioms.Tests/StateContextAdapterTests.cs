using System.Linq;
using Agent.Axioms;
using Agent.Axioms.DataContainers;
using Agent.Axioms.StateAdapters;
using Agent.Contract.Data;
using Agent.Contract.DataContainers;
using Agent.Core.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.StateBlock.Core;
using Share.StateManagement;
using TConfig;

namespace Agent.Axioms.Tests.StateAdapters
{
    /// <summary>StateContextAdapter 状态层行为测试（T11/T12）。</summary>
    [TestClass]
    public sealed class StateContextAdapterTests
    {
        private static StateCenter NewCenter() => new(new StorageCell("test"));
        private static StateContextAdapter NewAdapter(StateCenter center, string roundId) => new(center, "t", "s", roundId);
        private static readonly DataContainerBinder Binder = DataContainerBinder.Instance;

        [TestMethod]
        public void T11_HarvestClearsTransient()
        {
            var center = NewCenter();
            var state = NewAdapter(center, "r1");
            state.SetTransient("exec_result", "v");

            var container = new BindingContainer
            {
                OutputPorts = new[] { new PortSpec { Key = "exec_result" } },
            };

            var diag = Binder.BindHarvestPhase(state, container);

            // Transient 层清空 + 输出端口写入 Session
            Assert.IsFalse(state.EnumerateTransientKeys().Any(), "Transient 层应已清空");
            Assert.AreEqual("v", state.GetSession<string>("exec_result"));
            Assert.AreEqual(0, diag.Warnings.Count);
        }

        [TestMethod]
        public void T12_CrossRoundPersistence()
        {
            var center = NewCenter();

            // Round N：exec_result 写入 Session 层（经 Harvest）
            var s1 = NewAdapter(center, "r1");
            s1.SetTransient("exec_result", "R1");
            Binder.BindHarvestPhase(s1, new BindingContainer
            {
                OutputPorts = new[] { new PortSpec { Key = "exec_result" } },
            });

            // Round N+1：Seed 从 Session 层读入 Transient
            var s2 = NewAdapter(center, "r2");
            Binder.BindSeedPhase(s2, new BindingContainer
            {
                InputPorts = new[] { new PortSpec { Key = "exec_result", IsRequired = true } },
            });

            Assert.AreEqual("R1", s2.GetTransient<string>("exec_result"));
        }

        [TestMethod]
        public void GeneralizedReadWrite_LayerOverloads()
        {
            var center = NewCenter();
            var key = new StateKey("ns", "scope", "k");

            // 指定层写/读（Transient / Config），互不串层
            center.Write(key, "tv", StorageLayer.Transient);
            center.Write(key, "cv", StorageLayer.Config);
            Assert.AreEqual("tv", center.Read<string>(key, StorageLayer.Transient));
            Assert.AreEqual("cv", center.Read<string>(key, StorageLayer.Config));

            // Session 默认路径（无 layer 参数）保持原语义，且与 Transient 隔离
            center.Write(key, "sv");
            Assert.AreEqual("sv", center.Read<string>(key));
            Assert.AreEqual("sv", center.Read<string>(key, StorageLayer.Session));
            Assert.AreEqual("tv", center.Read<string>(key, StorageLayer.Transient), "Transient 层不应被 Session 写覆盖");
        }

        [TestMethod]
        public void Vars_IsBackedByTransient()
        {
            var center = NewCenter();
            var adapter = NewAdapter(center, "r1");

            // 有适配器：Vars 读写直落 StateCenter.Transient（框架中立 NodeRoundContext 承载 IDataSpace 来源，经 StateBackedDataSpace 外置）
            var ctx = new NodeRoundContext(new StateBackedDataSpace(adapter));
            ctx.Vars.Set("exec_result", "v");
            Assert.AreEqual("v", adapter.GetTransient<string>("exec_result"), "Vars 应基于通用 Transient 逻辑");
            Assert.AreEqual("v", ctx.Vars.Get<string>("exec_result"));
            Assert.IsTrue(ctx.Vars.Remove("exec_result"));
            Assert.IsFalse(adapter.TryGetTransient<string>("exec_result", out _));

            // 无适配器：回退进程内 DataSpace（兼容/测试路径）
            var ctxFallback = new NodeRoundContext(new DataSpace(validate: false));
            ctxFallback.Vars.Set("x", 1);
            Assert.IsTrue(ctxFallback.Vars.ContainsKey("x"));
        }
    }
}
