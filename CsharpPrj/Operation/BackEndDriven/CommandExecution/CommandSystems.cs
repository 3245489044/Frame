
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// <summary>声明内等待可见化阈值：≥ 该毫秒数的 sleep/wait 记一条日志，使「脚本显式长等待」在时间线上可见
    /// （短等待为 UI 轮询/布局让位用，保持静默，避免刷屏）。等待本身不属卡死，见
    /// <c>CommandCoordinator.DeclaredWaitGraceMs</c> 的声明内等待豁免。</summary>
    private const int DeclaredWaitLogMinMs = 1000;

    public SleepSystem(EntityManager entityManager)
        : base(entityManager, "sleep", "wait") { }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        if (meta.Target == null || !int.TryParse(meta.Target, out var ms))
        {
            SetFailure(entity, $"sleep: 无效的毫秒数 '{meta.Target}'");
            return;
        }

        // 等待前记录（而非等待后）：卡住现场可立即判定「脚本声明等待 N ms」而非「命令挂死」。
        if (ms >= DeclaredWaitLogMinMs)
            BackEndLog.Log("Command_Sleep", new { action = meta.Action, sleepMs = ms });
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
        if (!TryParseExec(meta, out var target, out var parameters, out var commandTimeoutMs, out var outputTarget, out var expectedContains, out var expectedJson))
        {
            SetFailure(entity, "exec: 缺少目标函数名");
            return;
        }

        BackEndLog.Log("Command_Exec_Sync", new { function = target, parameters = parameters, timeoutMs = commandTimeoutMs, output = outputTarget });

        try
        {
            // 同步路径（保留）：兼容调用 Execute 的旧调用方。
            var invocation = EventPlatformExecutor.Invoke(target, parameters, commandTimeoutMs);
            HandleInvocation(entity, target, invocation, outputTarget, expectedContains, expectedJson);
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
        if (!TryParseExec(meta, out var target, out var parameters, out var commandTimeoutMs, out var outputTarget, out var expectedContains, out var expectedJson))
        {
            SetFailure(entity, "exec: 缺少目标函数名");
            return;
        }

        BackEndLog.Log("Command_Exec_Sync", new { function = target, parameters = parameters, timeoutMs = commandTimeoutMs, output = outputTarget });

        try
        {
            var invocation = await EventPlatformExecutor.InvokeAsync(target, parameters, commandTimeoutMs)
                .ConfigureAwait(false);
            HandleInvocation(entity, target, invocation, outputTarget, expectedContains, expectedJson);
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
        out string? expectedContains,
        out string? expectedJson)
    {
        target = meta.Target;
        parameters = new Dictionary<string, object?>();
        commandTimeoutMs = null;
        outputTarget = null;
        expectedContains = null;
        expectedJson = null;
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

        // IEF+ D1：命令级结果断言 expect-json（极小文法：field=value / field!=value / field in (a,b)）——
        // 对函数返回的 JSON 文本做字段断言（强判据，替代「文本 + sleep」）；非法文法即 fail-fast。
        if ((parameters.TryGetValue("expect-json", out var ejRaw) || parameters.TryGetValue("expectJson", out ejRaw)) && ejRaw != null)
        {
            expectedJson = ejRaw.ToString();
            parameters.Remove("expect-json");
            parameters.Remove("expectJson");
        }

        return true;
    }

    private void HandleInvocation(
        Entity entity,
        string target,
        EventPlatformInvocation invocation,
        string? outputTarget,
        string? expectedContains,
        string? expectedJson)
    {
        if (!invocation.PlatformAvailable)
        {
            BackEndLog.Log("Command_Exec_Failed", new { function = target, error = invocation.Error });
            // E9：失败不清空输出控件（清空即销毁现场证据）；失败原因由本行日志承载。
            SetFailure(entity, $"exec '{target}' 失败: {invocation.Error}");
            return;
        }

        var result = invocation.Result!;
        if (result.IsSuccess)
        {
            // output 一致语义：函数结果先回写目标控件（唯一经 OutputTargetWriter，E10 单写者）。
            // 使后续 expectedContains 失配现场在 GUI 上可见（证据保留）。
            if (!string.IsNullOrWhiteSpace(outputTarget) && result.Output != null)
            {
                OutputTargetWriter.Write(_stateAccessor, outputTarget, result.Output.ToString());
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
                    // E9：断言失配不清空输出控件（保留证据）。
                    SetFailure(entity, $"exec '{target}' 结果断言失败: expectedContains 为空（空期望恒真，拒绝静默通过）");
                    return;
                }

                if (actualOutput.IndexOf(expectedContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    BackEndLog.Log("Command_Exec_ExpectedContains_Mismatch",
                        new { function = target, expectedContains, actual = LogTextTruncation.TruncateForLog(actualOutput) });
                    // E9：断言失配不清空输出控件（保留证据）。
                    SetFailure(entity,
                        $"exec '{target}' 结果断言失败: 函数返回文本不含期望子串 '{expectedContains}' — 实际 '{LogTextTruncation.TruncateForLog(actualOutput)}'");
                    return;
                }

                BackEndLog.Log("Command_Exec_ExpectedContains_Match",
                    new { function = target, expectedContains });
            }

            // IEF+ D1：结果级 JSON 字段断言（expect-json 极小文法，可选）：失配/非法文法 → fail-fast。
            if (expectedJson != null)
            {
                if (!TryEvaluateExpectJson(expectedJson, actualOutput, out var jsonError))
                {
                    BackEndLog.Log("Command_Exec_ExpectJson_Mismatch",
                        new { function = target, expectJson = expectedJson, error = jsonError, actual = LogTextTruncation.TruncateForLog(actualOutput) });
                    // E9：断言失配不清空输出控件（保留证据）。
                    SetFailure(entity, $"exec '{target}' 结果断言失败 (expect-json): {jsonError}");
                    return;
                }
                BackEndLog.Log("Command_Exec_ExpectJson_Match",
                    new { function = target, expectJson = expectedJson });
            }

            BackEndLog.Log("Command_Exec_Success",
                new { function = target, result = LogTextTruncation.TruncateForLog(result.Output) });
            SetSuccess(entity, result.Output);
        }
        else
        {
            BackEndLog.Log("Command_Exec_Failed",
                new { function = target, error = result.ErrorMessage });
            // E9：失败不清空输出控件（保留证据）。
            SetFailure(entity, $"exec '{target}' 失败: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// IEF+ D1：expect-json 极小文法求值——仅允许 <c>field=value</c> / <c>field!=value</c> / <c>field in (a,b)</c>；
    /// 非法文法 / 非 JSON / 字段缺失 / 失配 ⇒ false + error（禁演化成通用表达式引擎，风险 R5）。
    /// </summary>
    private static bool TryEvaluateExpectJson(string grammar, string actualJson, out string error)
    {
        error = string.Empty;
        var text = grammar?.Trim() ?? string.Empty;
        if (text.Length == 0) { error = "空期望恒真，拒绝静默通过"; return false; }

        var m = Regex.Match(text, @"^(?<field>[A-Za-z0-9_.]+)\s*(?<op>!=|=|in)\s*(?<rhs>.+)$");
        if (!m.Success)
        {
            error = $"非法 expect-json 文法 '{text}'（仅允许 field=value / field!=value / field in (a,b)）";
            return false;
        }

        var field = m.Groups["field"].Value;
        var op = m.Groups["op"].Value;
        var rhs = m.Groups["rhs"].Value.Trim();

        string actual;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(actualJson) ? "{}" : actualJson);
            if (!TryReadJsonPath(doc.RootElement, field, out var value))
            {
                error = $"结果 JSON 不含字段 '{field}'";
                return false;
            }
            actual = value ?? string.Empty;
        }
        catch (JsonException ex)
        {
            error = $"结果非 JSON: {ex.Message}";
            return false;
        }

        if (op == "in")
        {
            if (rhs.Length < 2 || rhs[0] != '(' || rhs[rhs.Length - 1] != ')')
            {
                error = $"非法 in 文法 '{rhs}'（须为 (a,b)）";
                return false;
            }
            var items = rhs.Substring(1, rhs.Length - 2)
                .Split(',').Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0).ToArray();
            if (!items.Contains(actual, StringComparer.OrdinalIgnoreCase))
            {
                error = $"字段 '{field}'='{actual}' 不在集合 ({string.Join(",", items)})";
                return false;
            }
            return true;
        }

        var expected = rhs.Trim('"');
        var equals = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        if (op == "=" && !equals) { error = $"字段 '{field}'='{actual}' != '{expected}'"; return false; }
        if (op == "!=" && equals) { error = $"字段 '{field}' 不应等于 '{expected}'（实际 '{actual}'）"; return false; }
        return true;
    }

    /// <summary>按点分路径读取 JSON 字段（仅对象层；缺失 ⇒ false）。</summary>
    private static bool TryReadJsonPath(JsonElement root, string path, out string? value)
    {
        value = null;
        var current = root;
        foreach (var seg in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(seg, out current))
                return false;
        }
        value = current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
        return true;
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