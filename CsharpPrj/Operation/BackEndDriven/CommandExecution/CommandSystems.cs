
using System.Diagnostics;
using System.IO;
using ECS.Components;
using ECS.Core;
using BackEndDriven.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using Modules.Parsing.Parsing;

namespace BackEndDriven.CommandExecution;


public class ParseCommandSystem : CommandSystemBase
{
    public ParseCommandSystem(EntityManager entityManager)
        : base(entityManager, "parse") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        SetSuccess(entity);
    }

    public Entity ParseAndCreateEntity(string commandLine, int index)
    {
        try
        {
            var metaCommand = CommandParser.Parse(commandLine);
            if (metaCommand != null)
            {
                return EntityManager.CreateCommandEntity(
                    metaCommand.Action ?? "",
                    metaCommand.Target,
                    metaCommand.Parameters,
                    commandLine,
                    index);
            }

            return EntityManager.CreateParseErrorEntity(
                commandLine, index, $"Failed to parse command: {commandLine}");
        }
        catch (Exception ex)
        {
            return EntityManager.CreateParseErrorEntity(
                commandLine, index, $"Parse error: {ex.Message}");
        }
    }
}


public class EchoSystem : CommandSystemBase
{
    public EchoSystem(EntityManager entityManager)
        : base(entityManager, "echo", "print") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var message = meta.Target ?? "(空)";
        BackEndLog.Log("Command_Echo", new { message });
        SetSuccess(entity, message);
    }
}


public class SleepSystem : CommandSystemBase
{
    public SleepSystem(EntityManager entityManager)
        : base(entityManager, "sleep", "wait") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        if (meta.Target == null || !int.TryParse(meta.Target, out var ms))
        {
            SetFailure(entity, $"sleep: 无效的毫秒数 '{meta.Target}'");
            return;
        }

        Thread.Sleep(ms);
        SetSuccess(entity);
    }
}


public class CloseSystem : CommandSystemBase
{
    public CloseSystem(EntityManager entityManager)
        : base(entityManager, "close") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        EntityManager.AddOrSetComponent(entity, new CloseRequestComponent
        {
            WindowTitle = meta.Target
        });
        SetSuccess(entity);
    }
}


public class ExitSystem : CommandSystemBase
{
    public ExitSystem(EntityManager entityManager)
        : base(entityManager, "exit", "quit") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        EntityManager.AddOrSetComponent(entity, new ExitRequestComponent
        {
            Handled = false
        });
        SetSuccess(entity);
    }
}


public class SideloadSystem : CommandSystemBase
{
    public SideloadSystem(EntityManager entityManager)
        : base(entityManager, "sideload")
    {
        // 注入模块日志门面（CommandSystemBase 下沉后不直接依赖 BackEndDriven.Diagnostics）
        CommandLog = BackEndLog.Log;
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "sideload: 缺少配置文件路径");
            return;
        }

        // Phase B：委托「加载即渲染」服务；未装配（非 GUI/独立命令环境）回退旧读文件行为。
        var renderSvc = AppServiceProvider.Resolve<IConfigLoadRenderService>();
        if (renderSvc == null)
        {
            ReadConfigFile(entity, target, subFolder: "", commandName: "sideload", logEventName: "Command_Sideload");
            return;
        }

        var fullPath = ResolveConfigPath(target, subFolder: "");
        if (fullPath == null || !File.Exists(fullPath))
        {
            // 诊断：配置解析失败不再静默——把解析结果与候选根打日志（此前 SetFailure 只置组件不打印）。
            BackEndLog.Log("Command_Sideload_Config_NotResolved",
                new { target, fullPath });
            SetFailure(entity, $"sideload: 配置文件不存在 '{fullPath ?? target}'");
            return;
        }

        var result = renderSvc.LoadAndShow(fullPath, new LoadRenderOptions { Show = false });
        if (result.Succeeded)
        {
            // 诊断：sideload 渲染成功时也落一条结构化日志（含挂载统计），使「渲染执行 vs 挂载可见」可在日志层核对。
            BackEndLog.Log("Command_Sideload_Render_Succeeded",
                new { scope = result.Scope, configPath = result.ConfigPath, nodeCount = result.NodeCount, hostChildCount = result.HostChildCount });
            SetSuccess(entity, result.ConfigText ?? result.Summary);
        }
        else
        {
            // 诊断：sideload 渲染失败不再静默——把 Error/挂载统计打日志（此前 SetFailure 只置组件不打印）。
            BackEndLog.Log("Command_Sideload_Render_Failed",
                new { scope = result.Scope, configPath = result.ConfigPath, error = result.Error, nodeCount = result.NodeCount, hostChildCount = result.HostChildCount });
            SetFailure(entity, $"sideload: {result.Error ?? "加载失败"}");
        }
    }
}


