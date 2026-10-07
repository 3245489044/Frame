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
/// <para>
/// 两形态互斥（NG-1）：单件（<see cref="Name"/> 字面量）｜批量（<see cref="Batch"/> 集合 +
/// <see cref="NameExpr"/> 逐元素文件名）。批量 = 按声明**结构映射**生成 N 个文件（非控制流）：
/// 逐元素以 <see cref="As"/> 帧名入栈，<see cref="NameExpr"/>/<see cref="Dir"/>/<see cref="Content"/>/
/// <see cref="When"/> 均在元素帧内求值。
/// </para>
/// </summary>
/// <param name="Name">文件名（单件形态；相对 <see cref="Dir"/>）。批量形态下为 null。</param>
/// <param name="Dir">相对子目录表达式（缺省 = 作用域根）。</param>
/// <param name="From">内容来源（缺省 = 空文件）。</param>
/// <param name="Content">内容表达式（缺省 = <see cref="From"/> 取值的文本形式）。</param>
/// <param name="When">写盘门控（求值为假即跳过该文件）。</param>
/// <param name="Batch">批量形态的集合来源（元素序列；缺省 = 单件形态）。</param>
/// <param name="As">批量形态的元素帧名（缺省 <c>item</c>；供 <c>from:item</c> 引用当前元素）。</param>
/// <param name="NameExpr">批量形态的逐元素文件名表达式（须求值为文本且不含路径分隔符）。</param>
public sealed record FileStandard(
    string? Name = null,
    ExprStandard? Dir = null,
    ValueSourceStandard? From = null,
    ExprStandard? Content = null,
    ExprStandard? When = null,
    ValueSourceStandard? Batch = null,
    string? As = null,
    ExprStandard? NameExpr = null);
