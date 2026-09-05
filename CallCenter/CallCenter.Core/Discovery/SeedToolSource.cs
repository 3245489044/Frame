using CallCenter.Core.Tools;

namespace CallCenter.Core.Discovery;

/// <summary>
/// 临时种子源（P0 过渡用）。经 D7 修订后，种子只保留确定性宿主能力 <c>read_nodes</c>；
/// <c>llm_chat/answer/cline/guard_input</c> 等 Agent 侧工具随 LLM 收拢于 Agent（不进 CallCenter），
/// 此处不再提供。真实跨语言工具一律由各 Track 的 <see cref="IToolSource"/>（如
/// <see cref="CSharpFunctionLibrarySource"/>）发现，本源在 P1/P2 打通后移除。
/// </summary>
public sealed class SeedToolSource : IToolSource
{
    public string Name { get; } = "seed.read_nodes";

    private static readonly Lazy<ToolDescriptor> ReadNodes = new(() => new ToolDescriptor(
        Id: "tool:csharp:read_nodes",
        Name: "read_nodes",
        Backend: ToolBackend.CSharp,
        Version: 1,
        Description: "读取执行节点（确定性宿主能力）",
        InputSchema: new Dictionary<string, object?>(),
        OutputSchema: new Dictionary<string, object?>(),
        Flags: ToolFlags.Deterministic | ToolFlags.HotReloadable));

    /// <summary>D7 修订后不得出现的 Agent/LLM 侧工具名（回归守卫）。</summary>
    public static readonly string[] AgentSideToolNames = { "llm_chat", "answer", "cline", "guard_input" };

    public IReadOnlyList<ToolDescriptor> Discover() => new[] { ReadNodes.Value };
}
