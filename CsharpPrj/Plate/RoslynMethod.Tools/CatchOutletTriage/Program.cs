using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tools.CatchOutletTriage;

/// <summary>
/// catch 出口盘点工具（M3 通用框架「解析内核」）—— 复用 <see cref="CSharpSourceProvider"/>（唯一解析落点）
/// + <see cref="QuerySkeleton"/>（有界查询）+ <see cref="RoslynSourceLocator"/>（位置换算），
/// 对给定文件清单做 AST 级 catch 块提取与出口分类（屏蔽注释/字符串/格式漂移），输出机读 JSON。
/// PS 侧 Invoke-CatchTriage.ps1 消费（PS 负责扫描/哈希/登记/棘轮，本工具只做分类）。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string filesList = null;
        string outPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-files":
                case "--files":
                    if (i + 1 < args.Length) filesList = args[++i];
                    break;
                case "-out":
                case "--out":
                    if (i + 1 < args.Length) outPath = args[++i];
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(filesList) || string.IsNullOrWhiteSpace(outPath))
        {
            PrintUsage();
            return 2;
        }
        if (!File.Exists(filesList))
        {
            Console.Error.WriteLine($"[ERROR] 文件清单不存在: {filesList}");
            return 2;
        }

        var provider = CSharpSourceProvider.Instance;
        var items = new List<object>();
        int scanned = 0;
        int failed = 0;

        foreach (var raw in File.ReadAllLines(filesList))
        {
            var path = raw.Trim();
            if (path.Length == 0 || !File.Exists(path)) continue;
            scanned++;

            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] 读取失败 {path}: {ex.Message}");
                failed++;
                continue;
            }

            SyntaxNode root;
            try { root = provider.GetRoot(provider.Parse(text, path)); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] 解析失败 {path}: {ex.Message}");
                failed++;
                continue;
            }

            IEnumerable<CatchClauseSyntax> catches;
            try { catches = QuerySkeleton.FindAllNodes<CatchClauseSyntax>(root); }
            catch { catches = root.DescendantNodes().OfType<CatchClauseSyntax>(); }

            foreach (var c in catches)
            {
                int startLine = RoslynSourceLocator.GetLineNumber(c);
                if (startLine <= 0) continue;
                var span = c.GetLocation().GetLineSpan();
                var exType = c.Declaration != null ? c.Declaration.Identifier.ValueText : string.Empty;
                items.Add(new
                {
                    file = path,
                    startLine,
                    endLine = span.EndLinePosition.Line + 1,
                    exception = exType,
                    cls = Classify(c),
                });
            }
        }

        var payload = new { schema = "catch-triage-raw-v1", items };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"[CatchOutletTriage] 文件 {scanned}（失败 {failed}）· catch 块 {items.Count} → {outPath}");
        return 0;
    }

    /// <summary>出口分类（与 PS 内核 CatchOutlet.psm1 同口径；AST 级，无文本漂移）。</summary>
    private static string Classify(CatchClauseSyntax c)
    {
        bool hasThrow = false, hasHandle = false, hasBudget = false, hasDegrade = false, hasLog = false;
        foreach (var n in c.DescendantNodes())
        {
            switch (n)
            {
                case ThrowStatementSyntax:
                    hasThrow = true;
                    break;
                case IdentifierNameSyntax id when id.Identifier.ValueText == "FailureBudget":
                    hasBudget = true;
                    break;
                case InvocationExpressionSyntax inv:
                    var name = inv.Expression.ToString();
                    if (name.EndsWith("handle", StringComparison.Ordinal)) hasHandle = true;
                    else if (name.IndexOf("egrade", StringComparison.Ordinal) >= 0) hasDegrade = true;
                    else if (name.IndexOf("Log", StringComparison.Ordinal) >= 0) hasLog = true;
                    break;
                case MemberAccessExpressionSyntax ma when ma.ToString().IndexOf("Log", StringComparison.Ordinal) >= 0:
                    hasLog = true;
                    break;
            }
        }
        if (hasThrow) return "rethrow";
        if (hasHandle) return "handle";
        if (hasBudget) return "budget";
        if (hasDegrade) return "degrade";
        if (hasLog) return "candidate-logonly";
        return "candidate-empty";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: CatchOutletTriage -files <文件清单> -out <JSON输出> [-h]");
        Console.WriteLine("  -files  文本文件：每行一个 .cs 绝对路径（PS 侧扫描后传入）");
        Console.WriteLine("  -out    JSON 输出路径（schema=catch-triage-raw-v1）");
        Console.WriteLine("  -h      显示帮助");
    }
}
