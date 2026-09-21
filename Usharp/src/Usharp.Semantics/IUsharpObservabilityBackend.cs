using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 观测后端端口：Usharp 核心只认端口 —— 契约登记 / 步骤发射绑定 / 编译期自观测 / manifest 产出由装配方提供；
/// 未装配时为 <see cref="NullObservabilityBackend"/>（纯声明校验模式：<c>[Obs]</c> 闸A、引用即边与结构化诊断
/// 100% 在核心内，不依赖任何观测实现）。
/// <para>签名只用 Usharp 自有类型 + BCL：观测内核类型（<c>BoundaryContract</c>/<c>EventContract</c>/<c>Obs</c>…）
/// 不得进入核心公共签名（H1 核心零观测重依赖）。</para>
/// </summary>
public interface IUsharpObservabilityBackend
{
    /// <summary>是否已装配真实观测能力（false = 纯声明校验模式，契约产出显式可见化为 <see cref="UsharpCodes.ObsBackendUnavailable"/>）。</summary>
    bool IsAvailable { get; }

    /// <summary>登记事件声明（适配层内部映射为 <c>BoundaryContract.Event</c> / <c>EventContract</c>）。</summary>
    void RegisterEvent(UsharpObsDecl decl);

    /// <summary>登记作用域声明（映射为关联键作用域词汇表）。</summary>
    void RegisterScope(UsharpObsDecl decl);

    /// <summary>登记实体声明（映射为 <c>EntityId.EntityType</c> 归属）。</summary>
    void RegisterEntity(UsharpObsDecl decl);

    /// <summary>登记 <c>[Flow]</c> 步骤的发射绑定（映射为端口契约的 <c>Emits</c>）。</summary>
    void RegisterFlowEmits(string flowId, string stepKey, IReadOnlyList<string> eventKinds);

    /// <summary>进入一次编译的观测作用域（返回句柄；Dispose 退出）。</summary>
    IDisposable BeginCompilationScope(string sourcePath);

    /// <summary>编译期自观测事件（kind 属应用侧语义实例，由宿主注册；内核零领域字符串）。</summary>
    void EmitCompilationEvent(string eventKind, IReadOnlyDictionary<string, double>? metrics = null);

    /// <summary>确定性 manifest 产出（<c>[Obs]</c> → JSON；未装配 → 返回可见化提示，不产假 manifest）。</summary>
    string RenderManifest(UsharpCompilation compilation);
}
