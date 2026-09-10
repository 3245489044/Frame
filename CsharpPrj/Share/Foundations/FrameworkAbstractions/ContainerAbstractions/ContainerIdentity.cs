
#nullable enable

using System;

namespace FrameworkAbstractions.ContainerAbstractions;

/// <summary>
/// 容器身份解析规则唯一入口——禁止在各调用方硬编码 scope→hostKey 映射。
/// 本层（FrameworkAbstractions）不依赖前端 ContainerRegistry 具体类型，解析通过注入委托完成；
/// 前端以此将解析源绑定到 ContainerRegistry（SingleImplementation：唯一反查口径）。
/// </summary>
public static class ContainerIdentity
{
    /// <summary>
    /// 按作用域解析容器身份。
    /// </summary>
    /// <param name="scope">作用域标识，如 "Side"。</param>
    /// <param name="resolve">解析委托：scope → { hostKey, entityKey, controlName }（前端绑定到 ContainerRegistry）。</param>
    /// <returns>命中则返回强类型身份，未命中返回 null。</returns>
    public static ContainerId? Resolve(string scope, Func<string, ContainerResolution?, ContainerId?> resolve)
    {
        if (string.IsNullOrWhiteSpace(scope) || resolve == null) return null;
        return resolve(scope, null);
    }

    /// <summary>
    /// 显式宿主标识路径解析（scope + hostKey 精确限定）。
    /// </summary>
    public static ContainerId? Resolve(string scope, string hostKey, Func<string, string, ContainerId?> resolve)
    {
        if (string.IsNullOrWhiteSpace(scope)) return null;
        if (resolve == null) return null;
        return resolve(scope, hostKey);
    }

    /// <summary>
    /// 按宿主标识反查作用域（替代硬编码 "sideContainer"→"Side" 映射）。
    /// <paramref name="reverse"/> 绑定到 ContainerRegistry.ResolveScopeByHostKey。
    /// </summary>
    public static string? ResolveScopeByHostKey(string hostKey, Func<string, string?> reverse)
    {
        if (string.IsNullOrWhiteSpace(hostKey) || reverse == null) return null;
        return reverse(hostKey);
    }
}

/// <summary>
/// 容器解析的中间产物（供身份构造），前端 ContainerRegistry.ResolveScope 返回。
/// </summary>
public sealed record ContainerResolution
{
    public required string HostKey { get; init; }
    public string? EntityKey { get; init; }
    public string? ControlName { get; init; }
}
