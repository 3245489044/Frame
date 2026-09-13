using System;

namespace Share.RoslynJson.Parsing;

/// <summary>
/// JSON 字面量解析失败（携带区块内 0 基字符偏移，供上层还原 <c>文件:行:列</c>）。
/// </summary>
public sealed class JsonLiteralParseException : Exception
{
    /// <summary>区块内 0 基字符偏移。</summary>
    public int Offset { get; }

    /// <summary>构造异常。</summary>
    /// <param name="message">人类可读消息。</param>
    /// <param name="offset">区块内 0 基字符偏移。</param>
    public JsonLiteralParseException(string message, int offset) : base(message)
    {
        Offset = offset;
    }
}
