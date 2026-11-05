using System;
using System.Text;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// 层级实体统一定位（任意层通用）：层 Id + 实体 Id（+ 可选作用域）。
/// <para>
/// 取代"每层一个枚举值 + 每层一套寻址规则"的固化形态——层由外部 <see cref="HierarchySchema"/> 声明，
/// 本类型只承载"某层的某个实体"这一中立事实，层数不受编译期约束。
/// </para>
/// <para>
/// 实体同一性由本记录的**值相等**给出（层 Id + 作用域 + Id）：去重集 / 环守卫一律以此为准。
/// <see cref="Key"/> 是同一信息的人类可读编码，供日志与跨进程键（其分量已转义，不会因分隔符位置不同而碰撞）。
/// </para>
/// </summary>
/// <param name="LevelId">层标识（对应 <see cref="HierarchyLevelSpec.LevelId"/>）。</param>
/// <param name="Id">实体 Id（层内唯一）。</param>
/// <param name="Scope">可选作用域（如工作流命名空间）；仅参与唯一键，不参与层判定。</param>
public readonly record struct LevelRef(string LevelId, string Id, string? Scope = null)
{
    /// <summary>是否为空引用（层或 Id 缺失）。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(LevelId) || string.IsNullOrWhiteSpace(Id);

    /// <summary>
    /// 运行时唯一键：<c>{LevelId}:{Id}</c>，带作用域时为 <c>{LevelId}:{Scope}.{Id}</c>。
    /// 分量内的 <c>:</c> / <c>.</c> / <c>\</c> 会被转义，故 <c>("a", "b:c")</c> 与 <c>("a:b", "c")</c> 不会碰撞。
    /// </summary>
    public string Key => string.IsNullOrWhiteSpace(Scope)
        ? $"{Escape(LevelId)}:{Escape(Id)}"
        : $"{Escape(LevelId)}:{Escape(Scope!)}.{Escape(Id)}";

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(':', '.', '\\') < 0) return value;
        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            if (ch is ':' or '.' or '\\') builder.Append('\\');
            builder.Append(ch);
        }
        return builder.ToString();
    }

    /// <summary>构造定位（可读式工厂）。</summary>
    public static LevelRef Of(string levelId, string id, string? scope = null) => new(levelId, id, scope);

    /// <summary>缺失层 / Id 时抛错（用于装配期显式校验，避免空引用静默流入引擎）。</summary>
    public LevelRef EnsureValid()
    {
        if (IsEmpty)
            throw new ArgumentException($"LevelRef 非法：LevelId/Id 不可为空（LevelId='{LevelId}', Id='{Id}'）。");
        return this;
    }

    public override string ToString() => Key;
}
