namespace Share.Messaging;

/// <summary>发送/迁移回执（失败必须带诊断码，禁静默）。</summary>
public sealed record MessageReceipt(bool Ok, string MessageId, MessageState State, string Code, string Message = "")
{
    /// <summary>成功回执。</summary>
    public static MessageReceipt Success(string messageId, MessageState state, string code = MessageDiagnostics.Ok)
        => new(true, messageId, state, code, string.Empty);

    /// <summary>失败回执（<paramref name="code"/> 不得为空，缺省给显式未知码）。</summary>
    public static MessageReceipt Failure(string messageId, MessageState state, string code, string message)
        => new(false, messageId, state, string.IsNullOrWhiteSpace(code) ? MessageDiagnostics.Unknown : code, message ?? string.Empty);
}
