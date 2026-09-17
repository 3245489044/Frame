namespace DataCenter.Protocol.Standards;

/// <summary>
/// 作用域声明（标准框架通用化重构 §2.2 第 16 项；对应内核 <c>ScriptedScopeSpec</c>）：
/// <c>tempDir</c>（临时目录）｜<c>using</c>（资源释放）｜<c>restore</c>（静态槽还原）；
/// 进入前建立 / 留存，退出（正常与异常路径）均回收。
/// </summary>
/// <param name="Kind">作用域种类（取值域见 <see cref="PortImplVocabulary.ScopeKinds"/>）。</param>
/// <param name="As">作用域名（值源 <c>tmpPath</c> / <c>tmpDir</c> 据它解析）。</param>
/// <param name="Prefix">目录名前缀（诊断可读；缺省 <c>spec</c>）。</param>
/// <param name="Files">预置文件序列（声明序 = 写盘序）。</param>
/// <param name="Invoke">进入期调用（<c>kind=using</c>：结果写槽 <see cref="As"/>，退出时若可释放即 <c>Dispose</c>）。</param>
/// <param name="Save">进入期留存项（<c>kind=restore</c>：静态成员当前值 → 槽）。</param>
/// <param name="Open">进入期步骤（<c>kind=restore</c>：在 <see cref="Save"/> 之后执行）。</param>
/// <param name="Close">退出期步骤（<c>kind=restore</c>：正常与异常路径均执行）。</param>
public sealed record ScopeStandard(
    string Kind,
    string As,
    string? Prefix = null,
    IReadOnlyList<FileStandard>? Files = null,
    InvokeStandard? Invoke = null,
    IReadOnlyList<SaveStandard>? Save = null,
    IReadOnlyList<StepStandard>? Open = null,
    IReadOnlyList<StepStandard>? Close = null);

/// <summary>
/// 作用域内预置文件（标准框架通用化重构 §2.2 第 17 项；对应内核 <c>ScriptedFileSpec</c>）：
/// <see cref="Name"/> 不得含路径分隔符；<see cref="Dir"/> 为表达式，不做文本插值方言。
/// </summary>
/// <param name="Name">文件名（相对 <see cref="Dir"/>）。</param>
/// <param name="Dir">相对子目录表达式（缺省 = 作用域根）。</param>
/// <param name="From">内容来源（缺省 = 空文件）。</param>
/// <param name="Content">内容表达式（缺省 = <see cref="From"/> 取值的文本形式）。</param>
/// <param name="When">写盘门控（求值为假即跳过该文件）。</param>
public sealed record FileStandard(
    string Name,
    ExprStandard? Dir = null,
    ValueSourceStandard? From = null,
    ExprStandard? Content = null,
    ExprStandard? When = null);
