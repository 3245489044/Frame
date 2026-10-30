namespace Plate.HttpMethod.OpenAI;

/// <summary>
/// OpenAI 兼容端点默认值（通用单源）—— 本地联调端点 / 默认真实端点 / 回环判据。
/// <para>零领域依赖：只描述"OpenAI 兼容 HTTP 端点"这一通用事实，不含任何 Agent 路由/提供商语义。
/// 领域侧（如 Agent.Contract.Llm.LlmEndpoints）保留同名常量作薄别名，避免调用点漂移。</para>
/// </summary>
public static class OpenAiEndpointDefaults
{
    /// <summary>本地联调端点 BaseUrl（OpenAI 兼容本地服务的监听前缀）。</summary>
    public const string LocalBaseUrl = "http://127.0.0.1:8787/";

    /// <summary>本地联调端口（与 <see cref="LocalBaseUrl"/> 一致；独立暴露便于日志/测试引用）。</summary>
    public const int LocalPort = 8787;

    /// <summary>默认真实端点 BaseUrl（<see cref="OpenAiCompatibleTransport.BuildEndpoint"/> 空参兜底）。</summary>
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    /// <summary>端点是否回环（结构判据：host ∈ {localhost, 127.0.0.0/8, ::1}）。</summary>
    public static bool IsLoopback(string? endpoint)
        => OpenAiCompatibleTransport.IsLoopbackEndpoint(endpoint);
}