public class ListSystem : CommandSystemBase
{
    public ListSystem(EntityManager entityManager)
        : base(entityManager, "list", "forms") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        try
        {
            var forms = System.Windows.Forms.Application.OpenForms;
            BackEndLog.Log("Command_List", new { count = forms.Count });
            for (int i = 0; i < forms.Count; i++)
            {
                BackEndLog.Log("Command_List_Item", new
                {
                    index = i,
                    text = forms[i].Text,
                    name = forms[i].Name
                });
            }
            SetSuccess(entity);
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"list 失败: {ex.Message}");
        }
    }
}


public class LoadSystem : CommandSystemBase
{
    public LoadSystem(EntityManager entityManager)
        : base(entityManager, "load")
    {
        // 注入模块日志门面（CommandSystemBase 下沉后不直接依赖 BackEndDriven.Diagnostics）
        CommandLog = BackEndLog.Log;
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "load: 缺少配置文件路径");
            return;
        }

        // Phase B：委托「加载即渲染」服务，modifier=show 语义生效；未装配（非 GUI/独立命令环境）回退旧读文件行为。
        var renderSvc = AppServiceProvider.Resolve<IConfigLoadRenderService>();
        if (renderSvc == null)
        {
            ReadConfigFile(entity, target, subFolder: "JsonConfig", commandName: "load", logEventName: "Command_Load");
            return;
        }

        var fullPath = ResolveConfigPath(target, subFolder: "JsonConfig");
        if (fullPath == null || !File.Exists(fullPath))
        {
            SetFailure(entity, $"load: 配置文件不存在 '{fullPath ?? target}'");
            return;
        }

        var show = meta.Parameters != null
            && meta.Parameters.TryGetValue("modifier", out var modifier)
            && string.Equals(modifier?.ToString(), "show", StringComparison.OrdinalIgnoreCase);

        var result = renderSvc.LoadAndShow(fullPath, new LoadRenderOptions { Show = show });

        // Phase A·活动作用域标定：加载成功后，将命令作用域标定为「实际渲染到的 scope」（融入主内容区时为 Content），
        // 命令 get/set/click 据此优先在活动 scope 内寻址，消除「同名控件跨 scope 串扰」（如 clearButton 误中 MainLayout）。
        var scope = result.Scope ?? Path.GetFileNameWithoutExtension(target);
        var scopeCtx = AppServiceProvider.Resolve<IScopeContext>();
        if (scopeCtx != null && !string.IsNullOrWhiteSpace(scope))
        {
            scopeCtx.CurrentScope = scope;
            BackEndLog.Log("Command_Load_ActiveScope", new { scope, configBaseName = Path.GetFileNameWithoutExtension(target) });
        }

        if (result.Succeeded)
        {
            // 保留向后兼容：渲染结果摘要 / 只读原始文本 回写 CommandResultComponent.ResultValue。
            SetSuccess(entity, result.ConfigText ?? result.Summary);
        }
        else
        {
            SetFailure(entity, $"load: {result.Error ?? "渲染失败"}");
        }
    }
}


