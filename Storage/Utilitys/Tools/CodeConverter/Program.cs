using CodeConverter.Rewriters;
using Microsoft.CodeAnalysis;
using RoslynSpace.RoslynStringRefactoringFramework;


namespace CodeConverter
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  CodeConverter - 代码转换工具");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var mode = "all";
            var targetDir = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\CentralConsole\BasicMethod.Tests\Logging.Tests"));

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-mode":
                    case "--mode":
                        if (i + 1 < args.Length)
                            mode = args[++i].ToLowerInvariant();
                        break;
                    case "-dir":
                    case "--dir":
                    case "-target":
                    case "--target":
                        if (i + 1 < args.Length)
                            targetDir = Path.GetFullPath(args[++i]);
                        break;
                    case "-h":
                    case "--help":
                        ShowHelp();
                        return;
                }
            }

            if (!Directory.Exists(targetDir))
            {
                Console.WriteLine($"错误：目标目录不存在: {targetDir}");
                Console.WriteLine("使用 --help 查看帮助信息。");
                return;
            }

            Console.WriteLine($"目标目录: {targetDir}");
            Console.WriteLine($"运行模式: {GetModeDisplayName(mode)}");
            Console.WriteLine();

            var csFiles = Directory.GetFiles(targetDir, "*.cs", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {csFiles.Length} 个 .cs 文件");
            Console.WriteLine();

            var framework = new RoslynRefactoringFramework();
            var rules = new List<(RefactoringRule rule, Func<SyntaxNode, int> getCount)>();

            if (mode is "all" or "logcategory")
            {
                var logRule = new LogCategoryReplacementRule();
                framework.AddRule(logRule);
                rules.Add((logRule, logRule.GetReplacementCount));
            }

            if (mode is "all" or "storage")
            {
                var storageRule = new StorageApiReplacementRule();
                framework.AddRule(storageRule);
                rules.Add((storageRule, storageRule.GetReplacementCount));
            }

            if (rules.Count == 0)
            {
                Console.WriteLine($"错误：未知模式 '{mode}'。可用模式: all, logcategory, storage");
                return;
            }

            int totalReplacements = 0;
            int processedFiles = 0;
            int errorFiles = 0;

            foreach (var filePath in csFiles)
            {
                var result = await ProcessFileAsync(filePath, framework, rules);
                if (result.Success)
                {
                    processedFiles++;
                    totalReplacements += result.ReplacementCount;
                    if (result.ReplacementCount > 0)
                    {
                        Console.WriteLine($"  ✓ {GetRelativePath(targetDir, filePath)} ({result.ReplacementCount} 处替换)");
                    }
                }
                else
                {
                    errorFiles++;
                    Console.WriteLine($"  ✗ {GetRelativePath(targetDir, filePath)}: {result.ErrorMessage}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine($"  处理完成: {processedFiles} 个文件");
            Console.WriteLine($"  总替换数: {totalReplacements} 处");
            Console.WriteLine($"  错误文件: {errorFiles} 个");
            Console.WriteLine("========================================");
        }

        static void ShowHelp()
        {
            Console.WriteLine("用法: CodeConverter [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  -mode <模式>     运行模式: all (默认), logcategory, storage");
            Console.WriteLine("  -dir <路径>      目标目录 (默认: Logging.Tests)");
            Console.WriteLine("  -h, --help       显示帮助信息");
            Console.WriteLine();
            Console.WriteLine("模式说明:");
            Console.WriteLine("  all          执行所有规则 (LogCategory + Storage API)");
            Console.WriteLine("  logcategory  仅执行 LogCategory 替换规则");
            Console.WriteLine("  storage      仅执行 Storage API 替换规则");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  CodeConverter");
            Console.WriteLine("  CodeConverter -mode storage");
            Console.WriteLine("  CodeConverter -mode logcategory -dir D:\\Projects\\MyTests");
        }

        static string GetModeDisplayName(string mode)
        {
            return mode switch
            {
                "all" => "全部规则 (LogCategory + Storage API)",
                "logcategory" => "仅 LogCategory 替换",
                "storage" => "仅 Storage API 替换",
                _ => mode
            };
        }

        static async Task<FileProcessResult> ProcessFileAsync(
            string filePath,
            RoslynRefactoringFramework framework,
            List<(RefactoringRule rule, Func<SyntaxNode, int> getCount)> rules)
        {
            try
            {
                var sourceCode = await File.ReadAllTextAsync(filePath);

                var context = new CodeRefactoringContext(sourceCode);
                var result = await framework.RefactorAsync(context);

                if (!result.Success)
                {
                    return new FileProcessResult
                    {
                        Success = false,
                        ErrorMessage = result.Error?.Message ?? "重构失败"
                    };
                }

                var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceCode);
                var root = syntaxTree.GetRoot();
                var totalCount = rules.Sum(r => r.getCount(root));

                if (totalCount > 0)
                {
                    await File.WriteAllTextAsync(filePath, result.TransformedCode);
                }

                return new FileProcessResult
                {
                    Success = true,
                    ReplacementCount = totalCount
                };
            }
            catch (Exception ex)
            {
                return new FileProcessResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        static string GetRelativePath(string basePath, string fullPath)
        {
            var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? basePath
                : basePath + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString()
                .Replace('/', Path.DirectorySeparatorChar));
        }
    }

    internal class FileProcessResult
    {
        public bool Success { get; set; }
        public int ReplacementCount { get; set; }
        public string? ErrorMessage { get; set; }
    }
}


