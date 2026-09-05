using CallCenter.Core.Tools;

namespace CallCenter.Core.Discovery;

/// <summary>
/// 工具发现源（终端面 Source 轨的抽象入口）。每种后端轨道（C# 函数库 / TS / PS / 外部 MCP）都以一个
/// <see cref="IToolSource"/> 实现作为「发现源」，产出语言无关的 <see cref="ToolDescriptor"/>，
/// 交由数据面 <see cref="DataPlane.CsgToolCatalog"/> 投影为 CSG CanonicalForm。
/// 硬编码种子只允许出现在临时 <see cref="SeedToolSource"/>，真实工具一律来自发现源。
/// </summary>
public interface IToolSource
{
    /// <summary>源名称（用于审计/观测，如 "csharp.function-libraries" / "seed.read_nodes"）。</summary>
    string Name { get; }

    /// <summary>执行一次发现，返回本源的当前工具集。同名冲突交由目录重名检测裁决。</summary>
    IReadOnlyList<ToolDescriptor> Discover();
}
