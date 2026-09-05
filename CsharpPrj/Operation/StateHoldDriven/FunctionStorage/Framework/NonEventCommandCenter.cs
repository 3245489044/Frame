
using System.Diagnostics;
using System.Windows.Forms;
using CommandFramework.Parsing;
using FunctionStorage.Abstractions;
using AbstractionsINonEventCommandRegistry = CommandFramework.Abstractions.INonEventCommandRegistry;
using AbstractionsIExecutionContext = TConfig.IExecutionContext;
using CoreNonEventCommandRegistry = FunctionStorage.Core.NonEventCommandRegistry;

namespace FunctionStorage
{
    public static class NonEventCommandCenter
    {
        private static readonly object _lock = new();
        private static AbstractionsINonEventCommandRegistry? _nonEventRegistry;

        public static AbstractionsINonEventCommandRegistry Registry
        {
            get
            {
                if (_nonEventRegistry == null) Initialize();
                return _nonEventRegistry!;
            }
        }

        public static void Initialize()
        {
            lock (_lock)
            {
                if (_nonEventRegistry != null) return;

                var registry = new CoreNonEventCommandRegistry();


                registry.Register("echo", ctx =>
                {
                    var target = GetTarget(ctx);
                    Console.WriteLine(target ?? "(空)");
                    SetStageSuccess(ctx, "echo");
                }, CommandFramework.Abstractions.CommandCriticality.Important, "输出文本");

                registry.Register("sleep", ctx =>
                {
                    var target = GetTarget(ctx);
                    if (int.TryParse(target, out var ms))
                    {
                        Thread.Sleep(ms);
                        SetStageSuccess(ctx, "sleep");
                    }
                    else
                    {
                        SetStageFailure(ctx, "sleep", $"sleep: 无效的毫秒数 '{target}'");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Important, "等待（毫秒）");

                registry.Register("close", ctx =>
                {
                    var target = GetTarget(ctx);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        SetStageFailure(ctx, "close", "close: 缺少窗口标题");
                        return;
                    }

                    try
                    {
                        var forms = Application.OpenForms;
                        for (int i = forms.Count - 1; i >= 0; i--)
                        {
                            if (forms[i].Text == target || forms[i].Name == target)
                            {
                                forms[i].Close();
                                SetStageSuccess(ctx, "close");
                                return;
                            }
                        }
                        SetStageFailure(ctx, "close", $"close: 未找到窗口 '{target}'");
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "close", $"close 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Important, "关闭窗口");

                registry.Register("sideload", ctx =>
                {
                    var target = GetTarget(ctx);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        SetStageFailure(ctx, "sideload", "sideload: 缺少配置文件路径");
                        return;
                    }

                    try
                    {
                        var projectRoot = FindProjectRoot();
                        var fullPath = Path.GetFullPath(
                            Path.Combine(projectRoot ?? "", "FileDriven", "DrivenConfig", target));

                        if (!File.Exists(fullPath))
                        {
                            fullPath = Path.GetFullPath(
                                Path.Combine(projectRoot ?? "", target));
                        }

                        if (!File.Exists(fullPath))
                        {
                            SetStageFailure(ctx, "sideload", $"sideload: 配置文件不存在 '{target}'");
                            return;
                        }

                        var json = File.ReadAllText(fullPath);

                        if (ctx.Items != null)
                        {
                            ctx.Items["__Sideloaded_" + Path.GetFileNameWithoutExtension(target)] = json;
                        }

                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("functions", out var functions))
                            {
                                foreach (var func in functions.EnumerateArray())
                                {
                                    var name = func.GetProperty("name").GetString();
                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        FunctionStorageRegistry.Register(name,
                                            new Func<object?, object?>(input => input),
                                            $"Sideloaded: {name}");
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

                        SetStageSuccess(ctx, "sideload");
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "sideload", $"sideload 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Important, "加载统一配置");

                registry.Register("list", ctx =>
                {
                    try
                    {
                        var forms = Application.OpenForms;
                        Console.WriteLine($"已打开的表单 ({forms.Count}):");
                        for (int i = 0; i < forms.Count; i++)
                        {
                            Console.WriteLine($"  [{i}] {forms[i].Text} ({forms[i].Name})");
                        }
                        SetStageSuccess(ctx, "list");
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "list", $"list 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "列出所有表单");

                registry.Register("exit", ctx =>
                {
                    try
                    {
                        Application.Exit();
                        SetStageSuccess(ctx, "exit");
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "exit", $"exit 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "退出框架");

                registry.Register("load", ctx =>
                {
                    var target = GetTarget(ctx);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        SetStageFailure(ctx, "load", "load: 缺少配置文件路径");
                        return;
                    }

                    try
                    {
                        var projectRoot = FindProjectRoot();
                        var fullPath = Path.GetFullPath(
                            Path.Combine(projectRoot ?? "", "FileDriven", "DrivenConfig", "JsonConfig", target));

                        if (!File.Exists(fullPath))
                        {
                            SetStageFailure(ctx, "load", $"load: 配置文件不存在 '{target}'");
                            return;
                        }

                        var json = File.ReadAllText(fullPath);
                        if (ctx.Items != null)
                        {
                            ctx.Items["__LoadedConfig_" + Path.GetFileNameWithoutExtension(target)] = json;
                        }

                        SetStageSuccess(ctx, "load");
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "load", $"load 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Critical, "加载 UI 配置文件");

                registry.Register("show", ctx =>
                {
                    var target = GetTarget(ctx);
                    Console.WriteLine($"Show: {target ?? "(空)"}");
                    SetStageSuccess(ctx, "show");
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "显示信息");

                registry.Register("look", ctx =>
                {
                    var target = GetTarget(ctx);
                    Console.WriteLine($"Look: {target ?? "(空)"}");
                    SetStageSuccess(ctx, "look");
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "查看配置");

                registry.Register("discover", ctx =>
                {
                    var target = GetTarget(ctx);
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            var forms = Application.OpenForms;
                            for (int i = 0; i < forms.Count; i++)
                            {
                                var controls = forms[i].Controls.Find(target, true);
                                if (controls.Length > 0)
                                {
                                    Console.WriteLine($"发现控件 '{target}': {controls[0].GetType().Name}");
                                    SetStageSuccess(ctx, "discover");
                                    return;
                                }
                            }
                            SetStageFailure(ctx, "discover", $"discover: 未找到控件 '{target}'");
                        }
                        else
                        {
                            SetStageSuccess(ctx, "discover");
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStageFailure(ctx, "discover", $"discover 失败: {ex.Message}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "发现控件");

                registry.Register("assert", ctx =>
                {
                    SetStageSuccess(ctx, "assert");
                }, CommandFramework.Abstractions.CommandCriticality.Important, "断言检查");

                registry.Register("validate", ctx =>
                {
                    var target = GetTarget(ctx);
                    Console.WriteLine($"Validate: {target ?? "(空)"}");
                    SetStageSuccess(ctx, "validate");
                }, CommandFramework.Abstractions.CommandCriticality.Optional, "验证表单");



                registry.Register("exec", (AbstractionsIExecutionContext ctx) =>
                {
                    string? target;
                    object? parameters;

                    if (TryExtractFecContext(ctx, out var fec, out target, out parameters))
                    {
                        if (string.IsNullOrWhiteSpace(target))
                        {
                            fec!.FunctionExecuteSuccess = false;
                            fec.FunctionErrorMessage = "exec: 缺少目标函数名";
                            return;
                        }
                        ExecDelegateHelper.ExecuteInFec(
                            fec!, target, parameters,
                            (fec, funcDelegate, params_) =>
                            {
                                var result = funcDelegate.DynamicInvoke(params_);
                                fec.FunctionExecuteSuccess = true;
                                fec.FunctionResultValue = result;
                            });
                        return;
                    }

                    ExecDelegateHelper.ExecuteInCmdCtx(
                        ctx, "exec", target, parameters);
                }, CommandFramework.Abstractions.CommandCriticality.Important, "执行已注册的工作流函数");

                registry.Register("exec-system", (AbstractionsIExecutionContext ctx) =>
                {
                    string? target;
                    object? unused;

                    if (TryExtractFecContext(ctx, out var fec, out target, out unused))
                    {
                        var args2 = fec!.ParsedCommand?.Parameters?.TryGetValue("args", out var argsVal2) == true
                            ? argsVal2?.ToString() ?? ""
                            : "";
                        var commandToExecute = !string.IsNullOrEmpty(args2) ? $"{target} {args2}" : target;
                        if (string.IsNullOrWhiteSpace(commandToExecute))
                        {
                            fec.FunctionExecuteSuccess = false;
                            fec.FunctionErrorMessage = "exec-system: 缺少要执行的命令";
                            return;
                        }
                        var (exitCode, output) = ExecDelegateHelper.RunSystemCommand(commandToExecute);
                        fec.FunctionExecuteSuccess = exitCode == 0;
                        fec.FunctionResultValue = output;
                        fec.FunctionErrorMessage = exitCode == 0 ? null : $"exit code: {exitCode}";
                        return;
                    }

                    var cmdTarget = GetTarget(ctx);
                    var args = "";
                    if (ctx.Items.TryGetValue("args", out var argsVal) && argsVal is string argsStr)
                    {
                        args = argsStr;
                    }
                    var cmdToExecute = !string.IsNullOrEmpty(args) ? $"{cmdTarget} {args}" : cmdTarget;
                    if (string.IsNullOrWhiteSpace(cmdToExecute))
                    {
                        SetStageFailure(ctx, "exec-system", "exec-system: 缺少要执行的命令");
                        return;
                    }
                    var (cmdExitCode, cmdOutput) = ExecDelegateHelper.RunSystemCommand(cmdToExecute!);
                    if (cmdExitCode == 0)
                    {
                        SetStageSuccess(ctx, "exec-system", cmdOutput);
                    }
                    else
                    {
                        SetStageFailure(ctx, "exec-system", $"exit code: {cmdExitCode}");
                    }
                }, CommandFramework.Abstractions.CommandCriticality.Important, "执行系统命令");

                _nonEventRegistry = registry;
            }
        }


        private static string? FindProjectRoot()
        {
            // 收敛到统一权威 ProjectPathManager（带静态缓存，消除每次 sideload/load 重复遍历目录树）
            try
            {
                return PathManager.ProjectPathManager.FindProjectRoot();
            }
            catch
            {
                // ProjectPathManager 内部遍历可能因路径权限异常失败；保持原行为轻量回退
                return null;
            }
        }

        private static string? GetTarget(AbstractionsIExecutionContext ctx)
        {
            if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
                return fec.ParsedCommand?.Target;

            if (ctx is CommandFramework.Models.CommandContext cmdCtx)
                return cmdCtx.ParsedCommand?.Target;

            if (ctx.Items.TryGetValue("Target", out var target))
                return target?.ToString();

            return null;
        }

        private static object? GetParameters(AbstractionsIExecutionContext ctx)
        {
            if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
                return fec.ParsedCommand?.Parameters;

            if (ctx is CommandFramework.Models.CommandContext cmdCtx)
                return cmdCtx.ParsedCommand?.Parameters;

            return null;
        }

        private static void SetStageSuccess(AbstractionsIExecutionContext ctx, string stageName, string? resultValue = null)
        {
            if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
            {
                fec.FunctionExecuteSuccess = true;
                fec.FunctionResultValue = resultValue;
                return;
            }

            if (ctx is CommandFramework.Models.CommandContext cmdCtx)
            {
                cmdCtx.SetStageResult("ExecuteCommand", true, null);
                return;
            }

            if (resultValue != null)
                ctx.Items["__ResultValue"] = resultValue;
        }

    private static void SetStageFailure(AbstractionsIExecutionContext ctx, string stageName, string errorMessage)
    {
        if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
        {
            fec.FunctionExecuteSuccess = false;
            fec.FunctionErrorMessage = errorMessage;
            return;
        }

        if (ctx is CommandFramework.Models.CommandContext cmdCtx)
        {
            cmdCtx.SetStageResult("ExecuteCommand", false, errorMessage);
            return;
        }

        ctx.Items["__ErrorMessage"] = errorMessage;
    }

    private static bool TryExtractFecContext(
        AbstractionsIExecutionContext ctx,
        out FunctionStorage.Core.FunctionExecutionContext? fec,
        out string? target,
        out object? parameters)
    {
        if (ctx is FunctionStorage.Core.FunctionExecutionContext fecCtx)
        {
            fec = fecCtx;
            target = fecCtx.ParsedCommand?.Target;
            parameters = fecCtx.ParsedCommand?.Parameters;
            return true;
        }

        fec = null;
        target = null;
        parameters = null;
        return false;
    }
}

internal static class ExecDelegateHelper
{
    public static void ExecuteInFec(
        FunctionStorage.Core.FunctionExecutionContext fec,
        string functionName,
        object? parameters,
        Action<FunctionStorage.Core.FunctionExecutionContext, Delegate, object?> executeAction)
    {
        try
        {
            if (FunctionStorageRegistry.Registry.TryGetDelegate(functionName, out var funcDelegate) && funcDelegate != null)
            {
                executeAction(fec, funcDelegate, parameters);
            }
            else
            {
                fec.FunctionExecuteSuccess = false;
                fec.FunctionErrorMessage = $"exec '{functionName}': 未找到已注册的函数";
            }
        }
        catch (Exception ex)
        {
            fec.FunctionExecuteSuccess = false;
            fec.FunctionErrorMessage = $"exec '{functionName}' 执行失败: {ex.Message}";
        }
    }

    public static void ExecuteInCmdCtx(
        TConfig.IExecutionContext ctx,
        string commandName,
        string? functionName,
        object? parameters)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            SetStageFailureStatic(ctx, commandName, $"{commandName}: 缺少目标函数名");
            return;
        }

        try
        {
            if (FunctionStorageRegistry.Registry.TryGetDelegate(functionName, out var funcDelegate) && funcDelegate != null)
            {
                funcDelegate.DynamicInvoke(parameters);
                SetStageSuccessStatic(ctx, commandName);
            }
            else
            {
                SetStageFailureStatic(ctx, commandName, $"{commandName} '{functionName}': 未找到已注册的函数");
            }
        }
        catch (Exception ex)
        {
            SetStageFailureStatic(ctx, commandName, $"{commandName} '{functionName}' 执行失败: {ex.Message}");
        }
    }

    public static (int ExitCode, string Output) RunSystemCommand(string command)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, output + (string.IsNullOrEmpty(error) ? "" : $"\nSTDERR: {error}"));
        }
        catch (Exception ex)
        {
            return (-1, $"执行失败: {ex.Message}");
        }
    }


    private static void SetStageSuccessStatic(TConfig.IExecutionContext ctx, string stageName)
    {
        if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
        {
            fec.FunctionExecuteSuccess = true;
            return;
        }
        if (ctx is CommandFramework.Models.CommandContext cmdCtx)
        {
            cmdCtx.SetStageResult("ExecuteCommand", true, null);
        }
    }

    private static void SetStageFailureStatic(TConfig.IExecutionContext ctx, string stageName, string errorMessage)
    {
        if (ctx is FunctionStorage.Core.FunctionExecutionContext fec)
        {
            fec.FunctionExecuteSuccess = false;
            fec.FunctionErrorMessage = errorMessage;
            return;
        }
        if (ctx is CommandFramework.Models.CommandContext cmdCtx)
        {
            cmdCtx.SetStageResult("ExecuteCommand", false, errorMessage);
            return;
        }
        ctx.Items["__ErrorMessage"] = errorMessage;
    }
}
}
