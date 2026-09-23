using System.Reflection;

namespace DataCenter.Protocol.Standards;

/// <summary>
/// <see cref="ExprStandard"/> 子节点枚举**单源**（R3）：直接 <see cref="ExprStandard"/> 子节点 + 拼接段。
/// <para>
/// 动机：此前内核（<c>ScriptedSteps.Subs</c>）与守卫（<c>ScriptedDeclarationGuardTests.Subs</c>）
/// 各持一份**手工**枚举清单，靠人工同步；实测两份**完全相同且都已漏** <c>Eq</c>/<c>Ne</c>/<c>Gt</c>/<c>Ge</c>/
/// <c>Lt</c>/<c>Le</c> 六个比较算子族（新增算子必然再漏）。
/// </para>
/// <para>
/// 口径：反射按**属性声明序**（<see cref="MemberInfo.MetadataToken"/>）枚举，
/// 单值属性（<see cref="ExprStandard"/> 型）+ 列表属性（<c>IReadOnlyList&lt;ExprStandard&gt;</c> 型）
/// + <see cref="ExprStandard.Parts"/> 的 <c>Expr</c>/<c>When</c>；新增 ExprStandard 型算子无需改本件。
/// </para>
/// <para>
/// 边界（有意不纳入）：<c>Source</c>/<c>FoldInit</c>（<see cref="ValueSourceStandard"/> 型）、
/// <c>FoldStep</c>（<see cref="ChainStandard"/> 型）、<c>Invoke</c>（<see cref="InvokeStandard"/> 型）
/// 不是 ExprStandard 子节点，其内部子树由各自的取值来源 / 调用校验路径负责（本件只回答「ExprStandard 的直接子节点」）。
/// </para>
/// </summary>
public static class ExprStandardWalker
{
    /// <summary>单值子节点属性（声明序）。</summary>
    private static readonly PropertyInfo[] DirectProperties =
        typeof(ExprStandard).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(ExprStandard))
            .OrderBy(property => property.MetadataToken)
            .ToArray();

    /// <summary>列表子节点属性（声明序）。</summary>
    private static readonly PropertyInfo[] ListProperties =
        typeof(ExprStandard).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(IReadOnlyList<ExprStandard>))
            .OrderBy(property => property.MetadataToken)
            .ToArray();

    /// <summary>
    /// 直接子节点（声明序；不含自身，不做去重 —— 同一节点被两处引用即出现两次，遍历方各自按需处理）。
    /// </summary>
    public static IReadOnlyList<ExprStandard> Children(ExprStandard expr)
    {
        ArgumentNullException.ThrowIfNull(expr);

        var children = new List<ExprStandard>();

        foreach (var property in DirectProperties)
        {
            if (property.GetValue(expr) is ExprStandard child) children.Add(child);
        }

        foreach (var property in ListProperties)
        {
            if (property.GetValue(expr) is IEnumerable<ExprStandard> items)
            {
                foreach (var item in items)
                {
                    if (item is not null) children.Add(item);
                }
            }
        }

        if (expr.Parts is { Count: > 0 } parts)
        {
            foreach (var part in parts)
            {
                if (part is null) continue;
                children.Add(part.Expr);
                if (part.When is { } gate) children.Add(gate);
            }
        }

        return children;
    }
}
