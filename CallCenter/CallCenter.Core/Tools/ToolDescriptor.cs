namespace CallCenter.Core.Tools;

/// <summary>工具后端（Track）。对应终端面各语言适配器轨。</summary>
public static class ToolBackend
{
    public const string CSharp = "CSharp";
    public const string TypeScript = "TypeScript";
    public const string PowerShell = "PowerShell";
    public const string ExternalMCP = "ExternalMCP";
    public const string Llm = "Llm";
}

/// <summary>工具标志位（进 CSG metadata：驱动防递归/内外二分/热加载）。</summary>
[Flags]
public enum ToolFlags
{
    None = 0,
    /// <summary>触发网/外部能力（LLM、外部 MCP），挂门控。</summary>
    Externalized = 1,
    /// <summary>允许函数级热加载（fingerprint 驱动）。</summary>
    HotReloadable = 2,
    /// <summary>确定性（零 LLM），可缓存结果。</summary>
    Deterministic = 4,
    /// <summary>不暴露为给模型的 Function Calling schema（防递归，尤其 llm_chat）。</summary>
    NotExposedToModel = 8,
}

/// <summary>语言无关工具描述 —— CSG 数据面「配置」投影的一个能力节点。</summary>
public sealed record ToolDescriptor(
    string Id,          // 全局唯一：tool:{backend}:{name}，如 tool:llm_chat / tool:guard_input
    string Name,        // 工具名：llm_chat / guard_input
    string Backend,     // ToolBackend
    int Version,
    string Description,
    IReadOnlyDictionary<string, object?> InputSchema,
    IReadOnlyDictionary<string, object?> OutputSchema,
    ToolFlags Flags)
{
    /// <summary>编译缓存/热加载指纹（P0 简化：id+backend+version 哈希）。</summary>
    public string Fingerprint =>
        Hash($"{Id}|{Backend}|{Version}");

    private static string Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (var c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h.ToString("x16");
    }
}