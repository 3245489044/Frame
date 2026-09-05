using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plate.HttpMethod.OpenAI
{
    /// <summary>
    /// OpenAI Function Calling 工具定义（通用 wire 层）。执行器 <see cref="ExecuteAsync"/> 由消费方注入
    /// （入参 = LLM 生成的 arguments JSON，返回 = 结果文本/JSON），wire 层不依赖任何领域契约。
    /// </summary>
    public sealed record OpenAiTool(
        string Name,
        string Description,
        Func<string, Task<string>> ExecuteAsync,
        JsonElement? InputSchema = null);
}
