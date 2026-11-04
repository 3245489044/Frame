#nullable enable

using System;
using System.Text;
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
    /// <summary>页面视图宿主（<c>data-name</c>）默认名：唯一挂载点，单写者写其子集。</summary>
    public const string DefaultRootName = "ui-root";

    /// <summary>视图根节点标记属性（DOM 断言口径：<c>[data-view-root=history]</c>）。</summary>
    public const string ViewRootAttribute = "data-view-root";

    /// <summary>视图根节点（<paramref name="inner"/> = 壳/容器内容）；带稳定键 ⇒ 根子集投影可按键增删。</summary>
    public static string Wrap(string viewKey, string inner)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) throw new ArgumentException("视图键不可为空。", nameof(viewKey));
        var key = viewKey.Trim();
        return "<section class=\"view-root\" data-view=\"" + key + "\" " + ViewRootAttribute + "=\"" + key
               + "\" data-key=\"view:" + key + "\">" + inner + "</section>";
    }

    /// <summary>按视图程序产出「根 + 空容器」骨架（无壳；用于无壳渲染器的兜底与断言）。</summary>
    public static string Build(UiViewBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var builder = new StringBuilder(256);
        foreach (var container in blueprint.Containers)
        {
            builder.Append("<div class=\"region");
            if (container.IsStream) builder.Append(" stream");
            builder.Append("\" data-name=\"").Append(container.Key).Append("\"></div>");
        }

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
