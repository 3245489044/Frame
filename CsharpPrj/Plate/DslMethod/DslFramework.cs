using System.Reflection;
using Plate.DslMethod.Abstractions;
using Plate.DslMethod.CodeGen;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Plate.DslMethod;

/// <summary>
/// DSL 框架门面：统一入口。解析 → 装载 → 校验 → 缺口/矩阵分析的组合调用。
/// </summary>
public static class DslFramework
{
    /// <summary>解析 DSL 文本为文档。</summary>
    public static DslDocument ParseText(string text, string sourceFile, string lib)
        => DslParser.Parse(text, sourceFile, lib);

    /// <summary>解析 DSL 文件为文档。</summary>
    public static DslDocument ParseFile(string path, string lib)
        => DslParser.Parse(System.IO.File.ReadAllText(path), path, lib);

    /// <summary>解析并装载契约注册中心（各文件显式指定 lib）。</summary>
    public static ContractRegistry BuildRegistry(params (string Path, string Lib)[] files)
    {
        var registry = new ContractRegistry();
        var docs = files.Select(f => ParseFile(f.Path, f.Lib)).ToList();
        registry.Load(docs);
        return registry;
    }

    /// <summary>扫描程序集中的代码公理（[Axiom]）与绑定（[BindAxiom]），并入 markdown [Bind]（可选）。</summary>
    public static AxiomScanner ScanAxioms(params Assembly[] assemblies)
        => new AxiomScanner().Scan(assemblies);

    /// <summary>从 DSL 承载技能正文（SKILL.md，去 front-matter）反转生成公理代码库（Axioms/{Level}/{Name}.cs）。</summary>
    public static List<GeneratedAxiom> GenerateAxioms(IReadOnlyList<string> contractSourceFiles, string outDir, AxiomCodeGeneratorOptions? options = null)
        => new AxiomCodeGenerator().Generate(contractSourceFiles, outDir, options);
}
