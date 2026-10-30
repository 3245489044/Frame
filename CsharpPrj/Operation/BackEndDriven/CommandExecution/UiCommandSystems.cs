
using System.Windows.Forms;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.ContainerAbstractions;
using BackEndDriven.Diagnostics;
using Share.EventBindingOrchestrator.Abstractions;
using Share.EventBindingOrchestrator.Adapters;

namespace BackEndDriven.CommandExecution;


public class DiscoverSystem : CommandSystemBase
{
    private readonly FrameworkAbstractions.WinFormsAbstractions.IControlFinder? _controlFinder;

    public DiscoverSystem(EntityManager entityManager)
        : base(entityManager, "discover")
    {
        _controlFinder = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        try
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                var found = _controlFinder?.FindByName<Control>(target);
                if (found != null)
                {
                    BackEndLog.Log("Command_Discover_Found",
                        new { controlName = target, controlType = found.GetType().Name });
                    SetSuccess(entity);
                    return;
                }
                SetFailure(entity, $"discover: 未找到控件 '{target}'");
            }
            else
            {
                SetSuccess(entity);
            }
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"discover 失败: {ex.Message}");
        }
    }
}


public class AssertSystem : CommandSystemBase
{
    private readonly FrameworkAbstractions.WinFormsAbstractions.IControlFinder? _controlFinder;
    private readonly FrameworkAbstractions.WinFormsAbstractions.IEcsStateAccessor? _stateAccessor;

    public AssertSystem(EntityManager entityManager)
        : base(entityManager, "assert")
    {
        _controlFinder = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
        // 断言读源与 get 同口径：经状态访问器（ECS 写入值）读取——htmlviewer/markdownviewer 的内容不在 Control.Text（值契约只写 SetHtml/SetMarkdown），
        // 只读物理 Text 会把 HTML 承载控件断言成静态占位符（假红）。
        _stateAccessor = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IEcsStateAccessor>();
    }

    /// <summary>断言读源：状态值优先（与 get 同口径；HTML 承载控件内容在此）→ 物理 Control.Text 兜底（无状态实体/未渲染控件）。</summary>
    private string? ReadControlValue(Control ctl, string controlName)
        => _stateAccessor?.GetValue(controlName) ?? ctl.Text;

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var subAction = meta.Target;
        if (string.IsNullOrWhiteSpace(subAction))
        {
            SetFailure(entity, "assert: 缺少断言子命令（event-bound/openforms/text-empty）");
            return;
        }

        var firstSpace = subAction.IndexOf(' ');
        var subToken = firstSpace < 0 ? subAction : subAction[..firstSpace];
        var sub = subToken.StartsWith("assert-", StringComparison.OrdinalIgnoreCase)
            ? subToken["assert-".Length..].ToLowerInvariant()
            : subToken.ToLowerInvariant();

