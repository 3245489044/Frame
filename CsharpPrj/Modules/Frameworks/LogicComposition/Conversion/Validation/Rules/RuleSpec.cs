using System;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 闸A 规则数据（规则表的行 = 一条 <see cref="RuleSpec"/>）：判据从 C# 代码下沉为**数据**，
/// 执行器仍是内核唯一 <c>GateEngine&lt;TModel&gt;</c>（本类型不含任何求值逻辑）。
/// <para>
/// 严重度复用内核 <see cref="ConversionSeverity"/>（Error/Warning/Info）——**不**新建第二套严重度枚举，
/// 保证诊断面单源（对齐 RuleSpec §3.2 I4「诊断仍为内核 ConversionDiagnostic」）。
/// </para>
/// <para>
/// 校验分工：形状（节点白名单 / 形参 / 作用域文法）在本类 <see cref="TryValidate"/> 内完成；
/// **码段归属**（<c>Code</c> ∈ 包码段）与**白名单键注册**（<c>enum-in</c>/<c>key-in-whitelist</c>/
/// <c>ref-declared</c> 的键须已注册）由 <c>RuleSpecEngine.Build</c> 结合 <c>IDomainPack</c> 与
/// <c>IPredicateRegistry</c> 完成（本类不依赖它们，保持领域无关）。
/// </para>
/// </summary>
public sealed class RuleSpec
{
    /// <summary>规则名（诊断归因与去重键；对齐 <c>IGateRule.Name</c>）。</summary>
    public string Name { get; }

    /// <summary>诊断码（须属本包码段，见类型注释的校验分工）。</summary>
    public string Code { get; }

    /// <summary>严重度（复用内核三级枚举）。</summary>
    public ConversionSeverity Severity { get; }

    /// <summary>判定作用域（<c>root</c> 或段序文法）。</summary>
    public RuleSpecScope Scope { get; }

    /// <summary>判据谓词（封闭白名单表达式树）。</summary>
    public PredicateExpr Require { get; }

    /// <summary>消息模板（支持 <c>{path}</c> 等占位由引擎替换；本类型不解析）。</summary>
    public string Message { get; }

    /// <summary>定位提示路径（缺省 null = 引擎按作用域回退定位，语义同既有 SpanOf/SpanOr）。</summary>
    public string? SpanHint { get; }

    /// <summary>构筑规则数据（必填项缺失即抛；形状非法由 <see cref="TryValidate"/> 报出）。</summary>
    /// <param name="name">规则名。</param>
    /// <param name="code">诊断码。</param>
    /// <param name="severity">严重度。</param>
    /// <param name="scope">判定作用域。</param>
    /// <param name="require">判据谓词；null 视为非法形状（由 <see cref="TryValidate"/> 报出）。</param>
    /// <param name="message">消息模板。</param>
    /// <param name="spanHint">定位提示路径（可空）。</param>
    public RuleSpec(
        string name,
        string code,
        ConversionSeverity severity,
        RuleSpecScope scope,
        PredicateExpr require,
        string message,
        string? spanHint = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Severity = severity;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Require = require;
        Message = message ?? string.Empty;
        SpanHint = spanHint;
    }

    /// <summary>
    /// 形状校验（筑期用）：规则名 / 码 / 消息非空 ∧ 作用域非 null ∧ 谓词白名单合法。
    /// 非法返回 false 并给出可定位原因（调用方 <c>RuleSpecEngine.Build</c> 按硬失败处理）。
    /// </summary>
    /// <param name="error">out：失败原因（成功为 null）。</param>
    public bool TryValidate(out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "规则名为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            error = "规则 " + Name + " 的诊断码为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            error = "规则 " + Name + " 的消息模板为空";
            return false;
        }

        if (Scope is null)
        {
            error = "规则 " + Name + " 的作用域为空";
            return false;
        }

        if (Require is null)
        {
            error = "规则 " + Name + " 的判据（Require）为空";
            return false;
        }

        if (!Require.TryValidate(out var reason))
        {
            error = "规则 " + Name + " 的判据非法：" + reason;
            return false;
        }

        if (SpanHint is not null && string.IsNullOrWhiteSpace(SpanHint))
        {
            error = "规则 " + Name + " 的 SpanHint 为空串（应省略或给出路径）";
            return false;
        }

        return true;
    }

    /// <summary>规范化单行描述（供构筑期消息与测试断言）。</summary>
    public override string ToString()
        => Name + "[" + Code + "/" + Severity + " @" + Scope + "] " + Require;
}