public class ExecSystem : CommandSystemBase, IAsyncCommandSystem
{
    private readonly IEcsStateAccessor? _stateAccessor;

    public ExecSystem(EntityManager entityManager)
        : base(entityManager, "exec")
    {
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        if (!TryParseExec(meta, out var target, out var parameters, out var commandTimeoutMs, out var outputTarget, out var expectedContains))
        {
            SetFailure(entity, "exec: 缺少目标函数名");
            return;
        }

        BackEndLog.Log("Command_Exec_Sync", new { function = target, parameters = parameters, timeoutMs = commandTimeoutMs, output = outputTarget });

        try
        {
            // 同步路径（保留）：兼容调用 Execute 的旧调用方。
            var invocation = EventPlatformExecutor.Invoke(target, parameters, commandTimeoutMs);
            HandleInvocation(entity, target, invocation, outputTarget, expectedContains);
        }
        catch (Exception ex)
        {
            BackEndLog.Log("Command_Exec_Error", new { function = target, error = ex.Message });
            SetFailure(entity, $"exec '{target}' 执行异常: {ex.Message}");
        }
    }

    /// <summary>异步路径：await 而非同步阻塞，供长时间异步函数（如 Cline_PoolReady）不钉死命令线程。</summary>
    public async Task ExecuteAsync(Entity entity, CommandMetaComponent meta)
    {
        if (!TryParseExec(meta, out var target, out var parameters, out var commandTimeoutMs, out var outputTarget, out var expectedContains))
        {
            SetFailure(entity, "exec: 缺少目标函数名");
            return;
        }

        BackEndLog.Log("Command_Exec_Sync", new { function = target, parameters = parameters, timeoutMs = commandTimeoutMs, output = outputTarget });

        try
        {
            var invocation = await EventPlatformExecutor.InvokeAsync(target, parameters, commandTimeoutMs)
                .ConfigureAwait(false);
            HandleInvocation(entity, target, invocation, outputTarget, expectedContains);
        }
        catch (Exception ex)
        {
            BackEndLog.Log("Command_Exec_Error", new { function = target, error = ex.Message });
            SetFailure(entity, $"exec '{target}' 执行异常: {ex.Message}");
        }
    }

    /// <summary>解析 exec 参数（目标函数名 + 命令级元数据 + 函数参数）。命令级元数据（timeout/output/expectedContains）用于本命令层，不传入函数。</summary>
    private static bool TryParseExec(
        CommandMetaComponent meta,
        out string target,
        out Dictionary<string, object?> parameters,
        out int? commandTimeoutMs,
        out string? outputTarget,
        out string? expectedContains)
    {
        target = meta.Target;
        parameters = new Dictionary<string, object?>();
        commandTimeoutMs = null;
        outputTarget = null;
        expectedContains = null;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (meta.Parameters != null)
        {
            foreach (var kvp in meta.Parameters)
            {
                // 保留 null 值以表达"参数未提供"，不吞成空串（原 ?? string.Empty 丢失缺省语义——R:3.4）。
                parameters[kvp.Key] = kvp.Value;
            }
        }

        // 命令级预算：提取 timeout（命令级元数据，非函数参数），覆盖 EventPlatformExecutor 的 30s 默认
        // （如 Cline_PoolReady 预热需 >56s 冷启动，否则被 30s 默认掐断）。
        if (parameters.TryGetValue("timeout", out var timeoutRaw)
            && int.TryParse(timeoutRaw?.ToString(), out var timeoutVal) && timeoutVal > 0)
        {
            commandTimeoutMs = timeoutVal;
            parameters.Remove("timeout");
        }

        // 命令级输出目标：提取 output（命令级元数据，非函数参数），把函数结果回写指定控件。
        if (parameters.TryGetValue("output", out var outputRaw) && outputRaw != null
            && !string.IsNullOrWhiteSpace(outputRaw.ToString()))
        {
            outputTarget = outputRaw.ToString();
            parameters.Remove("output");
        }

        // 命令级结果断言：提取 expectedContains（命令级元数据，非函数参数）——对函数返回文本做包含断言（结果级 fail-fast，
        // 解决「函数返回即 success 但业务未达标仍全绿」假阳性；区别于 assert text-contains 的控件文本断言，免依赖控件覆盖语义）。
        // 语义：空期望（恒真）拒绝、失配 SetFailure（同 AssertTextContains，防静默通过）。null=未提供，空串=提供但非法。
        if (parameters.TryGetValue("expectedContains", out var ecRaw) && ecRaw != null)
        {
            expectedContains = ecRaw.ToString();
            parameters.Remove("expectedContains");
        }

        return true;
    }