        try
        {
            switch (sub)
            {
                case "event-bound":
                    AssertEventBound(entity, meta);
                    break;

                case "openforms":
                    AssertOpenForms(entity, meta);
                    break;

                case "text-empty":
                    AssertTextEmpty(entity, meta);
                    break;

                case "container":
                    AssertContainer(entity, meta);
                    break;

                case "text-contains":
                    AssertTextContains(entity, meta);
                    break;

                default:
                    SetFailure(entity, $"assert: 未知断言子命令 '{subAction}'（支持 event-bound/openforms/text-empty/container/text-contains）");
                    break;
            }
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"assert '{subAction}' 失败: {ex.Message}");
        }
    }


    private void AssertEventBound(Entity entity, CommandMetaComponent meta)
    {
        var controlName = ExtractSubTarget(meta, "event-bound")
            ?? GetParamString(meta, "button")
            ?? GetParamString(meta, "target")
            ?? "";

        if (string.IsNullOrWhiteSpace(controlName))
        {
            SetFailure(entity, "assert-event-bound: 缺少按钮控件名");
            return;
        }

        var btn = _controlFinder?.FindByName<System.Windows.Forms.Button>(controlName);
        if (btn == null)
        {
            SetFailure(entity, $"assert-event-bound: 未找到按钮控件 '{controlName}'");
            return;
        }

        var inspector = AppServiceProvider.Resolve<IEventSubscriptionInspector>()
            ?? new WinFormsEventSubscriptionInspector();
        var hasSub = inspector.HasSubscribers(btn, "Click");
        if (hasSub == false)
        {
            SetFailure(entity, $"assert-event-bound: 按钮 '{controlName}' 未绑定任何 Click 事件处理程序");
            return;
        }

        BackEndLog.Log("Command_Assert_EventBound", new { controlName, subscribed = true });
        SetSuccess(entity, controlName);
    }

    private void AssertOpenForms(Entity entity, CommandMetaComponent meta)
    {
        var expectedStr = ExtractSubTarget(meta, "openforms")
            ?? GetParamString(meta, "count")
            ?? GetParamString(meta, "expected")
            ?? meta.Parameters?["_json"]?.ToString();
        if (!int.TryParse(expectedStr, out var expected))
        {
            SetFailure(entity, $"assert-openforms: 无效的期望窗体数 '{expectedStr}'");
            return;
        }

        var actual = Application.OpenForms.Count;
        if (actual != expected)
        {
            SetFailure(entity, $"assert-openforms: 窗体数量不匹配 — 期望 {expected}, 实际 {actual}");
            return;
        }

        BackEndLog.Log("Command_Assert_OpenForms", new { expected, actual });
        SetSuccess(entity, actual.ToString());
    }

    private void AssertTextEmpty(Entity entity, CommandMetaComponent meta)
    {
        var controlName = ExtractSubTarget(meta, "text-empty")
            ?? GetParamString(meta, "control")
            ?? GetParamString(meta, "target")
            ?? "";

        if (string.IsNullOrWhiteSpace(controlName))
        {
            SetFailure(entity, "assert-text-empty: 缺少控件名");
            return;
        }

        var ctl = _controlFinder?.FindByName<Control>(controlName);
        if (ctl == null)
        {
            SetFailure(entity, $"assert-text-empty: 未找到控件 '{controlName}'");
            return;
        }

        var actual = ReadControlValue(ctl, controlName) ?? string.Empty;
        if (!string.IsNullOrEmpty(actual))
        {
            SetFailure(entity, $"assert-text-empty: 控件 '{controlName}' 文本非空 — 实际 '{actual}'");
            return;
        }

        BackEndLog.Log("Command_Assert_TextEmpty", new { controlName });
        SetSuccess(entity, controlName);
    }

    /// <summary>
    /// 文本包含断言（业务结果自动校验）——校验某控件当前文本是否**包含**期望子串（忽略大小写）。
    /// 语法: assert text-contains &lt;controlName&gt; &lt;expectedSubstring...&gt;
    /// 解决 GUI 业务测试「命令 success 但结果不达标仍全绿」的假阳性：函数执行结果经 exec 写入控件后，
    /// 用本命令做子串断言（如 TaskPoolGetStatus → statusLabel，assert 其含 "Completed"；DbRead ur_tasks → 断言含任务 key），
    /// 失配即 SetFailure fail-fast，替代人工读输出。区别于 validate 的**精确相等**（expectedText），本命令用于含动态片段的整段文本。
    /// </summary>
    private void AssertTextContains(Entity entity, CommandMetaComponent meta)
    {
        var rest = ExtractSubTarget(meta, "text-contains") ?? "";
        var firstSpace = rest.IndexOf(' ');
        var controlName = (firstSpace < 0 ? rest : rest[..firstSpace]).Trim();
        var expected = firstSpace < 0 ? "" : rest[(firstSpace + 1)..];

        if (string.IsNullOrWhiteSpace(controlName))
        {
            SetFailure(entity, "assert-text-contains: 缺少控件名");
            return;
        }

        if (expected.Length == 0)
        {
            SetFailure(entity, "assert-text-contains: 缺少期望子串（空期望恒真，拒绝静默通过）");
            return;
        }

        var ctl = _controlFinder?.FindByName<Control>(controlName);
        if (ctl == null)
        {
            SetFailure(entity, $"assert-text-contains: 未找到控件 '{controlName}'");
            return;
        }

        var actual = ReadControlValue(ctl, controlName) ?? string.Empty;
        if (actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        {
            // 控件文本可能达数 KB（会话历史 HTML 含 L0 提示词）：日志/失败消息只带截断副本，判定仍用全文。
            // 断言用「头+尾」窗口——失配证据常在尾部（末条回答/错误气泡），仅留头部会截掉关键证据。
            // 附带长度：区分「空串/半截/全量」三种红灯语义（exec 未回写 → 空串，属零信息红）。
            var actualForLog = LogTextTruncation.TruncateForAssert(actual);
            BackEndLog.Log("Command_Assert_TextContains_Mismatch",
                new { controlName, expected, actual = actualForLog, len = actual.Length });
            SetFailure(entity, $"assert-text-contains: 控件 '{controlName}' 文本不包含期望子串 '{expected}' — 实际（长度 {actual.Length} 字符）'{actualForLog}'");
            return;
        }

        BackEndLog.Log("Command_Assert_TextContains", new { controlName, expected });
        SetSuccess(entity, controlName);
    }

    /// <summary>
    /// P7：容器归属断言——校验宿主容器的直接子控件数（「控件已挂载可见」的容器级判据）。
    /// 语法: assert container &lt;hostName&gt; count&lt;op&gt;&lt;n&gt;   （如 count&gt;0 / count==5 / count&gt;=1）
    /// 参数替代: container=&lt;host&gt; count=&lt;n&gt; op=&lt;op&gt;
    /// S3：语义移交 ContainerAssertionService（唯一断言权威），命令层仅做薄语法适配。
    /// </summary>
    private void AssertContainer(Entity entity, CommandMetaComponent meta)
    {
        var rest = ExtractSubTarget(meta, "container") ?? "";
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string hostName;
        string countExpr;
        if (parts.Length >= 2)
        {
            hostName = parts[0];
            countExpr = parts[1];
        }
        else
        {
            hostName = GetParamString(meta, "container") ?? GetParamString(meta, "host") ?? "";
            var countParam = GetParamString(meta, "count");
            var opParam = GetParamString(meta, "op") ?? ">=";
            countExpr = countParam != null ? $"{opParam}{countParam}" : "";
        }

        if (string.IsNullOrWhiteSpace(hostName) || string.IsNullOrWhiteSpace(countExpr))
        {
            SetFailure(entity, "assert-container: 语法应为 'assert container <hostName> count<op><n>'（如 'count>0'）");
            return;
        }

        var service = AppServiceProvider.Resolve<ContainerAssertionService>();
        if (service == null)
        {
            SetFailure(entity, "assert-container: ContainerAssertionService 未注册");
            return;
        }

        // 判定语义与失败文案全部由断言内核产出（类别机读、文案单一源），命令层只做语法适配。
        var diagnosis = service.AssertContainer(hostName, countExpr);
        if (!diagnosis.IsSuccess)
        {
            SetFailure(entity, AssertionDiagnosisLog.Report("assert-container", diagnosis));
            return;
        }

        BackEndLog.Log("Command_Assert_Container", new { hostName, expected = countExpr, actual = diagnosis.Actual });
        SetSuccess(entity, hostName);
    }


    private static string? ExtractSubTarget(CommandMetaComponent meta, string subName)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var prefix = subName + " ";
        if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = target[prefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        var fullPrefix = "assert-" + subName + " ";
        if (target.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = target[fullPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        return null;
    }

    // GetParamString 单一实现收敛到 ValidateSystemHelpers（S1：唯一权威，删冗余副本）。
    private static string? GetParamString(CommandMetaComponent meta, string key)
        => ValidateSystemHelpers.GetParamString(meta, key);
}


public class ValidateSystem : CommandSystemBase
{
    private readonly ContainerAssertionService? _assertionService;

    public ValidateSystem(EntityManager entityManager)
        : base(entityManager, "validate")
    {
        _assertionService = AppServiceProvider.Resolve<ContainerAssertionService>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "validate: 缺少目标控件名");
            return;
        }

        try
        {
            // 定位统一经断言内核（唯一寻址口径）：控件不存在 / 未挂载（孤儿）由诊断类别区分，
            // 不再在此手写「未找到控件（作用域 X 内）」与「孤儿控件」两套文案。
            var scopeOrHost = GetScopeParameter(meta);
            var lookup = _assertionService?.Locate(target, scopeOrHost);
            if (lookup == null)
            {
                SetFailure(entity, "validate: ContainerAssertionService 未注册");
                return;
            }

            if (!lookup.IsFound)
            {
                SetFailure(entity, AssertionDiagnosisLog.Report("validate", lookup.ToDiagnosis()));
                return;
            }

            var foundControl = lookup.Control!;

            // P7：文本断言——expectedText 参数存在即参与比对（含空串：合法"期望空文本"断言，如 commandTextBox 初始为空）。
            // 模板 ExpectedText 缺失展开为 expectedText=（空串）时同样参与比对：非空控件必然失配 → fail-fast，杜绝静默降级为存在性检查。
            if (meta.Parameters != null && meta.Parameters.TryGetValue("expectedText", out var expectedTextObj))
            {
                var expectedText = expectedTextObj?.ToString() ?? "";
                var actualText = foundControl.Text ?? "";
                if (!string.Equals(actualText, expectedText, StringComparison.Ordinal))
                {
                    SetFailure(entity, $"validate: 控件 '{target}' 文本不匹配 — 期望 '{expectedText}', 实际 '{actualText}'");
                    return;
                }
                BackEndLog.Log("Command_Validate_TextMatch",
                    new { target, expected = expectedText, actual = actualText });
            }

            if (meta.Parameters != null
                && (meta.Parameters.TryGetValue("expectedX", out var xObj)
                    || meta.Parameters.TryGetValue("x", out xObj))
                && (meta.Parameters.TryGetValue("expectedY", out var yObj)
                    || meta.Parameters.TryGetValue("y", out yObj)))
            {
                var expectedX = ConvertToInt(xObj);
                var expectedY = ConvertToInt(yObj);
                if (expectedX.HasValue && expectedY.HasValue)
                {
                    var actualLocation = foundControl.Location;
                    if (actualLocation.X != expectedX.Value || actualLocation.Y != expectedY.Value)
                    {
                        SetFailure(entity,
                            $"validate: 控件 '{target}' 位置不匹配 — 期望 ({expectedX.Value},{expectedY.Value}), 实际 ({actualLocation.X},{actualLocation.Y})");
                        return;
                    }
                    BackEndLog.Log("Command_Validate_PositionMatch",
                        new { target, expectedX = expectedX.Value, expectedY = expectedY.Value, actualX = actualLocation.X, actualY = actualLocation.Y });
                }
            }

            BackEndLog.Log("Command_Validate_Success", new { target });
            SetSuccess(entity, target);
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"validate '{target}' 失败: {ex.Message}");
        }
    }

    private static int? ConvertToInt(object? value)
    {
        if (value == null) return null;
        if (value is int i) return i;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        if (value is long l) return (int)l;
        if (value is double d) return (int)d;
        return null;
    }


    private static string? GetScopeParameter(CommandMetaComponent meta)
    {
        if (meta.Parameters != null && meta.Parameters.TryGetValue("scope", out var scopeObj) && scopeObj != null)
            return scopeObj.ToString();
        return null;
    }

}

