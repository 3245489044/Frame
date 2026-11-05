using System;
using System.Collections.Generic;

namespace Modules.Registry;

/// <summary>
/// 函数库清单条目（<c>function-libraries.json</c> / <c>function-libraries.domain.json</c>）。
/// <para>
/// 除传统 <c>type</c>/<c>method</c> 外，额外承载数据驱动注册所需的资源目录、配置清单与程序集定位声明。
/// 资源路径一律使用 token（<c>@config</c>/<c>@app</c>/<c>@shared</c>/<c>@manifest</c>/<c>@runtime</c>），
/// 由组合根注入的 <see cref="FunctionLibraryEnvironment"/> 解析，注册器不自行探测源码仓布局。
/// </para>
/// </summary>
public sealed class FunctionLibraryDescriptor
{
    /// <summary>清单显示名（同时用于诊断与去重提示）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>程序集限定类型名，如 <c>FunctionStorage.MathLibrary, FileDriven.Business</c>。</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>可选注册方法名；缺省按 <c>RegisterAll</c>/<c>Register</c> 约定解析。</summary>
    public string? MethodName { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>是否为必需库：必需库加载失败阻断启动；可选库失败仅告警。</summary>
    public bool Required { get; init; } = true;

    /// <summary>资源目录（token 或相对清单目录），数据驱动注册器据此解析 <see cref="ConfigPath"/>。</summary>
    public string? ResourceRoot { get; init; }

    /// <summary>注册配置相对路径（相对 <see cref="ResourceRoot"/>）。</summary>
    public string? ConfigPath { get; init; }

    /// <summary>期望函数清单相对路径（相对 <see cref="ResourceRoot"/>）。</summary>
    public string? ExpectedPath { get; init; }

    /// <summary>注册所需程序集名（相对名或 token 路径），供 <see cref="IFunctionLibraryContext.ResolveAssembly"/> 解析。</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = Array.Empty<string>();

    /// <summary>程序集搜索根（token 或相对清单目录），优先于环境默认搜索目录。</summary>
    public IReadOnlyList<string> AssemblySearchRoots { get; init; } = Array.Empty<string>();
}
