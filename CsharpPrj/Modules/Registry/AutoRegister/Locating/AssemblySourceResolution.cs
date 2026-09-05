using System.Reflection;

namespace Modules.Registry.AutoRegister;

/// <summary>程序集源装载的一次解析结果 —— 把"装到没 / 装的是哪个文件 / 试过哪些候选"一起带回，消灭反射黑盒（Target of an invocation）。</summary>
public sealed class AssemblySourceResolution
{
    /// <summary>解析到的程序集；<c>null</c> 表示未命中（见 <see cref="Error"/>）。</summary>
    public Assembly? Assembly { get; init; }

    /// <summary>实际装载的物理文件路径（默认 ALC 按名命中时可为程序集 Location；未命中为空串）。</summary>
    public string LoadedPath { get; init; } = string.Empty;

    /// <summary>本次实际探测过的候选文件路径（绝对），供诊断"试过哪些目录仍找不到"。</summary>
    public IReadOnlyList<string> SearchedCandidates { get; init; } = Array.Empty<string>();

    /// <summary>未命中时的人类可读原因（含 <see cref="SearchedCandidates"/> 明细）。</summary>
    public string Error { get; init; } = string.Empty;

    public bool Succeeded => Assembly != null;
}
