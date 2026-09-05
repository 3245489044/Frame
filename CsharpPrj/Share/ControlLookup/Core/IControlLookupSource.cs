#nullable enable

using System.Windows.Forms;

namespace Share.ControlLookup;

/// <summary>
/// 控件查找源策略（IControlLookupSource）——「按名找控件」的一种可用能力抽象。
/// 一个源代表一类查找口径（如 ControlPool O(1) 索引、EntityTree 实体树、IControlRegistry 普通索引、
/// WinForms 控件树递归深搜），由 <see cref="ControlLookupRegistry"/> 按 <see cref="Priority"/>
/// 顺序尝试，首个命中即返回（有序权威链）。
///
/// 设计意图：将既有 finder 族群（ControlRegistryAwareFinder / 原 ControlFinderAdapter / WinFormsControlFinder 等）的
/// 多层 fallback 收敛为「多源 + 单一聚合器」，各源可独立插拔、单独测试，
/// 语义口径由 Priority 显式声明，消除「适配器→适配器→适配器」的隐式转发。
/// </summary>
public interface IControlLookupSource
{
    /// <summary>源标识（诊断/日志用，应全局唯一）。</summary>
    string Name { get; }

    /// <summary>优先级：数值越小越先尝试。语义 = 可信度/首选度排序。</summary>
    int Priority { get; }

    /// <summary>当前源是否就绪可用（如依赖未注入/尚未初始化时返回 false，聚合器跳过）。</summary>
    bool IsReady { get; }

    /// <summary>按名称查找控件（全局口径：源内部决定从哪个根开始）。未命中返回 null。</summary>
    Control? FindByName(string name);

    /// <summary>在指定根控件下按名称查找（对不关心 root 的源可忽略 root）。未命中返回 null。</summary>
    Control? FindByName(Control root, string name);
}
