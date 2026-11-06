using System.Text.Json;
using Share.Frameworks.Billing;

namespace Share.Frameworks.Llm.Usage;

/// <summary>供应商 usage 标准化端口：原始 usage JSON → 统一字段。</summary>
public interface IUsageNormalizer
{
    UnifiedUsage Normalize(string model, JsonElement usage);
}
