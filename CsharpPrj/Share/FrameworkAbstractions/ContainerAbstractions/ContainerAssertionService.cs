
#nullable enable

using System;
using System.Windows.Forms;
using TConfig.ContainerAssertions;

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器断言服务（ContainerAssertionService）——脚本断言与框架自检复用的单一断言权威。
/// 语义全部经 <see cref="IContainerAccessPort"/> 解析，命令层（UiCommandSystems）仅做薄语法适配；
/// 脚本断言与框架自检（P7 审计 / 回归脚本）同口径（CountableOrderBridge：统一比较域）。
/// </summary>
public sealed class ContainerAssertionService
{
    private readonly IContainerAccessPort? _port;
    private readonly Func<string, Control?>? _findOrdinaryControl;

    /// <summary>
    /// </summary>
    /// <param name="port">容器访问端口（容器断言/挂载判定）。</param>
    /// <param name="findOrdinaryControl">普通控件查找委托（非容器控件，如按钮/标签/文本框），供 text/visible 断言。</param>
    public ContainerAssertionService(
        IContainerAccessPort? port = null,
        Func<string, Control?>? findOrdinaryControl = null)
    {
        _port = port;
        _findOrdinaryControl = findOrdinaryControl;
    }

    /// <summary>
    /// 容器归属断言：<paramref name="countExpr"/> 形如 count&gt;0 / count&gt;=n / count==n / count&lt;n 等。
    /// </summary>
    public bool AssertContainer(string scopeOrHostKey, string countExpr, out string reason)
    {
        reason = string.Empty;
        if (_port == null)
        {
            reason = "assert-container: 容器访问端口未注册（IContainerAccessPort）";
            return false;
        }

        var handle = ResolveHandle(scopeOrHostKey);
        if (handle == null)
        {
            reason = "assert-container: " + ContainerFailureMessages.Format(ContainerFailureKind.HostNotResolved, scopeOrHostKey);
            return false;
        }

        if (handle.Control == null)
        {
            reason = "assert-container: " + ContainerFailureMessages.Format(ContainerFailureKind.HostNoControl, scopeOrHostKey);
            return false;
        }

        // R4：Mounted 判定单一出口——统一经端口 IsMounted(ContainerId)（HostMountService → ContainerRegistry 实时挂载状态）。
        // 删除 handle.IsMounted（句柄快照 State==Mounted && Control!=null）与 _port.IsMounted 的双重校验冗余分支，
        // 消除「句柄快照 vs 实时状态」两套口径漂移。
        if (_port.IsMounted(handle.Id) == false)
        {
            reason = "assert-container: " + ContainerFailureMessages.Format(ContainerFailureKind.HostNotMounted, scopeOrHostKey, mountState: handle.State.ToString());
            return false;
        }

        if (!TryParseCountExpr(countExpr, out var op, out var expected))
        {
            reason = "assert-container: " + ContainerFailureMessages.Format(ContainerFailureKind.InvalidCountExpression, countExpr: countExpr);
            return false;
        }

        var actual = handle.Control.Controls.Count;
        if (!CompareCount(actual, op, expected))
        {
            reason = "assert-container: " + ContainerFailureMessages.Format(ContainerFailureKind.CountAssertionFailed, scopeOrHostKey, countExpr: countExpr, actual: actual);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 挂载断言：<paramref name="controlName"/> 已挂载到宿主 <paramref name="hostKey"/>（或任意可见窗体）。
    /// </summary>
    public bool AssertMounted(string controlName, string? hostKey, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(controlName))
        {
            reason = "validate-mount: 缺少目标控件名";
            return false;
        }

        var (control, hostId) = ResolveInHost(controlName, hostKey);
        if (control == null)
        {
            reason = hostKey != null
                ? "validate-mount: " + ContainerFailureMessages.Format(ContainerFailureKind.HostNotResolved, hostKey)
                : $"validate-mount: 控件 '{controlName}' 未挂载到任何可见窗体";
            return false;
        }

        if (hostId != null && !IsDescendant(control, hostId))
        {
            reason = "validate-mount: " + ContainerFailureMessages.Format(ContainerFailureKind.ControlNotInHostTree, controlName, host: hostKey);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 可见性断言：控件已挂载到窗体树且 Visible。
    /// </summary>
    public bool AssertVisible(string controlName, out string reason)
    {
        reason = string.Empty;
        var control = _findOrdinaryControl?.Invoke(controlName);
        if (control == null)
        {
            reason = "validate: " + ContainerFailureMessages.Format(ContainerFailureKind.TargetControlNotFound, controlName);
            return false;
        }
        if (control.FindForm() == null)
        {
            reason = $"validate: 控件 '{controlName}' 存在但未挂载到可见窗体（孤儿控件）";
            return false;
        }
        if (!control.Visible)
        {
            reason = $"validate: 控件 '{controlName}' 不可见（Visible=false）";
            return false;
        }
        return true;
    }

    // ── 私有解析辅助 ──

    private ContainerHandle? ResolveHandle(string scopeOrHostKey)
    {
        // 经端口统一解析：先按 scope（"Side"）、再按 hostKey（"sideContainer"）反查 scope 后解析。
        // 与 ResolveInHost 共用同一条解析口径，消除 assert-container / validate-mount 的寻址分裂。
        return _port?.ResolveHostByKeyOrScope(scopeOrHostKey);
    }

    private (Control? control, ContainerId? hostId) ResolveInHost(string controlName, string? hostKey)
    {
        if (!string.IsNullOrEmpty(hostKey))
        {
            // 复用 ResolveHandle 的统一解析（scope 优先 + hostKey 反查兜底），
            // 消除「validate-mount 直调 ResolveHost(hostKey) 只按 scope 查」导致的『未找到宿主』。
            var handle = ResolveHandle(hostKey);
            if (handle == null) return (null, null);

            var hostControl = handle.Control;
            if (hostControl == null) return (null, null);
            return (FindInControl(hostControl, controlName), handle.Id);
        }

        var control = _findOrdinaryControl?.Invoke(controlName);
        return (control, null);
    }

    // R2（C1a）：控件递归深搜语义统一为 WinForms 原生 Controls.Find(name, true) 一处实现。
    // 消除此前的另一套手写递归遍历（与 WinFormsControlFinder/WinFormsAbstractions 查找族重复），
    // 保证「按名递归深搜」在断言层与查找层同一语义、同一实现。
    private static Control? FindInControl(Control root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        var found = root.Controls.Find(name, true);
        return found.Length > 0 ? found[0] : null;
    }

    private bool IsDescendant(Control control, ContainerId hostId)
    {
        if (control == null) return false;
        var host = _port?.ResolveHost(hostId)?.Control;
        if (host == null) return false;

        var current = control.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, host)) return true;
            current = current.Parent;
        }
        return false;
    }

