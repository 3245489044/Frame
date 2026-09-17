namespace DataCenter.Protocol.Standards;

/// <summary>
/// 拼接段（标准框架通用化重构 §2.2 第 20 项；对应内核 <c>ScriptedConcatPartSpec</c>）：
/// <see cref="Expr"/> 的文本形式按序拼接；<see cref="When"/> 求值为假即省略该段（条件行省略）。
/// </summary>
/// <param name="Expr">段表达式（其文本形式为段文本）。</param>
/// <param name="When">段门控（缺省 = 恒参与）。</param>
public sealed record ConcatPartStandard(ExprStandard Expr, ExprStandard? When = null);

/// <summary>
/// 产物字段 ← 投影表达式（标准框架通用化重构 §2.2 第 18 项；对应内核 <c>ScriptedFieldSpec</c>）：
/// 字段序 = 产物序；字段名须与契约 <c>out.fields</c> 逐字同序（装载期由 C13 对账）。
/// </summary>
/// <param name="Field">产物字段名。</param>
/// <param name="Expr">取值表达式。</param>
public sealed record FieldExprStandard(string Field, ExprStandard Expr);