/// <summary>
/// 命令层语法辅助（ValidateSystem/AssertSystem/ValidateMountSystem 复用）。
/// 挂载/归属/计数判定不再在此实现——单一权威已收敛到 FrameworkAbstractions.ContainerAssertionService。
/// </summary>
internal static class ValidateSystemHelpers
{
    /// <summary>从命令参数取值。</summary>
    public static string? GetParamString(CommandMetaComponent meta, string key)
    {
        if (meta.Parameters != null && meta.Parameters.TryGetValue(key, out var val) && val != null)
            return val.ToString();
        return null;
    }

    // 计数表达式解析（CountOp/TryParseCountExpr/CompareCount 的历史副本）已移除——
    // 单一实现收敛到 ContainerAssertionService（唯一权威），命令层仅做 token 切分，不再重复解析/比较。
}

/// <summary>
/// P7：挂载断言命令——校验控件已挂载到指定宿主（「控件存在 ≠ 挂载可见」的直接判据）。
/// 语法: validate-mount &lt;control&gt; parent=&lt;hostName&gt; [visible]
/// S3：语义移交 ContainerAssertionService（唯一断言权威），命令层仅做薄语法适配。
/// </summary>
public class ValidateMountSystem : CommandSystemBase
{
    private readonly FrameworkAbstractions.WinFormsAbstractions.IControlFinder? _controlFinder;

