#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 页面区域布局协议（仅保留流段前缀；固定键表/分区产物映射已下线）：
/// 容器集与区域键一律由视图程序声明供给（<see cref="UiViewBlueprintCatalog"/>），
/// 结构槽判定单源 = <see cref="UiViewContainerSlots"/>。
/// </summary>
public static class HtmlPageRegionLayout
{
    /// <summary>流段键前缀（转发 <see cref="UiViewNames.StreamSectionPrefix"/>：气泡段差分键，非 DOM 名）。</summary>
    public const string StreamSectionPrefix = UiViewNames.StreamSectionPrefix;
}
