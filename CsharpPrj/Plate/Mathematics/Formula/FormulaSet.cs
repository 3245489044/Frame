using System.Text.Json;
using System.Text.Json.Serialization;

namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式文件根：schema + generatedAt + formulas[]。
/// <para>写出前统一排序并重算签名；加载时校验 schema 与签名，损坏/漂移即抛。</para>
/// </summary>
public sealed record FormulaSet
{
    public const string CurrentSchema = "metric-formula-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Schema { get; init; } = CurrentSchema;

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<FormulaDefinition> Formulas { get; init; } = Array.Empty<FormulaDefinition>();

    public static FormulaSet Create(
        IEnumerable<FormulaDefinition> formulas,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(formulas);

        FormulaDefinition[] normalized = formulas
            .Select(static formula => formula.Normalize())
            .OrderBy(static formula => formula.Id, StringComparer.Ordinal)
            .ToArray();

        return new FormulaSet
        {
            Schema = CurrentSchema,
            GeneratedAt = generatedAt ?? DateTimeOffset.UtcNow,
            Formulas = normalized,
        };
    }

    public FormulaDefinition? Find(string id)
        => (Formulas ?? Array.Empty<FormulaDefinition>())
            .FirstOrDefault(formula => string.Equals(formula.Id, id, StringComparison.Ordinal));

    public static FormulaSet Load(string path, bool verifySignatures = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        FormulaSet set = JsonSerializer.Deserialize<FormulaSet>(json, JsonOptions)
            ?? throw new InvalidOperationException($"公式文件为空或无法解析：{path}");

        if (!string.Equals(set.Schema, CurrentSchema, StringComparison.Ordinal))
            throw new InvalidOperationException($"公式 schema 不匹配：{set.Schema}，期望 {CurrentSchema}。");

        IReadOnlyList<FormulaDefinition> formulas = set.Formulas ?? Array.Empty<FormulaDefinition>();
        var normalized = new List<FormulaDefinition>(formulas.Count);
        foreach (FormulaDefinition formula in formulas)
        {
            formula.Validate();
            long computed = FormulaSignature.Compute(formula);
            if (verifySignatures && formula.Signature != computed)
            {
                throw new InvalidOperationException(
                    $"公式 {formula.Id} 签名不一致：文件 0x{formula.Signature:X16}，计算 0x{computed:X16}。");
            }

            normalized.Add(formula.Normalize());
        }

        return set with
        {
            Formulas = normalized
                .OrderBy(static formula => formula.Id, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        FormulaSet normalized = this with
        {
            Formulas = (Formulas ?? Array.Empty<FormulaDefinition>())
                .Select(static formula => formula.Normalize())
                .OrderBy(static formula => formula.Id, StringComparer.Ordinal)
                .ToArray(),
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
