#nullable enable

using System;

namespace Share.Composition.Text;

/// <summary>
/// 模板槽名合法性规则 —— 决定 <c>{...}</c> 中的 token 是否被当作槽。
/// <para>
/// 默认 <see cref="Identifier"/> 与 Agent 既有 <c>NodePromptComposer.IsSlotName</c> 对齐：
/// 仅字母/下划线开头、字母数字/下划线/点/横线/冒号组成的 token 才算槽。
/// 因此模板里的 JSON 对象（如 <c>{"a":1}</c>）不会被误判为槽，也不会被缺槽占位破坏。
/// </para>
/// </summary>
public sealed class TemplateSlotNameRules
{
    private readonly Func<string, bool> _isValid;

    private TemplateSlotNameRules(Func<string, bool> isValid, string name)
    {
        _isValid = isValid ?? throw new ArgumentNullException(nameof(isValid));
        Name = name;
    }

    /// <summary>规则名（诊断用）。</summary>
    public string Name { get; }

    /// <summary>标识符规则：字母/下划线开头；其余字母数字/下划线/点/横线/冒号。</summary>
    public static TemplateSlotNameRules Identifier { get; } = new(IsIdentifier, "Identifier");

    /// <summary>任意非空白 token 都视为槽（兼容旧 <c>TemplateFormatter</c> 的宽松语义）。</summary>
    public static TemplateSlotNameRules Any { get; } = new(static token => token.Trim().Length > 0, "Any");

    /// <summary>任意非空 token 都视为槽（含纯空白 token；与 <c>TemplateFormatter</c> 的 <c>[^{}]+</c> 口径一致）。</summary>
    public static TemplateSlotNameRules AnyToken { get; } = new(static token => token.Length > 0, "AnyToken");

    /// <summary>自定义规则（测试/特殊声明面用）。</summary>
    public static TemplateSlotNameRules Custom(Func<string, bool> isValid, string name = "Custom")
    {
        ArgumentNullException.ThrowIfNull(isValid);
        return new TemplateSlotNameRules(isValid, name is { Length: > 0 } ? name : "Custom");
    }

    /// <summary>token 是否为合法槽名（空/空白一律非法）。</summary>
    public bool IsValid(string? token)
        => token is { Length: > 0 } value && _isValid(value);

    private static bool IsIdentifier(string token)
    {
        if (token.Length == 0) return false;
        if (!char.IsLetter(token[0]) && token[0] != '_') return false;

        for (var i = 1; i < token.Length; i++)
        {
            var c = token[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.' && c != '-' && c != ':')
                return false;
        }

        return true;
    }
}
