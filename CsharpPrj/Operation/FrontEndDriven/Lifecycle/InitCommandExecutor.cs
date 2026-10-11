#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Eventing;
using Share.EventBindingOrchestrator.Execution;
using TConfig;

namespace FrontEndDriven.Lifecycle;

/// <summary>
/// 通用初始化命令执行器（框架级单一实现）：统一 <c>initCommands</c> 与 <c>lifecycle.onAfterLoad</c> 两条命令通道。
/// 命令形态与「命令执行」同构 = 动词/符号 + 命名参数 + 输出目标：
///   - 裸函数名 → 经统一函数通道执行（旧 initCommandMode="function" 语义）；
///   - `exec &lt;符号&gt; [k=v...] [output=&lt;Control&gt;]` → 符号解析 + 命名参数 + 输出写回
///     （`_stepSelector` 形态走工作流路径；函数未命中回退工作流路径）。
/// 消除「initCommands 支持 exec / lifecycle 只支持裸名」的双轨差异：两者共用本实现。
/// </summary>
public static class InitCommandExecutor
{
    /// <summary>批量执行命令条目（逐条容错：单条异常不阻断后续条目）。</summary>
    public static async Task ExecuteAllAsync(EventPlatform? platform, IEnumerable<string>? commands, string scope)
    {
        if (platform is null || commands is null)
            return;

        foreach (var raw in commands)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            try
            {
                await ExecuteAsync(platform, entry, scope).ConfigureAwait(false);
                FrontEndDrivenLog.Log("InitCommandExecutor_ok", new { scope, command = entry });
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("InitCommandExecutor_error",
                    new { scope, command = entry, error = ex.Message, exceptionType = ex.GetType().FullName });
            }
        }
    }

    /// <summary>
    /// 批量执行结构化命令（B3：带 <c>requires</c> 相位门）。未达前置 ⇒ 登记 pending 待相位跃迁后幂等重放；
    /// 达前置 ⇒ 立即执行（单条异常不阻断后续）。
    /// </summary>
    public static async Task ExecuteSpecsAsync(EventPlatform? platform, IEnumerable<InitCommandSpec>? specs, string scope)
    {
        if (platform is null || specs is null)
            return;

        foreach (var spec in specs)
        {
            if (spec is null || string.IsNullOrWhiteSpace(spec.Exec))
                continue;

            var entry = ComposeEntry(spec);

            if (!InitCommandGate.IsSatisfied(spec.Requires))
            {
                var requires = spec.Requires!;
                InitCommandGate.RegisterPending(requires, () => ExecuteAsync(platform, entry, scope));
                FrontEndDrivenLog.Log("InitCommandExecutor_pending",
                    new { scope, command = entry, requires = string.Join(",", requires) });
                continue;
            }

            try
            {
                await ExecuteAsync(platform, entry, scope).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("InitCommandExecutor_error",
                    new { scope, command = entry, error = ex.Message, exceptionType = ex.GetType().FullName });
            }
        }
    }

    /// <summary>由结构化声明组装命令串（补 `exec ` 前缀 + 追加 `output=` 目标；已含则原样）。</summary>
    private static string ComposeEntry(InitCommandSpec spec)
    {
        var exec = spec.Exec.Trim();
        if (!exec.StartsWith("exec ", StringComparison.OrdinalIgnoreCase))
            exec = "exec " + exec;

        if (!string.IsNullOrWhiteSpace(spec.Output)
            && exec.IndexOf("output=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            exec += " output=" + spec.Output;
        }

        return exec;
    }

    /// <summary>执行单条命令：`exec ...` 走 exec 解析；否则按裸函数名执行。</summary>
    public static async Task ExecuteAsync(EventPlatform platform, string entry, string scope)
    {
        var command = (entry ?? string.Empty).Trim();
        if (command.Length == 0)
            return;

        if (command.StartsWith("exec ", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteExecAsync(platform, command, scope).ConfigureAwait(false);
            return;
        }

        await platform.InvokeFunctionAsync(command,
            source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);
    }

    private static async Task ExecuteExecAsync(EventPlatform platform, string entry, string scope)
    {
        // 唯一分词实现（D2 收敛）：引号感知分词，与命令条（UIToolbox.ParseCommandLine）共用。
        var tokens = CommandLineTokenizer.Tokenize(entry);
        if (tokens.Count < 2)
            return;

        var symbol = tokens[1];
        var (namedParams, hasStepSelector, outputTarget) = ParseExecParams(tokens.Skip(2));

        // 工作流形态（显式 _stepSelector）直走 ExecuteWorkflow；其余先按函数解析，失败再回退工作流路径。
        if (hasStepSelector)
        {
            await ExecuteWorkflowAsync(platform, symbol, namedParams, scope).ConfigureAwait(false);
            return;
        }

        var result = await platform.InvokeFunctionAsync(symbol, namedParams,
            source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            // exec <Func> output=<Control> 写回函数结果（SKILL 语义：执行函数并写回控件）。
            if (!string.IsNullOrWhiteSpace(outputTarget))
                WriteOutput(outputTarget, result.Output);
            return;
        }

        FrontEndDrivenLog.Log("InitCommandExecutor_function_failed",
            new { scope, symbol, error = result.ErrorMessage });
        await ExecuteWorkflowAsync(platform, symbol, namedParams, scope).ConfigureAwait(false);
    }

    /// <summary>经统一函数通道执行工作流（ExecuteWorkflow，input 组装为 JSON 以携带 _stepSelector 等命名参数）。</summary>
    private static async Task ExecuteWorkflowAsync(
        EventPlatform platform, string workflowName, Dictionary<string, object?> namedParams, string scope)
    {
        var input = namedParams.Count > 0 ? JsonSerializer.Serialize(namedParams) : "{}";

        var result = await platform.InvokeFunctionAsync("ExecuteWorkflow",
            new Dictionary<string, object?>
            {
                ["workflowName"] = workflowName,
                ["input"] = input
            },
            source: "LifecycleHook", timeoutMs: 30_000, scope: scope).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            FrontEndDrivenLog.Log("InitCommandExecutor_workflow_failed",
                new { scope, workflowName, error = result.ErrorMessage });
        }
    }

    /// <summary>解析 exec 参数：k=v 命名参数；`output=` 单独提取为输出目标控件；识别 _stepSelector 形态。</summary>
    private static (Dictionary<string, object?> NamedParams, bool HasStepSelector, string? OutputTarget) ParseExecParams(IEnumerable<string> tokens)
    {
        var namedParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasStepSelector = false;
        string? outputTarget = null;

        foreach (var tok in tokens)
        {
            if (tok.StartsWith("output=", StringComparison.OrdinalIgnoreCase))
            {
                outputTarget = tok.Substring("output=".Length);
                continue;
            }

            var eq = tok.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = tok.Substring(0, eq);
            var value = tok.Substring(eq + 1);
            namedParams[key] = value;
            if (string.Equals(key, "_stepSelector", StringComparison.OrdinalIgnoreCase))
                hasStepSelector = true;
        }

        return (namedParams, hasStepSelector, outputTarget);
    }

    /// <summary>
    /// 将命令的函数结果写回 output=&lt;Control&gt; 目标控件。经 IEcsStateAccessor（与 set 命令/按钮写回同源），
    /// 写 ECS 真源并投影物理控件，get 可读到。仅处理字符串结果；列表型结果不支持经 SetValue 写回，跳过并记日志。
    /// </summary>
    public static void WriteOutput(string controlName, object? output)
    {
        var accessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
        if (accessor == null)
        {
            FrontEndDrivenLog.Log("InitCommandExecutor_output_no_accessor", new { controlName });
            return;
        }

        if (output is not string text)
        {
            FrontEndDrivenLog.Log("InitCommandExecutor_output_skipped",
                new { controlName, type = output?.GetType().FullName });
            return;
        }

        accessor.SetValue(controlName, text);
    }
}
