using Plate.DslMethod;

namespace Usharp.Semantics;

/// <summary>
/// 统一发射出口（消除 H7）：<c>kind → 产物</c>（契约 JSON / 提示词文本 / 工具面 / 端口 spec）。
/// <para>
/// 既有 <c>Usharp.Emit</c> 各发射器**不改语义**，改为实现本接口（<see cref="Emit"/> = 原方法体）；
/// 由「被手写调用」变为「被注册、被编排」——消费方按 <see cref="Id"/> 取产物，不感知实现。
/// </para>
/// </summary>
public interface IUsharpFacetProjector
{
    /// <summary>投影器 id（如 <c>flow.roundspec</c> / <c>obs.manifest</c>）。</summary>
    string Id { get; }

    /// <summary>消费的声明类。</summary>
    IReadOnlyList<DslSymbolKind> Kinds { get; }

    /// <summary>由一次编译产物发射领域产物（纯函数，无副作用）。</summary>
    object Emit(UsharpCompilation compilation);
}
