using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>供应商 usage 标准化端口：原始 usage JSON → 统一字段。</summary>
public interface IUsageNormalizer
{
    UnifiedUsage Normalize(string model, JsonElement usage);
}