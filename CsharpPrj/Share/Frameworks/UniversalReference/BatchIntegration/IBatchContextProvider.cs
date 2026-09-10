
using System;

namespace UniversalWorkflowFramework.BatchIntegration
{
    /// <summary>
    /// 批次上下文提供者。日志模块依赖此接口读取当前批次并订阅进入/退出事件，
    /// 与具体实现（BatchManager 单例）解耦。
    /// </summary>
    public interface IBatchContextProvider
    {
        string? CurrentBatchId { get; }

        event Action<string>? BatchEntered;

        event Action<string>? BatchExited;
    }
}
