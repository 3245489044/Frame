using CodePathExplorer.Export;
using CodePathExplorer.Explorers;
using CodePathExplorer.Models;
using CodePathExplorer.Scanner;

namespace CodePathExplorer;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    CodePathExplorer v1.0                            ║");
        Console.WriteLine("║          基于 RoslynMethod 的代码路径分析工具                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var parsedArgs = ParseArguments(args);
        string targetPath = parsedArgs.TargetPath ?? "";
        string? solutionPath = parsedArgs.SolutionPath;
        string? logOutputDir = parsedArgs.LogOutputDir;
        string? batchId = parsedArgs.BatchId;
        string? markdownOutput = parsedArgs.MarkdownOutput;
        bool noMarkdown = parsedArgs.NoMarkdown;
        bool noInteractive = parsedArgs.NoInteractive;


        if (string.IsNullOrEmpty(targetPath) && string.IsNullOrEmpty(solutionPath))
        {
            Console.Write("请输入要分析的代码库路径: ");
            targetPath = Console.ReadLine()?.Trim() ?? "";
        }

        var scanner = new CodebaseScanner();
        ScanResult scanResult;

        if (!string.IsNullOrEmpty(solutionPath))
        {
            if (!File.Exists(solutionPath))
            {
                Console.WriteLine($"错误: 解决方案文件不存在: {solutionPath}");
                return;
            }
            Console.WriteLine($"正在扫描解决方案: {solutionPath}");
            Console.WriteLine();
            scanResult = scanner.ScanSolution(solutionPath);
        }
        else
        {
            if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
            {
                Console.WriteLine($"错误: 路径不存在或无效: {targetPath}");
                return;
            }
            Console.WriteLine($"正在扫描: {targetPath}");
            Console.WriteLine();
            scanResult = scanner.Scan(targetPath);
        }

        Console.WriteLine($"  发现 {scanResult.TotalFiles} 个 .cs 文件，成功解析 {scanResult.ParsedFiles} 个");

        if (scanResult.IsSolutionMode && scanResult.Projects.Count > 0)
        {
            Console.WriteLine($"  扫描模式: 解决方案/项目模式 (跨项目分析)");
            Console.WriteLine($"  项目数量: {scanResult.Projects.Count}");
            foreach (var proj in scanResult.Projects)
            {
                Console.WriteLine($"    📦 {proj.Name} ({proj.SourceFiles.Count} 个文件, {proj.ProjectReferences.Count} 个引用)");
            }
        }

        if (scanResult.Errors.Count > 0)
        {
            Console.WriteLine($"  解析错误: {scanResult.Errors.Count} 个");
            foreach (var err in scanResult.Errors.Take(5))
                Console.WriteLine($"    ⚠ {err}");
        }

        Console.WriteLine();

        var result = new AnalysisResult
        {
            SourcePath = targetPath,
            FileCount = scanResult.ParsedFiles,
        };

        var allClasses = new HashSet<string>();

        foreach (var context in scanResult.Contexts)
        {
            try
            {
                var classes = context.Root.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();
                foreach (var cls in classes)
                    allClasses.Add(cls.Identifier.Text);

                var callExplorer = new CallChainExplorer(context);
                var chains = callExplorer.ExploreAll();
                result.CallChains.AddRange(chains);

                var fieldExplorer = new FieldFlowExplorer(context);
                var flows = fieldExplorer.ExploreAll();
                result.FieldFlowSets.AddRange(flows);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  分析文件失败: {context.FilePath} - {ex.Message}");
            }
        }

        result.ClassCount = allClasses.Count;

        var simplifier = new SimplificationAnalyzer();
        result.Suggestions = simplifier.Analyze(result);

        var printer = new ChainPrinter();
        var report = printer.PrintReport(result);
        Console.WriteLine(report);

        if (!noMarkdown)
        {
            string markdownPath;
            if (!string.IsNullOrEmpty(markdownOutput))
            {
                if (Directory.Exists(markdownOutput) || !Path.HasExtension(markdownOutput))
                {
                    Directory.CreateDirectory(markdownOutput);
                    markdownPath = Path.Combine(markdownOutput, "CodePathExplorer_Report.md");
                }
                else
                {
                    var dir = Path.GetDirectoryName(markdownOutput);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    markdownPath = markdownOutput;
                }
            }
            else
            {
                markdownPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                    "CodePathExplorer_Report.md");
            }

            var markdownExporter = new MarkdownExporter();
            var markdown = markdownExporter.ExportToMarkdown(result);
            File.WriteAllText(markdownPath, markdown);
            Console.WriteLine($"  Markdown 报告已导出: {markdownPath}");
        }

        if (!string.IsNullOrEmpty(logOutputDir))
        {
            if (string.IsNullOrEmpty(batchId))
            {
                batchId = LogExporter.GenerateBatchId();
            }

            var logExporter = new LogExporter();
            var logPath = logExporter.ExportToLog(result, logOutputDir, batchId);
            Console.WriteLine($"  分析日志已导出: {logPath}");
        }

        Console.WriteLine();
        if (!noInteractive)
        {
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    private static ParsedArguments ParseArguments(string[] args)
    {
        var result = new ParsedArguments();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--log-output":
                    if (i + 1 < args.Length)
                    {
                        result.LogOutputDir = args[++i];
                    }
                    break;

                case "--batch-id":
                    if (i + 1 < args.Length)
                    {
                        result.BatchId = args[++i];
                    }
                    break;

                case "--markdown-output":
                    if (i + 1 < args.Length)
                    {
                        result.MarkdownOutput = args[++i];
                    }
                    break;

                case "--no-markdown":
                    result.NoMarkdown = true;
                    break;

                case "--solution":
                    if (i + 1 < args.Length)
                    {
                        result.SolutionPath = args[++i];
                    }
                    break;

                case "--no-interactive":
                    result.NoInteractive = true;
                    break;

                default:
                    if (string.IsNullOrEmpty(result.TargetPath) && !args[i].StartsWith("--"))
                    {
                        result.TargetPath = args[i];
                    }
                    break;
            }
        }
        return result;
    }

    private class ParsedArguments
    {
        public string? TargetPath { get; set; }
        public string? SolutionPath { get; set; }
        public string? LogOutputDir { get; set; }
        public string? BatchId { get; set; }
        public string? MarkdownOutput { get; set; }
        public bool NoMarkdown { get; set; }
        public bool NoInteractive { get; set; }
    }
}


