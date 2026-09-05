namespace Plate.HttpMethod.OpenAI
{
    /// <summary>OpenAI Function Calling 工具调用项（wire 层通用模型）。</summary>
    public sealed record OpenAiToolCall(string Id, string Name, string Arguments);
}
