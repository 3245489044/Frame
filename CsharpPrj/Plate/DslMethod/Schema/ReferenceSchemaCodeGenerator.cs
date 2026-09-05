using System.Text;
using System.Text.Json;
using Plate.DslMethod.Parsing;

namespace Plate.DslMethod.Schema;

/// <summary>生成选项：Schema 版本号（缺省读取声明，未声明则 "1.0"）。</summary>
public sealed record ReferenceSchemaCodeGeneratorOptions
{
    public string? Version { get; init; }

    /// <summary>校验失败时抛异常（默认）；为 false 时仅返回 issues，不写产物。</summary>
    public bool ThrowOnInvalid { get; init; } = true;
}

/// <summary>一次生成的产物记录。</summary>
public sealed record ReferenceSchemaGenerationResult(
    string SchemaFile,
    string ReportFile,
    IReadOnlyList<Logic.ValidationIssue> Issues);

/// <summary>
/// 引用 Schema 生成器：读取 DSL 源（单一事实源）→ 校验 → 发射 <c>reference-schema.generated.json</c> 运行时产物
/// + <c>schema-report.txt</c> 映射报告。产物为【纯编译产物】，禁止手改；变更须作用于 DSL 源后重生成。
/// 镜像 <see cref="CodeGen.AxiomCodeGenerator"/> 的反转生成模式。
/// </summary>
public sealed class ReferenceSchemaCodeGenerator
{
    public const string DefaultJsonFile = "reference-schema.generated.json";
    public const string DefaultReportFile = "schema-report.txt";

    public ReferenceSchemaGenerationResult Generate(
        string dslFile,
        string outDir,
        ReferenceSchemaCodeGeneratorOptions? options = null)
    {
        var opts = options ?? new ReferenceSchemaCodeGeneratorOptions();

        var registry = new ReferenceSchemaRegistry();
        registry.Load(new[] { DslParser.Parse(File.ReadAllText(dslFile), dslFile, Path.GetFileNameWithoutExtension(dslFile)) });
        var issues = registry.Validate();

        if (issues.Count > 0)
        {
            if (opts.ThrowOnInvalid)
                throw new InvalidOperationException(
                    "引用 Schema DSL 校验失败（" + dslFile + "）:\n" + string.Join("\n", issues));
            return new ReferenceSchemaGenerationResult("", "", issues);
        }

        var schema = registry.ToSchema();
        if (!string.IsNullOrWhiteSpace(opts.Version)) schema.Version = opts.Version!;

        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, DefaultJsonFile);
        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var report = new List<string>
        {
            $"# 引用 Schema 映射报告（纯编译产物，源: {dslFile}）",
            $"# Version: {schema.Version}",
        };
        foreach (var n in schema.Nodes)
            report.Add($"Node\t{n.Name}\t{n.Display ?? ""}\t{n.SourceFile}:{n.LineNumber}" + (n.Deprecated ? "\tdep" : ""));
        foreach (var e in schema.Edges)
            report.Add($"Edge\t{e.Name}\tsrc={e.Source ?? "any"}\ttgt={e.Target ?? "any"}\tstrength={e.Strength}\t{e.SourceFile}:{e.LineNumber}" + (e.Deprecated ? "\tdep" : ""));
        foreach (var c in schema.Constraints)
            report.Add($"Constraint\t{c.Edge}\tsrcType={c.SrcType}\ttgtTypes={string.Join(",", c.TgtTypes)}\t{c.SourceFile}:{c.LineNumber}");
        var reportPath = Path.Combine(outDir, DefaultReportFile);
        File.WriteAllLines(reportPath, report, Encoding.UTF8);

        return new ReferenceSchemaGenerationResult(jsonPath, reportPath, issues);
    }
}
