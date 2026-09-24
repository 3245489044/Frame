namespace Share.Conversation.Facts;

/// <summary>
/// 事实来源（事实文档内「谁写的」）——声明面词表 = <c>Config\session-facts.ushar</c> 的
/// <c>[Space] FactSource</c>（C# 侧写 <see cref="Csharp"/>/<see cref="Legacy"/>/<see cref="Synthetic"/>；
/// TS 侧写 <see cref="TsDaemon"/>/<see cref="TsSdk"/>）。
/// </summary>
public enum FactSource
{
    /// <summary>C# 框架侧产出（产事实点 / 摄取器归一后的框架事实）。</summary>
    Csharp,

    /// <summary>TS daemon 控制面旁路 append（阶段帧 / heartbeat）。</summary>
    TsDaemon,

    /// <summary>TS SDK 执行面（hooks.jsonl / 会话库）——经摄取器归一。</summary>
    TsSdk,

    /// <summary>存量数据（经 <c>FactIdMigration</c> 读侧归一；投影时标 <c>source=Legacy</c>）。</summary>
    Legacy,

    /// <summary>合成事实（测试 / 演示），不得作为生产真源。</summary>
    Synthetic,
}
