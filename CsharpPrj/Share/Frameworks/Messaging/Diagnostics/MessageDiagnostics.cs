namespace Share.Messaging;

/// <summary>
/// 诊断码（结构事实，禁静默）：所有失败路径必须携带其一。
/// </summary>
public static class MessageDiagnostics
{
    /// <summary>成功。</summary>
    public const string Ok = "ok";

    /// <summary>未知（兜底，仍可见）。</summary>
    public const string Unknown = "MSG_UNKNOWN";

    /// <summary>地址无法解析（无对应消费者/角色/会话）。</summary>
    public const string AddressUnresolved = "MSG_ADDRESS_UNRESOLVED";

    /// <summary>委托深度超限。</summary>
    public const string HopExceeded = "MSG_HOP_EXCEEDED";

    /// <summary>扇出超限。</summary>
    public const string FanoutExceeded = "MSG_FANOUT_EXCEEDED";

    /// <summary>已过期（TTL 到）。</summary>
    public const string Expired = "MSG_EXPIRED";

    /// <summary>作用域被拒（空或越权）。</summary>
    public const string ScopeDenied = "MSG_SCOPE_DENIED";

    /// <summary>队列深度超限。</summary>
    public const string QueueOverflow = "MSG_QUEUE_OVERFLOW";

    /// <summary>未知消息种类。</summary>
    public const string KindUnknown = "MSG_KIND_UNKNOWN";

    /// <summary>投递画像未装配（禁静默回退到别的传输）。</summary>
    public const string TransportUnwired = "MSG_TRANSPORT_UNWIRED";

    /// <summary>状态迁移非法。</summary>
    public const string StateInvalid = "MSG_STATE_INVALID";

    /// <summary>命令缺少闭包凭证（ReplyTo / CorrelationId）。</summary>
    public const string CommandInvalid = "MSG_COMMAND_INVALID";

    /// <summary>查无此消息。</summary>
    public const string NotFound = "MSG_NOT_FOUND";

    /// <summary>传输不支持该操作（如只读流画像）。</summary>
    public const string Unsupported = "MSG_UNSUPPORTED";

    /// <summary>声明面缺件/编译失败（不得静默取默认）。</summary>
    public const string DeclarationMissing = "MSG_DECLARATION_MISSING";
}
