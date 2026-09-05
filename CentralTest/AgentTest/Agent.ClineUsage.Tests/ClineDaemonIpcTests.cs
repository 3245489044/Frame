using Agent.Modules.Pool;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.StateBlock;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// ClineDaemonIpc 阶段追踪帧解析测试（观测默认开，无需 node/AI）：
    /// 验证 type:"trace" 帧触发 Trace 事件并携带顶层 Stage/Detail，且不污染 Frame 事件（心跳/pong）。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class ClineDaemonIpcTests
    {
        [TestMethod]
        public void Trace帧_应触发Trace事件并携带Stage与Detail()
        {
            var ipc = new ClineDaemonIpc();
            DaemonResponse? seen = null;
            ipc.Trace += r => seen = r;

            const string line = "{\"id\":\"\",\"type\":\"trace\",\"stage\":\"ensure-core-start\",\"detail\":{\"timeoutMs\":3000}}";
            var consumed = ipc.TryHandleLine(line);

            Assert.IsTrue(consumed, "trace 帧应被消费");
            Assert.IsNotNull(seen, "Trace 事件应触发");
            Assert.AreEqual("ensure-core-start", seen!.Stage, "Stage 应从顶层字段解析");
            Assert.IsNotNull(seen.Detail, "Detail 应解析");
            Assert.AreEqual(3000, seen.Detail!.Value.GetProperty("timeoutMs").GetInt32(), "Detail.timeoutMs 应解析");
        }

        [TestMethod]
        public void Trace帧_不应触发Frame事件()
        {
            var ipc = new ClineDaemonIpc();
            var frameCount = 0;
            ipc.Frame += _ => frameCount++;

            ipc.TryHandleLine("{\"id\":\"\",\"type\":\"trace\",\"stage\":\"warmup-start\"}");

            Assert.AreEqual(0, frameCount, "trace 帧不应进入 Frame 事件");
        }

        [TestMethod]
        public void 心跳帧_应触发Frame事件()
        {
            var ipc = new ClineDaemonIpc();
            DaemonResponse? frame = null;
            ipc.Frame += r => frame = r;

            ipc.TryHandleLine("{\"id\":\"warmup-1\",\"type\":\"heartbeat\",\"elapsedMs\":1002}");

            Assert.IsNotNull(frame, "心跳帧应触发 Frame");
            Assert.AreEqual("heartbeat", frame!.Type);
        }

        [TestMethod]
        public void Persist帧_应触发Persist事件并携带kind_sdk_sessionId()
        {
            var ipc = new ClineDaemonIpc();
            ClinePersistFrame? persist = null;
            ipc.Persist += p => persist = p;

            const string line =
                "{\"id\":\"\",\"type\":\"persist\",\"detail\":{\"kind\":\"messages\",\"sdk\":\"cline\",\"sessionId\":\"s1\",\"payload\":[{\"role\":\"user\",\"text\":\"hi\"}]}}";
            var consumed = ipc.TryHandleLine(line);

            Assert.IsTrue(consumed, "persist 帧应被消费");
            Assert.IsNotNull(persist, "Persist 事件应触发");
            Assert.AreEqual("messages", persist!.Kind, "kind 应携带 messages");
            Assert.AreEqual("cline", persist.Sdk);
            Assert.AreEqual("s1", persist.SessionId);
            Assert.IsNotNull(persist.Payload, "Payload 应解析");
        }

        [TestMethod]
        public void Persist帧_不应触发Frame事件()
        {
            var ipc = new ClineDaemonIpc();
            var frameCount = 0;
            ipc.Frame += _ => frameCount++;

            ipc.TryHandleLine("{\"id\":\"\",\"type\":\"persist\",\"detail\":{\"kind\":\"session\",\"sdk\":\"claude\",\"sessionId\":\"s2\"}}");

            Assert.AreEqual(0, frameCount, "persist 帧不应进入 Frame 事件");
        }
    }
}