namespace ContractFramework.Abstractions;

/// <summary>
/// 执行体种类（受控词；禁散落字面量）。对应文档 §6.1 内置适配器一览。
/// </summary>
public enum ContractHostKind
{
    /// <summary>C# 直调装配期执行体（<c>ValidationRuleContractHost</c> 等）。</summary>
    DotNet = 0,

    /// <summary>编译期代码分析（<c>Plate/RoslynMethod</c> 侧）。</summary>
    Roslyn = 1,

    /// <summary>门禁脚本侧（<c>Check-*.ps1</c> 退出码 → 裁决）。</summary>
    PowerGate = 2,

    /// <summary>宿主运行期探针（断言 → 裁决 + <c>Heal</c>）。</summary>
    Probe = 3,
}

/// <summary>
/// 执行体引用（声明面 <c>host</c> 节点）：种类（受控词）+ 实现落点（类型全名或仓库相对路径）。
/// </summary>
public sealed class ContractHostRef
{
    private ContractHostRef(ContractHostKind kind, string impl, bool kindRecognized)
    {
        Kind = kind;
        Impl = impl;
        IsKindRecognized = kindRecognized;
    }

    /// <summary>执行体种类。</summary>
    public ContractHostKind Kind { get; }

    /// <summary>实现落点（类型全名如 <c>UiBackend.Html.HtmlKeyContract</c>，或仓库相对路径）。</summary>
    public string Impl { get; }

    /// <summary>种类是否在受控词白名单内。</summary>
    public bool IsKindRecognized { get; }

    /// <summary>非空且种类已识别。</summary>
    public bool IsWellFormed => IsKindRecognized && !string.IsNullOrWhiteSpace(Impl);

    /// <summary>非法引用占位（缺 <c>host</c> 时用）。</summary>
    public static ContractHostRef Invalid { get; } =
        new(ContractHostKind.DotNet, string.Empty, kindRecognized: false);

    /// <summary>受控词种类解析。</summary>
    public static bool TryParseKind(string? kind, out ContractHostKind parsed)
    {
        if (Enum.TryParse(kind, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed))
        {
            return true;
        }

        parsed = ContractHostKind.DotNet;
        return false;
    }

    /// <summary>宽容构造。</summary>
    public static ContractHostRef Create(string? kind, string? impl)
    {
        var recognized = TryParseKind(kind, out var parsed);
        return new ContractHostRef(parsed, (impl ?? string.Empty).Trim(), recognized);
    }
}
