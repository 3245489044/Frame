
#nullable enable

using System;
using System.Windows.Forms;
using TConfig.ContainerAssertions;

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器断言服务（ContainerAssertionService）——脚本断言与框架自检复用的单一断言权威。
/// 结构固定为「定位（<see cref="Locate"/>）→ 判据（Assert*）→ 诊断（<see cref="ContainerDiagnosis"/>）」三段：
/// 1）定位只回答「在哪儿 / 为什么找不到」，类别不折叠（宿主未解析 ≠ 宿主内控件缺失 ≠ 孤儿控件）；
/// 2）判据只回答「已定位的对象是否满足断言」（计数 / 挂载 / 可见）；
/// 3）诊断承载机读类别 + 事实 + 唯一文案，命令层不得自行拼装失败文本。
/// 语义全部经 <see cref="IContainerAccessPort"/> 解析，命令层（UiCommandSystems）仅做薄语法适配；
/// 脚本断言与框架自检（P7 审计 / 回归脚本）同口径（CountableOrderBridge：统一比较域）。
/// </summary>
public sealed class ContainerAssertionService
{
    /// <summary>诊断证据样本上限：证据要可读，不刷屏。</summary>
    private const int CandidateSampleSize = 24;

    /// <summary>诊断证据遍历深度上限（渲染根 → 视图 → 控件通常 ≤ 4 层）。</summary>
    private const int CandidateSampleMaxDepth = 8;

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

    // ── 定位（唯一入口，类别不折叠） ──

    /// <summary>
    /// 定位目标控件：<paramref name="hostKeyOrScope"/> 非空时在宿主树内定位（scope 优先 + hostKey 反查，唯一寻址口径），
    /// 为空时按可见窗体树定位（普通控件语义）。所有失败类别逐类区分并返回，供上层直接诊断。
    /// </summary>
    public ContainerLookup Locate(string controlName, string? hostKeyOrScope)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return new ContainerLookup { Kind = ContainerFailureKind.TargetControlNotFound, Subject = controlName };

        if (!string.IsNullOrWhiteSpace(hostKeyOrScope))
        {
            if (_port == null)
                return new ContainerLookup
                {
                    Kind = ContainerFailureKind.AccessPortUnavailable,
                    Subject = controlName,
                    HostKey = hostKeyOrScope,
                };

            var handle = _port.ResolveHostByKeyOrScope(hostKeyOrScope);
            if (handle == null)
                return new ContainerLookup
                {
                    Kind = ContainerFailureKind.HostNotResolved,
                    Subject = controlName,
                    HostKey = hostKeyOrScope,
                };

            var hostControl = handle.Control;
            if (hostControl == null)
                return new ContainerLookup
                {
                    Kind = ContainerFailureKind.HostNoControl,
                    Subject = controlName,
                    HostKey = hostKeyOrScope,
                    Host = handle,
                };

            var found = FindInControl(hostControl, controlName);
            if (found != null)
                return new ContainerLookup
                {
                    Kind = ContainerFailureKind.None,
                    Control = found,
                    Host = handle,
                    Subject = controlName,
                    HostKey = hostKeyOrScope,
                };

            // 宿主已解析：不是挂载断链，而是「目标名不在该宿主的树里」——附现有子控件样本作为证据。
            return new ContainerLookup
            {
                Kind = ContainerFailureKind.ControlNotInHostTree,
                Subject = controlName,
                HostKey = hostKeyOrScope,
                Host = handle,
                Candidates = SampleReachableNames(hostControl),
            };
        }

        var ordinary = _findOrdinaryControl?.Invoke(controlName);
        if (ordinary == null)
            return new ContainerLookup { Kind = ContainerFailureKind.TargetControlNotFound, Subject = controlName };

        // 「控件对象存在 ≠ 已挂载可见」：FindForm()==null 即孤儿控件（池直查命中的未挂载对象）。
        if (ordinary.FindForm() == null)
            return new ContainerLookup { Kind = ContainerFailureKind.OrphanControl, Subject = controlName, Control = ordinary };

