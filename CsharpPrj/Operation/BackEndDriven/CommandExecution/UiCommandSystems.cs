
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

    public AssertSystem(EntityManager entityManager)
        : base(entityManager, "assert")
    {
        _controlFinder = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
    }

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

        if (!string.IsNullOrEmpty(ctl.Text))
        {
            SetFailure(entity, $"assert-text-empty: 控件 '{controlName}' 文本非空 — 实际 '{ctl.Text}'");
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

        var actual = ctl.Text ?? string.Empty;
        if (actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        {
            BackEndLog.Log("Command_Assert_TextContains_Mismatch",
                new { controlName, expected, actual });
            SetFailure(entity, $"assert-text-contains: 控件 '{controlName}' 文本不包含期望子串 '{expected}' — 实际 '{actual}'");
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

        if (!service.AssertContainer(hostName, countExpr, out var reason))
        {
            SetFailure(entity, reason);
            return;
        }

        BackEndLog.Log("Command_Assert_Container", new { hostName, countExpr });
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
    private readonly FrameworkAbstractions.WinFormsAbstractions.IControlFinder? _controlFinder;

    public ValidateSystem(EntityManager entityManager)
        : base(entityManager, "validate")
    {
        _controlFinder = AppServiceProvider.Resolve<FrameworkAbstractions.WinFormsAbstractions.IControlFinder>();
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
            var foundControl = ResolveScopedControl(target, meta);

            if (foundControl == null)
            {
                SetFailure(entity, $"validate: 未找到控件 '{target}'" +
                    (GetScopeParameter(meta) != null ? $"（作用域 '{GetScopeParameter(meta)}' 内）" : ""));
                return;
            }

            // P7：挂载可见校验——「控件对象存在 ≠ 已挂载到可见窗体树」。
            // 池中孤儿控件（未挂载到任何容器）在此判失败，消除「日志全绿但 UI 空」的假阳性。
            if (!ValidateSystemHelpers.IsMountedOnVisibleTree(foundControl))
            {
                BackEndLog.Log("Command_Validate_NotMounted",
                    new { target, note = "控件对象存在但未挂载到可见窗体（孤儿控件，请检查宿主解析/关系挂载）" });
                SetFailure(entity,
                    $"validate: 控件 '{target}' 存在但未挂载到可见窗体（孤儿控件）——validate 不再以「对象存在」为通过判据");
                return;
            }

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

    /// <summary>挂载/计数辅助统一收口到 <see cref="ValidateSystemHelpers"/>（ValidateSystem/AssertSystem/ValidateMountSystem 复用）。</summary>
    private static bool IsMountedOnVisibleTree(Control control)
        => ValidateSystemHelpers.IsMountedOnVisibleTree(control);

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

    private Control? ResolveScopedControl(string target, CommandMetaComponent meta)
    {
        var scope = GetScopeParameter(meta);

        // 显式 scope：经容器访问端口解析宿主（S3：删除 scope→hostName 硬编码 switch，以 ContainerRegistry 为唯一反查口径）。
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var host = ResolveHostOfScope(scope) ?? (Control?)null;
            if (host == null)
                return null;
            return _controlFinder?.FindRecursive(host, target)
                ?? ControlSearch(host, target);
        }

        // P7：无 scope 默认限定可见窗体树（挂载可见语义）——ControlPool/注册表直查仅作显式降级并 WARN。
        // 孤儿控件即使被池直查命中，也会在 Execute 的 IsMountedOnVisibleTree 校验中判失败。
        var inVisibleTree = _controlFinder?.FindAcrossForms(
            formMatcher: form => form.Visible,
            controlLookup: form => _controlFinder?.FindRecursive(form, target));
        if (inVisibleTree != null)
            return inVisibleTree;

        // P7：孤儿控件（池直查命中但未挂载到可见窗体树）在查控件阶段即判失败——不再返回控件让 Execute 的
        // IsMountedOnVisibleTree 绕一圈再判（避免双重日志）。孤儿诊断由此处 Fallback_PoolExistence 一条日志承载。
        var poolOnly = _controlFinder?.FindByName<Control>(target);
        if (poolOnly != null)
        {
            BackEndLog.Log("Command_Validate_Fallback_PoolExistence",
                new { target, note = "控件对象存在但未挂载到可见窗体树（孤儿控件），validate 判失败" });
            return null;
        }

        return null;
    }

    /// <summary>经容器访问端口按作用域解析宿主 Control（ContainerRegistry 唯一反查口径）。</summary>
    private System.Windows.Forms.Control? ResolveHostOfScope(string scope)
    {
        var port = AppServiceProvider.Resolve<IContainerAccessPort>();
        var handle = port?.ResolveHost(scope);
        return handle?.Control;
    }

    private static Control? ControlSearch(Control root, string name)
    {
        foreach (Control c in root.Controls)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                return c;
            var nested = ControlSearch(c, name);
            if (nested != null) return nested;
        }
        return null;
    }
}

/// <summary>
/// P7：挂载可见/计数表达式共享辅助（ValidateSystem/AssertSystem/ValidateMountSystem 复用）。
/// 「控件对象存在 ≠ 已挂载可见」——FindForm()==null 即孤儿控件（池直查命中的未挂载对象）。
/// </summary>
internal static class ValidateSystemHelpers
{
    /// <summary>挂载可见判据：控件已挂载到窗体树（FindForm() != null）。</summary>
    public static bool IsMountedOnVisibleTree(Control control)
    {
        if (control == null) return false;
        try
        {
            return control.FindForm() != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>沿 Parent 链上溯判定 child 是否 ancestor 的后代。</summary>
    public static bool IsDescendantOf(Control child, Control ancestor)
    {
        if (child == null || ancestor == null) return false;
        var current = child.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current.Parent;
        }
        return false;
    }

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

            if (!service.AssertMounted(controlName: target, hostKey: parent, out var reason))
            {
                SetFailure(entity, reason);
                return;
            }

            if (requireVisible && !service.AssertVisible(target, out var visReason))
            {
                SetFailure(entity, visReason);
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
