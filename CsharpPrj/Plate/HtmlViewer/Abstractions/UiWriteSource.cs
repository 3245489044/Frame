#nullable enable

namespace UiBackend.Abstractions;

/// <summary>
/// 写回来源（单源枚举，刷新分层与交互快路径共用）：决定收敛窗口、合并优先级与稳态判据。
/// <para>归属规则：入口定意图（<see cref="Mount"/> / <see cref="Refresh"/> / <see cref="Interactive"/>）；
/// <see cref="Tick"/> 仅由「只产易变叶子」的分支降标（判据载体 = 计划动词，非执行者叙述）。</para>
/// </summary>
public enum UiWriteSource
{
    /// <summary>首挂载 / 整页落地（唯一整页出口）。</summary>
    Mount = 0,

    /// <summary>刷新驱动（事件 / 定时 / 外部变更）。</summary>
    Refresh,

    /// <summary>心跳（只允许易变叶子目标；出现区域/树目标即判违规）。</summary>
    Tick,

    /// <summary>显式交互（点击 / 发送）：不得被在飞刷新合并掉。</summary>
    Interactive,

    /// <summary>流（日志 / 过程帧 / 气泡段）。</summary>
    Stream,
}
