using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tools.ClearCSharpComments;

internal static class Program
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "_deprecated", "backups", "generated", "node_modules", ".venv"
    };

    private static readonly HashSet<SyntaxKind> CommentKinds = new()
    {
        SyntaxKind.SingleLineCommentTrivia,
        SyntaxKind.MultiLineCommentTrivia,
        SyntaxKind.SingleLineDocumentationCommentTrivia,
        SyntaxKind.MultiLineDocumentationCommentTrivia
    };

    private static int Main(string[] args)
    {
        string root = ".";
        bool whatIf = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-path":
                case "--path":
                    if (i + 1 < args.Length)
                    {
                        root = args[++i];
                    }

                    break;
                case "-whatif":
                case "--whatif":
                    whatIf = true;
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
            }
        }

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"[ERROR] 目录不存在: {root}");
            return 2;
        }

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int total = 0;
        int changed = 0;
        int unchanged = 0;
        int failed = 0;
        int commentsRemoved = 0;
        var failures = new List<string>();

        foreach (var file in files)
        {
            total++;
            var result = ProcessFile(file, whatIf);
            switch (result.Status)
            {
                case FileStatus.Changed:
                    changed++;
                    commentsRemoved += result.CommentsRemoved;
                    break;
                case FileStatus.Unchanged:
                    unchanged++;
                    break;
                default:
                    failed++;
                    failures.Add($"{file}: {result.Message}");
                    break;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"总文件数: {total}");
        Console.WriteLine($"已修改: {changed}");
        Console.WriteLine($"未变更: {unchanged}");
        Console.WriteLine($"失败: {failed}");
        Console.WriteLine($"移除注释数: {commentsRemoved}");
        if (whatIf)
        {
            Console.WriteLine("(WhatIf 模式: 未写入任何文件)");
        }

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("失败列表:");
            foreach (var failure in failures)
            {
                Console.WriteLine($"  {failure}");
            }
        }

        return failed > 0 ? 1 : 0;
    }

    private static FileResult ProcessFile(string path, bool whatIf)
    {
        var (encoding, text) = ReadFileWithEncoding(path);
        var tree = CSharpSourceProvider.Instance.Parse(text, path);

        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return FileResult.Failed("语法错误, 跳过");
        }

        var root = tree.GetRoot();
        int count = root.DescendantTrivia(descendIntoTrivia: true).Count(t => CommentKinds.Contains(t.Kind()));
        if (count == 0)
        {
            return FileResult.Unchanged;
        }

        var newRoot = new CommentRemovalRewriter().Visit(root);
        var newText = newRoot.ToFullString();
        if (newText == text)
        {
            return FileResult.Unchanged;
        }

        if (!whatIf)
        {
            File.WriteAllText(path, newText, encoding);
        }

        return FileResult.Changed(count);
    }

    private static (Encoding Encoding, string Text) ReadFileWithEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
        }
        else
        {
            encoding = new UTF8Encoding(false);
        }

        return (encoding, encoding.GetString(bytes));
    }

    private static bool IsExcluded(string fullPath)
    {
        string[] segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => ExcludedDirs.Contains(s));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: ClearCSharpComments -path <根目录> [-whatif] [-h]");
        Console.WriteLine("  -path     C# 项目根目录 (默认: 当前目录)");
        Console.WriteLine("  -whatif   预览模式: 仅统计修改, 不写入文件");
        Console.WriteLine("  -h        显示帮助");
    }

    private enum FileStatus
    {
        Changed,
        Unchanged,
        Failed
    }

    private sealed class FileResult
    {
        public FileStatus Status { get; init; }
        public int CommentsRemoved { get; init; }
        public string Message { get; init; } = "";

        public static FileResult Changed(int comments) => new() { Status = FileStatus.Changed, CommentsRemoved = comments };

        public static FileResult Unchanged => new() { Status = FileStatus.Unchanged };

        public static FileResult Failed(string message) => new() { Status = FileStatus.Failed, Message = message };
    }

    private sealed class CommentRemovalRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            SyntaxTriviaList newLeading = ProcessTokenTrivia(token.LeadingTrivia, isTrailing: false);
            SyntaxTriviaList newTrailing = ProcessTokenTrivia(token.TrailingTrivia, isTrailing: true);
            return base.VisitToken(token.WithLeadingTrivia(newLeading).WithTrailingTrivia(newTrailing));
        }

        private static SyntaxTriviaList ProcessTokenTrivia(SyntaxTriviaList list, bool isTrailing)
        {
            if (list.Count == 0)
            {
                return list;
            }

            var items = list.ToArray();
            var result = new List<SyntaxTrivia>(list.Count);

            for (int i = 0; i < items.Length; i++)
            {
                SyntaxTrivia trivia = items[i];
                if (!CommentKinds.Contains(trivia.Kind()))
                {
                    result.Add(trivia);
                    continue;
                }

                if (isTrailing)
                {
                    if (result.Count > 0 && result[^1].IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        result.RemoveAt(result.Count - 1);
                    }

                    continue;
                }

                RemoveLineLeadingIndent(result);
                if (i + 1 < items.Length && items[i + 1].IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    i++;
                }
                else if (i + 2 < items.Length
                         && items[i + 1].IsKind(SyntaxKind.WhitespaceTrivia)
                         && items[i + 2].IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    i += 2;
                }
            }

            return SyntaxFactory.TriviaList(result);
        }

        private static void RemoveLineLeadingIndent(List<SyntaxTrivia> list)
        {
            if (list.Count == 0 || !list[^1].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return;
            }

            bool isLineLeading = list.Count == 1 || list[^2].IsKind(SyntaxKind.EndOfLineTrivia);
            if (isLineLeading)
            {
                list.RemoveAt(list.Count - 1);
            }
        }
    }
}