    #region 计数表达式（计数口径与断言共用）

    public enum CountOp
    {
        GreaterThan,
        GreaterOrEqual,
        Equal,
        LessThan,
        LessOrEqual,
        NotEqual,
    }

    /// <summary>
    /// 解析计数表达式——唯一权威实现（命令层只做 token 切分，不再重复解析）。
    /// 兼容两种形态：带前缀 `count&gt;0` / `count&gt;=n`（脚本/命令层传入），以及裸操作符 `&gt;0` / `&gt;=n`。
    /// </summary>
    public static bool TryParseCountExpr(string expr, out CountOp op, out int value)
    {
        op = CountOp.Equal;
        value = 0;
        if (string.IsNullOrWhiteSpace(expr)) return false;

        var e = expr.Trim();

        // 兼容 `count>0` 前缀形态：剥掉可选的 `count` token（忽略大小写）后再按裸操作符解析。
        var countPrefix = "count";
        if (e.StartsWith(countPrefix, StringComparison.OrdinalIgnoreCase))
            e = e[countPrefix.Length..].TrimStart();

        string opStr, numStr;

        if (e.StartsWith(">=")) { opStr = ">="; numStr = e[2..]; }
        else if (e.StartsWith("<=")) { opStr = "<="; numStr = e[2..]; }
        else if (e.StartsWith("==")) { opStr = "=="; numStr = e[2..]; }
        else if (e.StartsWith("!=")) { opStr = "!="; numStr = e[2..]; }
        else if (e.StartsWith('>')) { opStr = ">"; numStr = e[1..]; }
        else if (e.StartsWith('<')) { opStr = "<"; numStr = e[1..]; }
        else if (e.StartsWith('=')) { opStr = "=="; numStr = e[1..]; }
        else { opStr = "=="; numStr = e; }

        if (!int.TryParse(numStr.Trim(), out value)) return false;

        op = opStr switch
        {
            ">" => CountOp.GreaterThan,
            ">=" => CountOp.GreaterOrEqual,
            "==" => CountOp.Equal,
            "<" => CountOp.LessThan,
            "<=" => CountOp.LessOrEqual,
            "!=" => CountOp.NotEqual,
            _ => CountOp.Equal,
        };
        return true;
    }

    public static bool CompareCount(int actual, CountOp op, int expected)
        => op switch
        {
            CountOp.GreaterThan => actual > expected,
            CountOp.GreaterOrEqual => actual >= expected,
            CountOp.Equal => actual == expected,
            CountOp.LessThan => actual < expected,
            CountOp.LessOrEqual => actual <= expected,
            CountOp.NotEqual => actual != expected,
            _ => false,
        };

    #endregion
}
