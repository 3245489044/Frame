using System;

namespace TestInfrastructure;

/// <summary>
/// 仓库根目录定位器：从测试程序集所在目录向上回溯，找到同时包含
/// <c>CsharpPrj</c> 与 <c>Projects\Agent</c> 的目录（即仓库根）。
/// 定位结果首次计算后静态缓存，避免每个测试重复回溯。
/// </summary>
public static class RepoRootLocator
{
    private static readonly Lazy<string> _root = new(FindRoot);

    /// <summary>仓库根目录的绝对路径（惰性计算并缓存）。</summary>
    public static string Root => _root.Value;

    /// <summary>执行一次仓库根目录查找（不依赖缓存）。</summary>
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "CsharpPrj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Projects", "Agent")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("无法定位仓库根目录（需同时含 CsharpPrj 与 Projects/Agent）。");
    }
}