using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 声明面装载描述符（L 层数据形态）—— 把「从哪个文件、用哪个发射器、按哪套日志键、回退什么」外化为**纯数据**，
/// 与装载机制（<see cref="DeclarationSpecLoader"/>）分离；每个声明面只需一条描述符，不再各自复制装载骨架。
/// </summary>
/// <typeparam name="T">声明面产物类型（如 RoundSpec / GovernanceSpec）。</typeparam>
/// <param name="ConfigRelativePath">Config/ 下相对路径（交调用方注入的路径解析委托解析）。</param>
/// <param name="BaseName">DeclarationSource.BaseName（日志 file 字段语义）。</param>
/// <param name="FlowId">[Flow] 流号（无流语义 ⇒ null）。</param>
/// <param name="Emit">领域发射回调：一次编译 → 产物 + 诊断（唯一领域知识注入点）。</param>
/// <param name="Log">日志画像（键位 + 载荷形状；未给键位的事件不落日志）。</param>
public sealed record DeclarationSpec<T>(
    string ConfigRelativePath,
    string BaseName,
    string? FlowId,
    Func<UsharpCompilation, string?, DeclarationEmitOutput<T>> Emit,
    DeclarationLogProfile Log)
    where T : class;
