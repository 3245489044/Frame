#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 视图/区域名字契约（常量单源）：声明面与编排面共用，禁各处手写字面量。
/// <para>本类位于声明层，不依赖任何壳/骨架类型；壳与骨架反向转发本类常量。</para>
/// </summary>
public static class UiViewNames
{
    /// <summary>区域容器属性前缀（服务端注入；与 DomOps.Patch name 对齐，供区域化写回命中）。</summary>
    public const string RegionNamePrefix = "region-";

    /// <summary>页面视图宿主（<c>data-name</c>）默认名：唯一挂载点，单写者写其子集。</summary>
    public const string RootName = "ui-root";

    /// <summary>区域写回目标前缀（<c>region:&lt;key&gt;</c>）：写回目标合法域。</summary>
    public const string RegionTargetPrefix = "region:";

    /// <summary>流段键前缀（气泡段：<c>bubbles:r{round}</c>）：差分键，不是 DOM 名。</summary>
    public const string StreamSectionPrefix = "bubbles:";
}
