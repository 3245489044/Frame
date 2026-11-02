using System.Collections.Generic;
using System.Linq;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>一个 <c>[Const]</c> 成员（名 + 值原文；值逐字，禁改写）。</summary>
/// <param name="Name">成员名（逐字）。</param>
/// <param name="Value">值表达式原文（字符串字面量 / 整数字面量 / nameof / 限定的成员引用）。</param>
public sealed record DeclaredConstantMember(string Name, string Value);

/// <summary>一个 <c>[Const]</c> 单元（目标类型身份 + 成员表）。</summary>
/// <param name="Target">目标类型全名（命名空间.类型）。</param>
/// <param name="Kind">类型种类（class/record/struct）。</param>
/// <param name="Modifier">类型修饰（static/sealed/abstract/plain）。</param>
/// <param name="ValueType">成员值类型（string/int）。</param>
/// <param name="Members">成员表（声明序）。</param>
public sealed record DeclaredConstantUnit(
    string Target, string Kind, string Modifier, string ValueType,
    IReadOnlyList<DeclaredConstantMember> Members);

/// <summary>
/// <c>[Const]</c> 发射器：编译产物 → 冻结常量单元表（manifest <c>constants</c> 段）。
/// <para>
/// 口径：<b>只发射、不裁决</b>（形态/Range 判据归闸A；类型存在性归生成器）；
/// 序 = target 升序 → valueType 升序（同 target 多单元按值类型拆分），成员保声明序（位置即语义）。
/// </para>
/// </summary>
public static class UsharpConstantsEmitter
{
    /// <summary>发射全部常量单元（无声明 ⇒ 空表，不产出伪造项）。</summary>
    /// <param name="compilation">已编译声明面。</param>
    public static IReadOnlyList<DeclaredConstantUnit> Emit(UsharpCompilation compilation)
    {
        var units = new List<DeclaredConstantUnit>();
        foreach (var unit in compilation.Consts)
        {
            units.Add(new DeclaredConstantUnit(
                unit.Target,
                unit.Kind,
                unit.Modifier,
                unit.ValueType,
                unit.Entries
                    .Select(e => new DeclaredConstantMember(e.Key, e.Value))
                    .ToList()));
        }

        return units
            .OrderBy(u => u.Target, System.StringComparer.Ordinal)
            .ThenBy(u => u.ValueType, System.StringComparer.Ordinal)
            .ToList();
    }
}
