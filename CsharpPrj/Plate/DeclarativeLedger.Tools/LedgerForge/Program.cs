using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 声明式台账**离线写入器**（F2-a）：数据 / 逻辑分离 —— 路径 / 分组规则 / 预期键数 / 文案一律读
/// <c>Split-SpecPortLedger.data.json</c>（与既有 PowerShell 工具**同一真源**）；本件零端口键、零领域词。
/// <para>
/// 本批落地 <c>check</c>（只读语义对拍）。写侧（<c>compose</c> / <c>split</c> / <c>set-impl</c>）按方案 R-1
/// 先只读对拍、再替换（判据 = 与 PS 产出逐字节一致 + 幂等 + 原文保真）。
/// </para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var (command, data) = Cli.Parse(args);
            return command switch
            {
                "check" => CheckCommand.Run(LedgerView.Load(data)),
                "help" or "--help" or "-h" or "" => Help(),
                _ => Fail($"未知命令 '{command}'（可用：check | help）"),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[ledger:forge] {exception.GetType().Name}: {exception.Message}");
            return 2;
        }
    }

    private static int Help()
    {
        Console.WriteLine("LedgerForge —— 声明式台账离线写入器（离线工具，非运行时面）");
        Console.WriteLine();
        Console.WriteLine("  check [--data <data.json>]   只读语义对拍：分片重组结果 ↔ 台账产物");
        Console.WriteLine("                               （键集相等 ∧ 逐键规范化文本相等 ∧ 非 ports 元数据段相等；键序不参与比较）");
        Console.WriteLine("  help                          本帮助");
        Console.WriteLine();
        Console.WriteLine("  --data 缺省 = 自程序集位置向上定位 Environment\\CentralTest\\Tools\\Split-SpecPortLedger.data.json");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"[ledger:forge] {message}");
        return 1;
    }

    /// <summary>命令行解析（无第三方依赖；未知选项即抛）。</summary>
    private static class Cli
    {
        internal static (string Command, string? Data) Parse(string[] args)
        {
            var command = string.Empty;
            string? data = null;

            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                if (arg is "--data" or "-d")
                {
                    if (index + 1 >= args.Length) throw new ArgumentException("--data 缺少取值");
                    data = args[++index];
                    continue;
                }

                if (arg.StartsWith('-'))
                    throw new ArgumentException($"未知选项 '{arg}'（可用：--data <path>）");

                if (command.Length != 0) throw new ArgumentException($"只接受一个命令（实得 '{command}' 与 '{arg}'）");
                command = arg;
            }

            return (command, data);
        }
    }
}
