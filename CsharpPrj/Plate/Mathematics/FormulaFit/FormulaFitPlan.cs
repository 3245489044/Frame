using System.Text.Json;
using System.Text.Json.Serialization;

namespace MathematicalAlgorithms.FormulaFit;

/// <summary>拟合计划根：目标列表 + 窗口条件。</summary>
public sealed record FormulaFitPlan
{
    public const string CurrentSchema = "formula-fit-plan-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Schema { get; init; } = CurrentSchema;

    public string PlanId { get; init; } = "default";

    public FormulaFitPlanWindow Window { get; init; } =
        new(string.Empty, string.Empty, new Dictionary<string, string>());

    public IReadOnlyList<FormulaFitTargetPlan> Targets { get; init; } =
        Array.Empty<FormulaFitTargetPlan>();

    public static FormulaFitPlan Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        FormulaFitPlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<FormulaFitPlan>(json, JsonOptions)
                ?? throw new InvalidOperationException($"计划文件为空：{path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"计划文件解析失败：{path}：{ex.Message}", ex);
        }

        return plan.Validate();
    }

    public FormulaFitPlan Validate()
    {
        if (!string.Equals(Schema, CurrentSchema, StringComparison.Ordinal))
            throw new InvalidOperationException($"计划 schema 不匹配：{Schema}，期望 {CurrentSchema}。");

        if (Targets is null || Targets.Count == 0)
            throw new InvalidOperationException("计划 targets 不能为空。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormulaFitTargetPlan target in Targets)
        {
            target.Validate();
            if (!ids.Add(target.Id))
                throw new InvalidOperationException($"计划 target id 重复：{target.Id}。");
        }

        return this;
    }
}

/// <summary>计划窗口：时间范围 + 静态条件。</summary>
public sealed record FormulaFitPlanWindow(
    string From,
    string To,
    IReadOnlyDictionary<string, string> Conditions)
{
    public IReadOnlyDictionary<string, string> NormalizedConditions
    {
        get
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (Conditions is not null)
            {
                foreach (KeyValuePair<string, string> pair in Conditions)
                    sorted[pair.Key] = pair.Value;
            }

            return sorted;
        }
    }
}
