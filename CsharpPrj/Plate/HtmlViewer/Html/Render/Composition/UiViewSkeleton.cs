#nullable enable

using System;
using System.Text;
using Modules.Parsing.Markup;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 视图根骨架（框架件）：把视图程序声明的容器投影为一段 DOM 骨架。
/// <para>骨架只描述结构（根 + 空容器），内容由同拍区域写填充；「容器不存在」由「不产该容器」表达，
/// 因此不需要任何清空动词（权威:「axioms:AX.NoSilentOutlet」的对称面：缺即结构性缺失，非静默省略）。</para>
/// <para>本类零领域类型依赖：容器键/角色全部来自编译产物。</para>
/// </summary>
public static class UiViewSkeleton
{
    /// <summary>页面视图宿主（<c>data-name</c>）默认名（转发 <see cref="UiViewNames.RootName"/>）。</summary>
    public const string DefaultRootName = UiViewNames.RootName;

    /// <summary>视图根节点标记属性（DOM 断言口径：<c>[data-view-root=history]</c>）。</summary>
    public const string ViewRootAttribute = "data-view-root";

    /// <summary>视图根节点（<paramref name="inner"/> = 壳/容器内容）；带稳定键 ⇒ 根子集投影可按键增删。</summary>
    public static string Wrap(string viewKey, string inner)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) throw new ArgumentException("视图键不可为空。", nameof(viewKey));
        var key = viewKey.Trim();
        var attrs = new MarkupAttrBatch()
            .Class("view-root", "view-root")
            .Add("data-view", key)
            .Add(ViewRootAttribute, key)
            .Key("view:" + key);
        return MarkupTag.Element("section", attrs, UiMarkup.Trusted(inner));
    }

    /// <summary>按视图程序产出「根 + 空容器」骨架（无壳；用于无壳渲染器的兜底与断言）。</summary>
    public static string Build(UiViewBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var builder = new StringBuilder(256);
        foreach (var container in blueprint.Containers)
            builder.Append(MarkupBlock.Render(
                "div",
                container.IsStream ? "region stream" : "region",
                "region",
                container.Key,
                null));

        return Wrap(blueprint.ViewKey, builder.ToString());
    }

    /// <summary>容器存在性断言（结构事实）：骨架中每个声明容器出现恰一次。</summary>
    public static bool ContainsAllContainers(string html, UiViewBlueprint blueprint)
    {
        if (string.IsNullOrEmpty(html)) return false;
        foreach (var key in blueprint.ContainerKeys)
        {
            if (html.IndexOf("data-name=\"" + key + "\"", StringComparison.Ordinal) < 0) return false;
        }

        return true;
    }
}
