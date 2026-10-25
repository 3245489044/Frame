namespace TConfig;

/// <summary>
/// 交互域前缀（声明层单源）：外层宿主物理控件 = <c>host:</c>，htmlPage 内元素 = <c>page:</c>。
/// 与 Plate.HtmlViewer 的域索引前缀同值；声明图/实体注册/事件声明统一引用本常量，禁止各层手写前缀。
/// </summary>
public static class InteractionDomains
{
    public const string Host = "host:";

    public const string Page = "page:";

    public const string HostName = "host";

    public const string PageName = "page";

    public static string Qualify(string domain, string name)
        => domain == PageName ? Page + name : Host + name;
}
