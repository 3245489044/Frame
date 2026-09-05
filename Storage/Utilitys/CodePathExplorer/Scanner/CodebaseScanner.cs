using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace CodePathExplorer.Scanner;

public class CodebaseScanner
{
    private static readonly string[] ExcludedDirectories =
    {
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}"
    };

    private readonly SolutionProjectScanner _solutionScanner = new();

    public ScanResult Scan(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"目录不存在: {directoryPath}");

        var solutionResult = _solutionScanner.Scan(directoryPath);

        if (solutionResult.IsSolutionMode && solutionResult.ProjectContexts.Count > 0)
        {
            return BuildScanResultFromSolution(directoryPath, solutionResult);
        }

        return ScanDirectoryLegacy(directoryPath);
    }

    public ScanResult ScanSolution(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"解决方案文件不存在: {solutionPath}");

        var solutionResult = _solutionScanner.Scan(solutionPath);
        var slnDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        return BuildScanResultFromSolution(slnDir, solutionResult);
    }

    private ScanResult BuildScanResultFromSolution(string directoryPath, SolutionScanResult solutionResult)
    {
        var allContexts = new List<AnalysisContext>();
        var allErrors = new List<string>();
        var totalFiles = 0;
        var parsedFiles = 0;

        foreach (var projectContext in solutionResult.ProjectContexts)
        {
            totalFiles += projectContext.Project.SourceFiles.Count;
            parsedFiles += projectContext.FileContexts.Count;
            allContexts.AddRange(projectContext.FileContexts);
            allErrors.AddRange(projectContext.CompilationErrors);
        }

        return new ScanResult
        {
            DirectoryPath = directoryPath,
            TotalFiles = totalFiles,
            ParsedFiles = parsedFiles,
            Contexts = allContexts,
            Errors = allErrors,
            IsSolutionMode = true,
            SolutionPath = solutionResult.SolutionPath,
            Projects = solutionResult.Projects,
            ProjectContexts = solutionResult.ProjectContexts
        };
    }

    private ScanResult ScanDirectoryLegacy(string directoryPath)
    {
        var csFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ExcludedDirectories.Any(d => f.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var contexts = new List<AnalysisContext>();
        var errors = new List<string>();

        var basicReferences = new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(Uri).Assembly.Location,
            typeof(System.Text.StringBuilder).Assembly.Location,
        };

        var metadataReferences = basicReferences
            .Select(r => MetadataReference.CreateFromFile(r))
            .ToArray();

        foreach (var file in csFiles)
        {
            try
            {
                var code = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                var compilation = CSharpCompilation.Create("Temp")
                    .AddSyntaxTrees(tree)
                    .AddReferences(metadataReferences)
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                var semanticModel = compilation.GetSemanticModel(tree);

                var context = new AnalysisContext
                {
                    FilePath = file,
                    SourceCode = code,
                    SyntaxTree = tree,
                    Root = root,
                    SemanticModel = semanticModel
                };

                contexts.Add(context);
            }
            catch (Exception ex)
            {
                errors.Add($"解析失败: {file} - {ex.Message}");
            }
        }

        return new ScanResult
        {
            DirectoryPath = directoryPath,
            TotalFiles = csFiles.Count,
            ParsedFiles = contexts.Count,
            Contexts = contexts,
            Errors = errors,
            IsSolutionMode = false
        };
    }
}

public class ScanResult
{
    public string DirectoryPath { get; set; } = "";

    public int TotalFiles { get; set; }

    public int ParsedFiles { get; set; }

    public List<AnalysisContext> Contexts { get; set; } = new();

    public List<string> Errors { get; set; } = new();


    public bool IsSolutionMode { get; set; }

    public string? SolutionPath { get; set; }

    public List<ProjectInfo> Projects { get; set; } = new();

    public List<ProjectCompilationContext> ProjectContexts { get; set; } = new();
}
