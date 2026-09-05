using System.Text.Json;

namespace CallCenter.Mcp;

/// <summary>
/// MCP 调用结果文本 → 上层值的宽容封包（TH-9 单一实现，C1 回归锚点）。
/// 统一客户端已把 MCP content 提取为文本；本类决定该文本如何呈给上层：
/// 可解析 JSON → 结构化值（JsonElement / string / number 等）；否则 → 原文 string。
/// 调用方（如 Agent 工作流函数面 mcp_call_tool）经此输出统一 result 语义，避免各端点自写「解析/兜底」分叉。
/// </summary>
public static class McpResult
{
    /// <summary>按 TH-9 宽容处理：可解析 JSON → 反序列化结果；解析失败（非 JSON 文本）→ 原样字符串。</summary>
    public static object? CoerceStructured(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(text);
        }
        catch
        {
            // 非 JSON 文本（如工具直接输出人类可读/任意文本）→ 以原文呈现，不做错误化。
            return text;
        }
    }
}
