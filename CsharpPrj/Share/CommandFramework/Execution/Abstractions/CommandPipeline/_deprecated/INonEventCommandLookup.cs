// ============================================================
// INonEventCommandLookup.cs — 非事件命令查找接口（轻量级）
//
// 提供 ExecuteCommandStage 所需的只读查找能力，
// 避免 CommandFramework 直接引用 FunctionStorage 导致的循环依赖。
// ============================================================

namespace CommandFramework.Abstractions;

/// <summary>
/// 非事件命令查找接口（轻量级只读接口）
/// <para>
/// 提供 ExecuteCommandStage 所需的只读查找能力。
/// 实现方（FunctionStorage.NonEventCommandRegistry）同时实现此接口。
/// </para>
/// </summary>
public interface INonEventCommandLookup
{
    /// <summary>
    /// 尝试获取命令的处理函数
    /// </summary>
    /// <param name="commandName">命令名称</param>
    /// <param name="handler">输出参数：处理函数</param>
    /// <returns>是否找到处理函数</returns>
    bool TryGetHandler(string commandName, out Action<TConfig.IExecutionContext>? handler);

    /// <summary>
    /// 所有已注册的命令名称列表
    /// </summary>
    IReadOnlyCollection<string> RegisteredCommandNames { get; }

    /// <summary>
    /// 判断命令是否已注册
    /// </summary>
    /// <param name="commandName">命令名称</param>
    /// <returns>是否已注册</returns>
    bool Contains(string commandName);
}