    private void HandleInvocation(
        Entity entity,
        string target,
        EventPlatformInvocation invocation,
        string? outputTarget,
        string? expectedContains)
    {
        if (!invocation.PlatformAvailable)
        {
            BackEndLog.Log("Command_Exec_Failed", new { function = target, error = invocation.Error });
            SetFailure(entity, $"exec '{target}' 失败: {invocation.Error}");
            return;
        }

        var result = invocation.Result!;
        if (result.IsSuccess)
        {
            // output 一致语义：函数结果先回写目标控件（ECS 状态真源 → 投影物理控件，与 SetSystem 同路径），
            // 使后续 expectedContains 失配现场在 GUI 上可见。
            if (!string.IsNullOrWhiteSpace(outputTarget)
                && _stateAccessor != null
                && result.Output != null)
            {
                _stateAccessor.SetValue(outputTarget, result.Output.ToString());
            }

            // 结果级内容断言（expectedContains，可选用）：对函数返回文本做包含校验（忽略大小写）。
            // 失配/空期望 → SetFailure fail-fast，杜绝「函数返回即 success 但业务未达标仍全绿」。
            var actualOutput = result.Output?.ToString() ?? string.Empty;
            if (expectedContains != null)
            {
                if (string.IsNullOrWhiteSpace(expectedContains))
                {
                    BackEndLog.Log("Command_Exec_ExpectedContains_Mismatch",
                        new { function = target, expectedContains, actual = LogTextTruncation.TruncateForLog(actualOutput), note = "空期望恒真，拒绝静默通过" });
                    SetFailure(entity, $"exec '{target}' 结果断言失败: expectedContains 为空（空期望恒真，拒绝静默通过）");
                    return;
                }

                if (actualOutput.IndexOf(expectedContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    BackEndLog.Log("Command_Exec_ExpectedContains_Mismatch",
                        new { function = target, expectedContains, actual = LogTextTruncation.TruncateForLog(actualOutput) });
                    SetFailure(entity,
                        $"exec '{target}' 结果断言失败: 函数返回文本不含期望子串 '{expectedContains}' — 实际 '{LogTextTruncation.TruncateForLog(actualOutput)}'");
                    return;
                }

                BackEndLog.Log("Command_Exec_ExpectedContains_Match",
                    new { function = target, expectedContains });
            }

            BackEndLog.Log("Command_Exec_Success",
                new { function = target, result = LogTextTruncation.TruncateForLog(result.Output) });
            SetSuccess(entity, result.Output);
        }
        else
        {
            BackEndLog.Log("Command_Exec_Failed",
                new { function = target, error = result.ErrorMessage });
            SetFailure(entity, $"exec '{target}' 失败: {result.ErrorMessage}");
        }
    }
}


public class ShowSystem : CommandSystemBase
{
    public ShowSystem(EntityManager entityManager)
        : base(entityManager, "show") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        BackEndLog.Log("Command_Show", new { message = meta.Target ?? "(空)" });
        SetSuccess(entity);
    }
}


public class LookSystem : CommandSystemBase
{
    public LookSystem(EntityManager entityManager)
        : base(entityManager, "look") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        BackEndLog.Log("Command_Look", new { target = meta.Target ?? "(空)" });
        SetSuccess(entity);
    }
}