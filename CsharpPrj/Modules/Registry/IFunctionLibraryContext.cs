using System.Collections.Generic;
using System.Reflection;

namespace Modules.Registry;

/// <summary>
/// 单个函数库的装配上下文：资源定位、程序集定位与诊断出口。
/// <para>
/// 由 <see cref="FunctionLibraryEnvironment"/> 按清单条目构造，传给
/// <c>RegisterAll(ITrConfigRegistry, IFunctionLibraryContext)</c>。
/// 注册器只依赖本接口，不依赖任何产品/源码仓路径。
/// </para>
/// </summary>
public interface IFunctionLibraryContext
{
    /// <summary>当前清单条目。</summary>
    FunctionLibraryDescriptor Descriptor { get; }

    /// <summary>当前清单文件的绝对路径（用于 <c>@manifest</c> 相对解析与诊断）。</summary>
    string ManifestPath { get; }

    /// <summary>当前库的资源目录绝对路径（由 <c>resourceRoot</c> 或清单目录派生）。</summary>
    string ResourceDirectory { get; }

    /// <summary>是否运行于 Runtime 发布包。</summary>
    bool IsPublished { get; }

    /// <summary>解析必需资源；不存在时抛出带候选清单的 <see cref="System.IO.FileNotFoundException"/>。</summary>
    string ResolveRequiredResource(string relativeOrTokenPath);

    /// <summary>解析可选资源；不存在返回 <c>null</c>。</summary>
    string? ResolveOptionalResource(string? relativeOrTokenPath);

    /// <summary>解析程序集；失败时抛出带候选清单的异常。</summary>
    Assembly ResolveAssembly(string nameOrPath);

    /// <summary>环境提供的默认程序集搜索目录（App/Shared/扩展目录）。</summary>
    IReadOnlyList<string> AssemblySearchDirectories { get; }

    /// <summary>信息诊断（正常路径）。</summary>
    void Info(string message);

    /// <summary>告警诊断（非阻断路径）。</summary>
    void Warn(string message);
}
