using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TreeTraceFramework.Abstractions;
using WorkFlowMethod.CallFrame.Abstractions;
using WorkFlowMethod.CallFrame.Core;
using WorkFlowMethod.CallFrame.Models;
using CallFrameCore = WorkFlowMethod.CallFrame.Core.CallFrame;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("CallFrame")]
    public class CallFrameUsageTests
    {
        [TestMethod]
        public void CallFrameService_CreateGetDelete_ShouldWork()
        {
            var service = new CallFrameService();
            Assert.IsNotNull(CallFrameService.Default);

            var cf = service.CreateCallFrame("session_001");
            Assert.AreEqual("session_001", cf.SessionId);
            Assert.AreSame(cf, service.GetCallFrame("session_001"));

            Assert.IsNull(service.GetCallFrame("nonexistent"));
            Assert.IsNull(service.GetCallFrame(null));
            Assert.ThrowsException<ArgumentNullException>(() => service.CreateCallFrame(null));
            Assert.ThrowsException<ArgumentNullException>(() => service.CreateCallFrame(""));

            Assert.AreSame(cf, service.GetOrCreateCallFrame("session_001"));
            Assert.IsNotNull(service.GetOrCreateCallFrame("session_new"));

            Assert.IsTrue(service.DeleteCallFrame("session_001"));
            Assert.IsFalse(service.DeleteCallFrame("nonexistent"));
            Assert.IsFalse(service.DeleteCallFrame(null));
        }

        [TestMethod]
        public void CallFrame_CreateRoot_ShouldWork()
        {
            var cf = new CallFrameCore("session_cf_001");
            Assert.AreEqual("session_cf_001", cf.SessionId);
            Assert.IsFalse(string.IsNullOrEmpty(cf.CallFrameId));

            var root = cf.CreateRoot("MainFunction", "initial input");
            Assert.AreEqual("MainFunction", root.Name);
            Assert.AreSame(root, cf.Root);

            root.AddLabel("important");
            Assert.AreEqual(1, root.Labels.Count());

            root.AddInput("in");
            root.AddOutput("out");
            Assert.AreEqual(2, root.Inputs.Count());
            Assert.AreEqual("in", root.Inputs.Last());
            Assert.AreEqual("out", root.Outputs.First());

            Assert.IsTrue(cf.ToString().Contains("CallFrame"));
        }

        [TestMethod]
        public void CallFrameNode_ShouldStoreRecordsAndLabels()
        {
            var cf = new CallFrameCore("session_cfn_001");
            var root = cf.CreateRoot("Func");
            root.AddRecord("record_data");
            root.AddLabel("label1");
            root.AddLabel("label2");

            Assert.AreEqual("record_data", root.Records.First());
            Assert.AreEqual(2, root.Labels.Count());
            Assert.IsFalse(string.IsNullOrEmpty(root.NodeId));
        }

        [TestMethod]
        public void CallFrameService_StoreRestore_ShouldRoundtrip()
        {
            var service = new CallFrameService();
            var cf = service.CreateCallFrame("session_store_001");
            cf.CreateRoot("StoredFunc");

            service.StoreCallFrame(cf);
            var restored = service.RestoreCallFrame(cf.CallFrameId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(cf.CallFrameId, restored.CallFrameId);

            Assert.IsNull(service.RestoreCallFrame("nonexistent_id"));
            Assert.IsNull(service.RestoreCallFrame(null));
            Assert.ThrowsException<ArgumentNullException>(() => service.StoreCallFrame(null));
        }

        [TestMethod]
        public void CallFrameService_AddNodeFromConfig_ShouldWork()
        {
            var service = new CallFrameService();
            var config = new AddNodeConfig { Name = "ConfigRoot", Inputs = { "in1" }, Outputs = { "out1" }, Labels = { "cfg" }, Records = { "rec1" } };
            var node = service.AddNodeFromConfig("session_cfg_001", config);
            Assert.AreEqual("ConfigRoot", node.Name);

            Assert.ThrowsException<ArgumentNullException>(() => service.AddNodeFromConfig("session_cfg_003", null));
        }
    }
}
