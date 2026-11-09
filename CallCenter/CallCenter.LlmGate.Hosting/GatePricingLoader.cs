using System.Globalization;
using System.Text.Json;
using CallCenter.Cost.Pricing;

namespace CallCenter.LlmGate.Hosting;

/// <summary>价格表装载（格式与 Projects/Plug/Agent/Config/cost/pricing.json 对齐）。</summary>
public static class GatePricingLoader
{
    /// <summary>缺件 ⇒ 空表（所有模型无定价 ⇒ 人工闸 Hold；不静默按 0）。坏件抛异常。</summary>
    public static DeclaredPriceTable Load(string? path, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            log?.Invoke($"[llm-gate] 价格表缺失：{path ?? "(未配置)"}；所有模型将按「无定价」转人工。");
            return DeclaredPriceTable.Empty;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var quotes = new List<PriceQuote>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var provider = GetString(element, "provider");
            var model = GetString(element, "model");
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidDataException($"pricing.json 条目缺 provider/model：{element.GetRawText()}");
            }

            var effective = GetString(element, "effectiveDate") ?? "1970-01-01";
            quotes.Add(new PriceQuote(
                provider!,
                model!,
                DateOnly.ParseExact(effective, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                GetString(element, "currency") ?? "USD",
                GetDecimal(element, "inputPerToken") ?? 0m,
                GetDecimal(element, "cachedInputPerToken"),
                GetDecimal(element, "cacheWritePerToken"),
                GetDecimal(element, "outputPerToken") ?? 0m,
                Tiers: null,
                CpuPerSecond: GetDecimal(element, "cpuPerSecond"),
                MemoryPerGbSecond: GetDecimal(element, "memoryPerGbSecond"),
                DiskPerGbWrite: GetDecimal(element, "diskPerGbWrite"),
                DiskPerGb: GetDecimal(element, "diskPerGb"),
                PerCallPrice: GetDecimal(element, "perCallPrice"),
                CacheDiscount: GetDecimal(element, "cacheDiscount")));
        }

        return new DeclaredPriceTable(quotes);
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetDecimal(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
                ? number
                : null;
}