        return new ContainerLookup { Kind = ContainerFailureKind.None, Control = ordinary, Subject = controlName };
    }

    // ── 判据（只判已定位对象是否满足断言） ──

    /// <summary>
    /// 容器归属断言：宿主已解析 + 已挂载 + 直接子控件数满足 <paramref name="countExpr"/>（count&gt;0 / count==n / count&gt;=n / count&lt;n）。
    /// </summary>
    public ContainerDiagnosis AssertContainer(string scopeOrHostKey, string countExpr)
    {
        if (_port == null)
            return ContainerDiagnosis.Of(ContainerFailureKind.AccessPortUnavailable,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr);

        var handle = _port.ResolveHostByKeyOrScope(scopeOrHostKey);
        if (handle == null)
            return ContainerDiagnosis.Of(ContainerFailureKind.HostNotResolved,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr);

        if (handle.Control == null)
            return ContainerDiagnosis.Of(ContainerFailureKind.HostNoControl,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr);

        // R4：Mounted 判定单一出口——统一经端口 IsMounted(ContainerId)（HostMountService → ContainerRegistry 实时挂载状态）。
        if (_port.IsMounted(handle.Id) == false)
            return ContainerDiagnosis.Of(ContainerFailureKind.HostNotMounted,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr,
                mountState: handle.State.ToString());

        if (!TryParseCountExpr(countExpr, out var op, out var expected))
            return ContainerDiagnosis.Of(ContainerFailureKind.InvalidCountExpression,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr);

        var actual = handle.Control.Controls.Count;
        if (!CompareCount(actual, op, expected))
            return ContainerDiagnosis.Of(ContainerFailureKind.CountAssertionFailed,
                subject: scopeOrHostKey, host: scopeOrHostKey, expected: countExpr, actual: actual);

        return ContainerDiagnosis.Success(scopeOrHostKey, actual);
    }

    /// <summary>
    /// 挂载断言：<paramref name="controlName"/> 已挂载到宿主 <paramref name="hostKey"/>（hostKey 为空则按可见窗体树判定），
    /// <paramref name="requireVisible"/> 为 true 时追加「控件可见」判据（一个命令走完定位+挂载+可见，避免二次调用产生两套文案）。
    /// </summary>
    public ContainerDiagnosis AssertMounted(string controlName, string? hostKey, bool requireVisible = false)
    {
        var lookup = Locate(controlName, hostKey);
        if (!lookup.IsFound)
            return lookup.ToDiagnosis();

        if (requireVisible && lookup.Control!.Visible == false)
            return ContainerDiagnosis.Of(ContainerFailureKind.ControlNotVisible, subject: controlName, host: hostKey);

        return ContainerDiagnosis.Success(controlName);
    }

    /// <summary>可见性断言：控件已挂载到可见窗体树且 Visible。</summary>
    public ContainerDiagnosis AssertVisible(string controlName)
        => AssertMounted(controlName, hostKey: null, requireVisible: true);

    // ── 私有解析辅助 ──

    /// <summary>
    /// 诊断证据：宿主内**可到达**的控件名样本。
    /// 只取直接子控件不够——渲染根（<c>root:{scope}</c>）通常独占宿主，直接子控件只有这一个合成名，
    /// 证据等于没有（实测：报「宿主现有子控件: root:Content」，仍看不出面板里到底有什么）。
    /// 因此按树深度优先收集后代名（跳过合成根名），使「目标不存在」能直接看到实际存在的控件。
    /// </summary>
    private static IReadOnlyList<string>? SampleReachableNames(Control host)
    {
        var names = new List<string>();
        Collect(host, names, depth: 0);
        return names.Count == 0 ? null : names;
    }

    private static void Collect(Control parent, List<string> names, int depth)
    {
        if (depth > CandidateSampleMaxDepth || names.Count >= CandidateSampleSize) return;

        foreach (Control child in parent.Controls)
        {
            if (names.Count >= CandidateSampleSize) return;

            // 合成根名（root:{scope}）不是配置声明的控件名，作证据只会造成干扰，跳过但其子节点继续收集。
            if (!string.IsNullOrEmpty(child.Name) && !child.Name.Contains(':'))
                names.Add(child.Name);

            Collect(child, names, depth + 1);
        }
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
