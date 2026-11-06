using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Share.HostAssembly.Commands;

/// <summary>
/// 控制台子命令宿主 —— 把「取子命令 → 校验位置参数个数 → 输出用法 → 映射退出码 0/1/2 → 执行」这一进程边界协议
/// 收敛为单一实现，供多可执行工具共用。
/// <para>
/// 与 <see cref="CommandDispatcher{TRequest}"/> 的分工：后者是「进程内 action → 异步 handler」总线分发；
/// 本类只承载控制台协议（同步 handler、位置参数、usage、退出码、异常边界），不改动既有异步分发语义。
/// </para>
/// <para>
/// 流约定：handler 自身输出写 <c>output</c>；用法/未知命令/参数个数错误与异常边界写 <c>error</c>。
/// 调用方若希望两者同流（如单元测试只捕获标准输出），传入同一个 <see cref="TextWriter"/> 即可。
/// </para>
/// </summary>
public sealed class ConsoleCommandHost
{
    /// <summary>成功。</summary>
    public const int ExitOk = 0;

    /// <summary>有错误/断言失败。</summary>
    public const int ExitFailed = 1;

    /// <summary>用法错误或未预期异常（进程边界的统一兜底）。</summary>
    public const int ExitUsage = 2;

    private static readonly string[] HelpTokens = ["-h", "--help", "help"];

    private readonly Dictionary<string, CommandSpec> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _usage;
    private CommandSpec? _fallback;

    /// <summary>构筑宿主。</summary>
    /// <param name="usage">用法文本（无参数 / 帮助 / 未知命令 / 参数个数错误时回显）。</param>
    public ConsoleCommandHost(string usage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        _usage = usage;
    }

    /// <summary>用法文本。</summary>
    public string Usage => _usage;

    /// <summary>已注册命令名集合。</summary>
    public IReadOnlyCollection<string> Commands => _commands.Keys;

    /// <summary>注册一个子命令（同名幂等覆盖）。</summary>
    /// <param name="spec">命令规格。</param>
    public ConsoleCommandHost Register(CommandSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        ArgumentNullException.ThrowIfNull(spec.Handler);
        _commands[spec.Name] = spec;
        return this;
    }

    /// <summary>
    /// 登记位置参数回退规格：首 token 不是已注册命令时，整段参数按该规格执行（如「无子命令，首参即配置根」）。
    /// </summary>
    /// <param name="spec">回退规格。</param>
    public ConsoleCommandHost WithFallback(CommandSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Handler);
        _fallback = spec;
        return this;
    }

    /// <summary>执行命令。</summary>
    /// <param name="args">完整参数（含子命令名）。</param>
    /// <param name="output">命令输出（handler 自身产出）。</param>
    /// <param name="error">用法/未知命令/参数错误/异常边界输出；缺省与 <paramref name="output"/> 同流。</param>
    public int Run(IReadOnlyList<string>? args, TextWriter output, TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        error ??= output;

        if (args is null || args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            error.WriteLine(_usage);
            return ExitUsage;
        }

        var name = args[0];
        if (HelpTokens.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            error.WriteLine(_usage);
            return ExitOk;
        }

        CommandSpec spec;
        IReadOnlyList<string> rest;
        if (_commands.TryGetValue(name, out var registered))
        {
            spec = registered;
            rest = args.Skip(1).ToList();
        }
        else if (_fallback is not null)
        {
            spec = _fallback;
            rest = args;
        }
        else
        {
            error.WriteLine($"未知命令：{name}");
            error.WriteLine(_usage);
            return ExitUsage;
        }

        if (rest.Count < spec.MinArgs || rest.Count > spec.MaxArgs)
        {
            error.WriteLine($"用法错误：{spec.Name} 需要 {spec.Signature}。");
            error.WriteLine(_usage);
            return ExitUsage;
        }

        try
        {
            return spec.Handler(rest, output);
        }
        catch (Exception ex)
        {
            // 统一异常边界：任何 handler 未捕获异常都落 2（用法/失败），不破坏 0/1/2 退出协议。
            error.WriteLine($"{spec.ErrorLabel ?? spec.Name} 失败: {ex.Message}");
            return ExitUsage;
        }
    }
}

/// <summary>
/// 子命令规格：名称 + 位置参数签名 + 参数个数边界 + handler。
/// <para>
/// <see cref="MinArgs"/>/<see cref="MaxArgs"/> 只约束「子命令名之后」的位置参数个数；
/// 需要自定义选项解析的命令可把 <see cref="MaxArgs"/> 放宽到 <see cref="int.MaxValue"/> 并在 handler 内自行校验。
/// </para>
/// </summary>
/// <param name="Name">子命令名（宿主按 OrdinalIgnoreCase 匹配）。</param>
/// <param name="Signature">位置参数签名（用于参数个数错误文案，如 <c>&lt;configRoot&gt; &lt;outDir&gt;</c>）。</param>
/// <param name="MinArgs">最少位置参数个数。</param>
/// <param name="Handler">命令处理器：参数（不含子命令名）+ 输出 → 退出码。</param>
public sealed record CommandSpec(
    string Name,
    string Signature,
    int MinArgs,
    Func<IReadOnlyList<string>, TextWriter, int> Handler)
{
    /// <summary>最多位置参数个数（缺省不限）。</summary>
    public int MaxArgs { get; init; } = int.MaxValue;

    /// <summary>失败文案前缀（缺省取 <see cref="Name"/>；用于保留既有「ui-declaration 失败:」等输出）。</summary>
    public string? ErrorLabel { get; init; }
}
