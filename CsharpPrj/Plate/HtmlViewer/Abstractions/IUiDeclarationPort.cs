#nullable enable

using UiBackend.Html;

namespace UiBackend.Abstractions;

/// <summary>
/// UI 声明端口（控制反转边界的唯一读取契约）：视图程序目录 + 位置呈现属性 + 声明指纹。
/// <para>
/// 方向：Plate（框架）只持有本契约与不可变模型；实现由组合根注入（配置归属方）。
/// 禁在框架层读取产品配置文件 / 反序列化声明产物（声明解析唯一实现 = 端口实现方）。
/// </para>
/// <para>
/// 失败可见化：<see cref="EnsureReady"/> 装配期 fail-fast；未声明项由实现方产显式可见诊断
/// （对齐 «axioms:AX.NoSilentOutlet» 的 U 出口），禁静默兜底第二份视图表。
/// </para>
/// </summary>
public interface IUiDeclarationPort
{
    /// <summary>视图程序目录（不可变快照；容器所有权/生命周期/状态迁移的唯一来源）。</summary>
    UiViewBlueprintCatalog Blueprints { get; }

    /// <summary>声明指纹（并入页面稳定指纹：改声明即刷新）。</summary>
    string Fingerprint { get; }

    /// <summary>装配期自检（组合根调用一次）：声明缺失/非法 ⇒ 抛（禁运行期静默降级）。</summary>
    void EnsureReady();

    /// <summary>loc → 文案（未声明 ⇒ 显式占位 + 诊断；支持页面文案覆盖）。</summary>
    string Text(string loc, params object?[] args);

    /// <summary>loc → 呈现属性（未声明/种类不匹配 ⇒ false，调用方显式可见化）。</summary>
    bool TryResolveSpec(string loc, string kind, out UiLocSpec? spec);
}
