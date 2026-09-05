using CallCenter.Core.Discovery;

namespace CallCenter.Tests;

/// <summary>
/// D7 种子集修订回归闸：CallCenter 种子不再含 Agent/LLM 侧工具（llm_chat/answer/cline/guard_input 随 LLM
/// 收拢于 Agent），只保留确定性宿主能力 read_nodes + 真实 C# 函数库。
/// </summary>
[TestClass]
public class SeedToolSourceTests
{
    [TestMethod]
    public void D7_Seed_Contains_Only_ReadNodes_And_No_AgentSide_Tools()
    {
        var tools = new SeedToolSource().Discover();

        Assert.AreEqual(1, tools.Count, "D7 后临时种子仅保留 read_nodes");
        Assert.AreEqual("read_nodes", tools[0].Name);
    }

    [TestMethod]
    public void D7_Seed_Excludes_Every_AgentSide_Tool()
    {
        var tools = new SeedToolSource().Discover();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var agentTool in SeedToolSource.AgentSideToolNames)
            Assert.IsFalse(names.Contains(agentTool), $"Agent 侧工具 '{agentTool}' 不应出现在 CallCenter 种子中（D7）");
    }
}
