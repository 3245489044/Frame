using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core.Loops;
using Agent.Core.TaskPool;
using Operation.LoopFoldMethod.Decision;

namespace Agent.Core.Tests;

/// <summary>
/// AgentTaskTermination（阶段 2a）测试：快照轮次的确定性硬性终止映射 —— halt→Halt、token 耗尽→Timeout、否则→null。
/// 验证「硬性编码直接产出信号」（SEP.DeterministicCoreDisjoint / AX.TerminationBipartition）。
/// </summary>
[TestClass]
public class AgentTaskTerminationTests
{
    [TestMethod]
    public void Check_NoHaltNoExhaustion_ReturnsNull()
    {
        // 确定性核心未到终止阈值 → null（交 LLM skill 产出折叠链标识符 D′）。
        var termination = new AgentTaskTermination();
        Assert.IsNull(termination.Check(new AgentTaskItem()));
    }

    [TestMethod]
    public void Check_Halt_ReturnsHaltSignal()
    {
        // ControlFlag.halt=1（熔断短路）→ TerminationSignal.Halt（TM）。
        var item = new AgentTaskItem();
        item.Flag.SetHalt();
        var termination = new AgentTaskTermination();
        Assert.AreEqual(TerminationSignal.Halt, termination.Check(item));
    }

    [TestMethod]
    public void Check_TokenExhausted_ReturnsTimeout()
    {
        // ProgressToken 耗尽（value≥Max）→ TerminationSignal.Timeout（TO）。
        var item = new AgentTaskItem();
        while (!item.Token.IsExhausted) item.Token.Advance();
        var termination = new AgentTaskTermination();
        Assert.AreEqual(TerminationSignal.Timeout, termination.Check(item));
    }

    [TestMethod]
    public void Check_HaltTakesPrecedenceOverTokenExhaustion()
    {
        // 短路口 halt=1 优先于 token 耗尽（对齐 TerminationDecider：halt → TM 优先）。
        var item = new AgentTaskItem();
        while (!item.Token.IsExhausted) item.Token.Advance();
        item.Flag.SetHalt();
        var termination = new AgentTaskTermination();
        Assert.AreEqual(TerminationSignal.Halt, termination.Check(item));
    }
}
