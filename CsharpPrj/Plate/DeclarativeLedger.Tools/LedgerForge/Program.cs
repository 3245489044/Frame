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
            var options = Cli.Parse(args);
            return options.Command switch
            {
                "check" => CheckCommand.Run(LedgerView.Load(options.Data), options.Bytes),
                "compose" => ComposeCommand.Run(LedgerView.Load(options.Data), options.Write),
                "split" => SplitCommand.Run(LedgerView.Load(options.Data), options.Write),
                "set-impl" => SetImplCommand.Run(LedgerView.Load(options.Data), options.Impl!, options.Write),
                "normalize-key" => NormalizeKeyCommand.Run(
                    SpecKindView.Load(options.Data), options.Project!, options.Suite, options.Write, options.Check),
                "derive-schema" => DeriveSchemaCommand.Run(LedgerView.Load(options.Data), options.Schemas, options.Write),
                "emit-declaration" => EmitDeclarationCommand.Run(LedgerView.Load(options.Data), options.Family!, options.Out),
                "lint" => LintCommand.Run(LedgerView.Load(options.Data), options.Only),
                "entry-check" => EntryCheckCommand.Run(LedgerView.Load(options.Data), options.Bin!, options.Only, options.Kind),
                "help" or "--help" or "-h" or "" => Help(),
                _ => Fail($"未知命令 '{options.Command}'"
                          + "（可用：check | compose | split | set-impl | normalize-key | derive-schema | emit-declaration | lint | entry-check | help）"),
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
        Console.WriteLine("  check [--bytes] [--data <view>]        只读语义对拍：分片重组 ↔ 台账产物（键序不参与比较）");
        Console.WriteLine("                                         --bytes 追加「产物 + 全部分片」逐字节对拍");
        Console.WriteLine("  compose [--write] [--data <view>]      分片 → 产物；缺省 dry-run（与磁盘产物逐字节比较）");
        Console.WriteLine("  split [--write] [--data <view>]        产物 → 分片；缺省 dry-run（与磁盘分片逐字节比较）");
        Console.WriteLine("  set-impl --impl <data.json> [--write]  impl 区间改写（花括号配平；其余字节逐字保留；缺省 dry-run）");
        Console.WriteLine("  normalize-key --project <工程> [--suite <用例>] [--check] [--write]");
        Console.WriteLine("                                         步骤 kind → 端口键归一（映射源 = 冻结 legacyMap；--check 残留即红）");
        Console.WriteLine("  derive-schema [--schemas <dir>] [--write]");
        Console.WriteLine("                                         词表 → 结构门（schema enum / 键集）；缺省只读对账，--write 幂等写回");
        Console.WriteLine("  emit-declaration --family <fixture.cs> [--out <dir>]");
        Console.WriteLine("                                         声明化骨架草稿 + 缺失落点清单（哨兵 __BIND__；草稿须人工审定）");
        Console.WriteLine("  lint [--only <键前缀>] [--data <view>] 声明体**干跑校验**（不构建）：键位置 / 算子可达 / 槽拼写");
        Console.WriteLine("                                         政策（严重度 / 平铺组 / 不可达算子 / 文案）= 数据视图 lint 段");
        Console.WriteLine("  entry-check --bin <dir> [--only <键前缀>] [--kind <reflective|declared>]");
        Console.WriteLine("                                         产品**入口形态判据**（离线反射 indicative）：public static / 无委托");
        Console.WriteLine("                                         / 非 async / 有返回 / 实参可匹配；政策 = 数据视图 entry 段");
        Console.WriteLine("                                         --kind declared 判「已声明 target/method 但 kind 未切」的条目（预检）");
        Console.WriteLine("  help                                   本帮助");
        Console.WriteLine();
        Console.WriteLine("  开关约束：--bytes 仅 check；--check 仅 normalize-key；--write 不适用于 check / help");
        Console.WriteLine("  --data 缺省 = 自程序集位置向上定位该数据视图（台账 = Split-SpecPortLedger.data.json；");
        Console.WriteLine("            归一 = Convert-SpecKindToPortKey.data.json）");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"[ledger:forge] {message}");
        return 1;
    }

    /// <summary>命令行选项（命令 + 数据视图 + 各命令专属参数与开关）。</summary>
    private sealed record Options(
        string Command, string? Data, bool Write, bool Bytes, bool Check,
        string? Impl, string? Project, string? Suite, string? Schemas, string? Family, string? Out, string? Only,
        string? Bin, string? Kind);

    /// <summary>命令行解析（无第三方依赖；未知选项即抛；命令与开关不匹配即抛）。</summary>
    private static class Cli
    {
        internal static Options Parse(string[] args)
        {
            var command = string.Empty;
            string? data = null, impl = null, project = null, suite = null, schemas = null, family = null, output = null;
            string? only = null;
            string? bin = null;
            string? kind = null;
            var write = false;
            var bytes = false;
            var check = false;

            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                switch (arg)
                {
                    case "--data" or "-d":
                        data = Value(args, ref index, arg);
                        continue;

                    case "--only" or "-o":
                        only = Value(args, ref index, arg);
                        continue;

                    case "--bin":
                        bin = Value(args, ref index, arg);
                        continue;

                    case "--kind" or "-k":
                        kind = Value(args, ref index, arg);
                        continue;

                    case "--impl":
                        impl = Value(args, ref index, arg);
                        continue;

                    case "--project":
                        project = Value(args, ref index, arg);
                        continue;

                    case "--suite":
                        suite = Value(args, ref index, arg);
                        continue;

                    case "--schemas":
                        schemas = Value(args, ref index, arg);
                        continue;

                    case "--family":
                        family = Value(args, ref index, arg);
                        continue;

                    case "--out":
                        output = Value(args, ref index, arg);
                        continue;

                    case "--write" or "-w":
                        write = true;
                        continue;

                    case "--bytes" or "-b":
                        bytes = true;
                        continue;

                    case "--check" or "-c":
                        check = true;
                        continue;
                }

                if (arg.StartsWith('-')) throw new ArgumentException($"未知选项 '{arg}'（见 help）");
                if (command.Length != 0) throw new ArgumentException($"只接受一个命令（实得 '{command}' 与 '{arg}'）");
                command = arg;
            }

            if (bytes && command != "check")
                throw new ArgumentException($"--bytes 仅适用于 check（实得命令 '{command}'）");
            if (only is not null && command is not ("lint" or "entry-check"))
                throw new ArgumentException($"--only 仅适用于 lint / entry-check（实得命令 '{command}'）");
            if (command == "entry-check" && string.IsNullOrWhiteSpace(bin))
                throw new ArgumentException("entry-check 需要 --bin <产品输出目录>");
            if (kind is not null && command != "entry-check")
                throw new ArgumentException($"--kind 仅适用于 entry-check（实得命令 '{command}'）");
            if (kind is not (null or "reflective" or "declared"))
                throw new ArgumentException($"--kind 取值只许 reflective / declared（实得 '{kind}'）");
            if (check && command != "normalize-key")
                throw new ArgumentException($"--check 仅适用于 normalize-key（实得命令 '{command}'）");
            if (write && command is "check" or "help" or "" or "--help" or "-h")
                throw new ArgumentException($"--write 不适用于 '{command}'");
            if (command == "set-impl" && string.IsNullOrWhiteSpace(impl))
                throw new ArgumentException("set-impl 需要 --impl <数据文件>");
            if (command == "normalize-key" && string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("normalize-key 需要 --project <工程名>");
            if (command == "emit-declaration" && string.IsNullOrWhiteSpace(family))
                throw new ArgumentException("emit-declaration 需要 --family <夹具文件>");

            return new Options(command, data, write, bytes, check, impl, project, suite, schemas, family, output, only, bin, kind);
        }

        private static string Value(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length) throw new ArgumentException($"{option} 缺少取值");
            return args[++index];
        }
    }
}