    public ValidateMountSystem(EntityManager entityManager)
        : base(entityManager, "validate-mount", "mount")
    {
        _controlFinder = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
    }

    public override void Execute(Entity entity, CommandMetaComponent meta)
    {
        var target = meta.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetFailure(entity, "validate-mount: 缺少目标控件名");
            return;
        }

        try
        {
            var parent = ValidateSystemHelpers.GetParamString(meta, "parent");
            var requireVisible = meta.Parameters != null && meta.Parameters.ContainsKey("visible");

            var service = AppServiceProvider.Resolve<ContainerAssertionService>();
            if (service == null)
            {
                SetFailure(entity, "validate-mount: ContainerAssertionService 未注册");
                return;
            }

            // 一次调用走完「定位 + 挂载 + 可见」：避免先前挂载/可见两段各出一套文案（validate-mount / validate 前缀漂移）。
            var diagnosis = service.AssertMounted(target, hostKey: parent, requireVisible: requireVisible);
            if (!diagnosis.IsSuccess)
            {
                SetFailure(entity, AssertionDiagnosisLog.Report("validate-mount", diagnosis));
                return;
            }

            BackEndLog.Log("Command_Validate_Mount_Success", new { target, parent = parent ?? "(form)" });
            SetSuccess(entity, target);
        }
        catch (Exception ex)
        {
            SetFailure(entity, $"validate-mount '{target}' 失败: {ex.Message}");
        }
    }
}
