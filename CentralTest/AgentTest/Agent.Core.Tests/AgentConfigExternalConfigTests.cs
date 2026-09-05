using Agent.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests;

/// <summary>
/// 验证 agent-config.json 路径解析 + provider 解析 + 阶段3「配置单一事实源 / 时序修复」：
/// <see cref="AgentConfig.ApplyExternalConfig"/> 从仓库默认路径加载并应用 LLM 配置（真实默认 semantic + BaseUrl），
/// 且**解析多智能体表（Agents/DefaultAgent）**——使 <see cref="AgentOptionalModules.RegisterAgents"/>（装配管线阶段 4）
/// 在 <see cref="AgentConfig.ApplyPersistence"/>（阶段 5）之前即能消费命名智能体配置，消除「Agents 未被解析 → cline 默认回退
/// config.Llm（semantic）、丢失 Agents.cline 的 BaseUrl/Provider」的次级根因 C / 时序错位。
/// </summary>
[TestClass]
public class AgentConfigExternalConfigTests
{
    [TestMethod]
    public void ApplyExternalConfig_ResolvesDefaultPath_AppliesLlm_And_BindsAgents()
    {
        var config = new AgentConfig();

        var applied = config.ApplyExternalConfig();

        // agent-config.json 存在于 仓库根/Projects/Agent/Config → 默认路径解析应成功
        Assert.IsTrue(applied, "agent-config.json 应从默认 仓库根/Projects/Agent/Config 解析并被应用");
        // agent-config.json 声明 Llm.Provider=semantic（真实默认）→ ResolveProvider 返回 semantic
        Assert.AreEqual("semantic", LlmHosting.ResolveProvider(config),
            "agent-config.json 声明的真实 provider（semantic）应生效");
        // 阶段3 单一事实源：Llm 节显式补 BaseUrl，真实端点不再依赖凭据隐式回退（可观测、明确）
        Assert.AreEqual("https://api.deepseek.com", config.Llm.BaseUrl,
            "Llm 节应显式声明真实 BaseUrl（配置单一事实源）");
        // 阶段3 时序修复：ApplyExternalConfig 必须解析 Agents 节 → 命名智能体表被填充（供 RegisterAgents 阶段 4 消费）
        Assert.IsTrue(config.Agents.ContainsKey("cline"),
            "ApplyExternalConfig 应解析 Agents 节（阶段3 时序修复：RegisterAgents 前可见）");
        Assert.AreEqual("deepseek", config.Agents["cline"].Provider);
        // 单一事实源：Agents.cline.BaseUrl 与 Llm.BaseUrl 一致（真实端点），不再指向本地 8787（消除 split-brain：
        // 真实模式 cline 经 daemon 走真实端点；本地联调由 LocalOnly 本地绕开承担，不依赖 Agents.cline.BaseUrl 指向本地）
        Assert.AreEqual(config.Llm.BaseUrl, config.Agents["cline"].BaseUrl,
            "Agents.cline.BaseUrl 应与 Llm.BaseUrl 一致（真实端点单一事实源，消除 split-brain）");
        Assert.IsFalse(config.Agents["cline"].BaseUrl!.Contains("127.0.0.1"),
            "Agents.cline.BaseUrl 不应指向本地端点（本地联调由 LocalOnly 本地绕开承担）");
    }
}
