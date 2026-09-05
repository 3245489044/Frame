using CallCenter.Core.Tools;

namespace CallCenter.Core.Discovery;

/// <summary>
/// C# 函数库发现的产物：<see cref="ToolDescriptor"/>（进 CsgToolCatalog/数据面）+ 可选 <see cref="Delegate"/>
///（供 CSharpTrack Runtime 轨真实执行）。两者同源、一次性反射收集，避免运行时重复发现。
/// </summary>
public sealed record CSharpToolDefinition(
    ToolDescriptor Descriptor,
    Delegate? Delegate);
