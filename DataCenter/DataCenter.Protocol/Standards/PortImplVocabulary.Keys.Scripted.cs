namespace DataCenter.Protocol.Standards;

/// <summary>
/// 脚本族逐层键白名单（<see cref="PortImplVocabulary"/> 的脚本部分；分文件仅为可读性，语义同一处）。
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary>脚本步骤键（<see cref="StepStandard"/>；<c>foreach</c> 形态的键与步骤同层）。</summary>
    public static readonly IReadOnlyList<string> StepKeys =
        ["collect", "when", "foreach", "items", "as", "where", "do", "new", "invoke"];

    /// <summary>调用步骤键（<see cref="InvokeStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> InvokeKeys =
    [
        "as", "dep", "target", "method", "property", "await", "expect", "args", "out", "break",
        "genericArgs", "on", "nonPublic",
    ];

    /// <summary>构造步骤键（<see cref="NewStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> NewKeys =
        ["as", "type", "genericArgs", "args", "chain", "init"];

    /// <summary>初始化项键（<see cref="InitStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> InitKeys = ["member", "value"];

    /// <summary>链式调用键（<see cref="ChainStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ChainKeys = ["method", "args", "rebind"];

    /// <summary>委托工厂键（<see cref="DelegateStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> DelegateKeys =
        ["kind", "steps", "result", "parameters", "collect", "await"];

    /// <summary>进入期留存键（<see cref="SaveStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> SaveKeys = ["slot", "type", "key", "genericArgs"];

    /// <summary>收集声明键（<see cref="CollectStandard"/>；裸字符串 = 槽名的简写形态）。</summary>
    public static readonly IReadOnlyList<string> CollectKeys = ["slot", "from"];

    /// <summary>作用域声明键（<see cref="ScopeStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ScopeKeys =
        ["kind", "as", "prefix", "files", "invoke", "save", "open", "close"];

    /// <summary>作用域预置文件键（<see cref="FileStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> FileKeys = ["name", "dir", "from", "content", "when"];

    /// <summary>载体声明键（<see cref="DeliverStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> DeliverKeys =
        ["kind", "defer", "issues", "issueField", "capture", "value"];

    /// <summary>issue 捕获键（<see cref="DeliverCaptureStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> CaptureKeys =
        ["call", "as", "dep", "target", "method", "await", "args", "out", "expect", "message", "messageField", "when"];

    /// <summary>场景表键（<see cref="ScenarioStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ScenarioKeys = ["select", "cases"];

    /// <summary>拼接段键（<see cref="ConcatPartStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ConcatPartKeys = ["expr", "when"];

    /// <summary>投影项键（<see cref="FieldExprStandard"/>；算子可直接平铺在项上）。</summary>
    public static readonly IReadOnlyList<string> FieldExprKeys = ["field", "expr"];
}
