namespace DataCenter.Protocol.Standards;

/// <summary>
/// 脚本族逐层键白名单（<see cref="PortImplVocabulary"/> 的脚本部分；分文件仅为可读性，语义同一处）。
/// </summary>
public static partial class PortImplVocabulary
{
    /// <summary>
    /// 脚本步骤键（<see cref="StepStandard"/>；四形态键 + 值源步骤（from:list/map）同层载荷键的**并集**，
    /// 与 schema <c>$defs/step</c> 的扁平闭合同口径；形态判别由读取器 fail-fast，跨形态杂键即「未声明形态」）。
    /// </summary>
    public static readonly IReadOnlyList<string> StepKeys =
        [
            "collect", "when", "foreach", "items", "as", "where", "do", "new", "invoke", "throw",
            "from", "pairs", "elementType", "genericArgs", "expr",
        ];

    /// <summary>主动抛出步骤键（<see cref="ThrowStandard"/>；G3）。</summary>
    public static readonly IReadOnlyList<string> ThrowKeys = ["type", "message"];

    /// <summary>调用步骤键（<see cref="InvokeStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> InvokeKeys =
    [
        "as", "dep", "target", "method", "property", "await", "expect", "args", "out", "break",
        "genericArgs", "on", "nonPublic", "keepTask",
    ];

    /// <summary>
    /// 构造步骤键（<see cref="NewStandard"/>）。
    /// <para><c>expect</c> = 构造期期望异常类型集（<c>|</c> 分隔全名）：命中 ⇒ 结果为该异常对象（供
    /// <c>typeName</c>/<c>message</c> 投影），构造未抛或类型不符 ⇒ 结果为构造实例 / 原样上抛（口径同 <c>invoke.expect</c>）。</para>
    /// </summary>
    public static readonly IReadOnlyList<string> NewKeys =
        ["as", "type", "genericArgs", "args", "chain", "init", "expect"];

    /// <summary>初始化项键（<see cref="InitStandard"/>；<c>key</c> = 容器写入键，G7）。</summary>
    public static readonly IReadOnlyList<string> InitKeys = ["member", "value", "key"];

    /// <summary>链式调用键（<see cref="ChainStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ChainKeys = ["method", "args", "rebind"];

    /// <summary>委托工厂键（<see cref="DelegateStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> DelegateKeys =
        ["kind", "steps", "result", "parameters", "collect", "await"];

    /// <summary>进入期留存键（<see cref="SaveStandard"/>；<c>set</c>/<c>restore</c> = NG-2 进入期设值 + 退出期自动还原）。</summary>
    public static readonly IReadOnlyList<string> SaveKeys =
        ["slot", "type", "key", "genericArgs", "set", "restore"];

    /// <summary>收集声明键（<see cref="CollectStandard"/>；裸字符串 = 槽名的简写形态）。</summary>
    public static readonly IReadOnlyList<string> CollectKeys = ["slot", "from"];

    /// <summary>作用域声明键（<see cref="ScopeStandard"/>）。</summary>
    public static readonly IReadOnlyList<string> ScopeKeys =
        ["kind", "as", "prefix", "files", "invoke", "save", "open", "close"];

    /// <summary>作用域预置文件键（<see cref="FileStandard"/>；<c>batch</c>/<c>as</c> = NG-1 按声明生成 N 文件）。</summary>
    public static readonly IReadOnlyList<string> FileKeys =
        ["name", "dir", "from", "content", "when", "batch", "as"];

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
