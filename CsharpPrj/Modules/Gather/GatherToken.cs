#nullable enable

using System;

namespace Modules.Gather;

/// <summary>
/// 强类型取数令牌（**禁裸字符串取数**）：token 由声明面生成/校验，取数只经 <see cref="GatherHub.Get{T}"/>。
/// <para>Domain+Scope 即框架复用键；调用方无法绕过声明临时拼一个键。</para>
/// </summary>
public readonly record struct GatherToken<T>(string Domain, string Scope)
{
    /// <summary>按域/作用域声明令牌（值由声明面校验，未知域在 Hub 取数时即失败）。</summary>
    public static GatherToken<T> For(string domain, string scope)
        => new(domain ?? throw new ArgumentNullException(nameof(domain)),
               scope ?? throw new ArgumentNullException(nameof(scope)));
}

/// <summary>产物令牌（出口闭合）：写出只经 <see cref="GatherHub.Emit"/> 命中已声明 Produce。</summary>
public readonly record struct GatherProduceToken(string Name)
{
    /// <summary>按产物名声明令牌。</summary>
    public static GatherProduceToken For(string name)
        => new(name ?? throw new ArgumentNullException(nameof(name)));
}
