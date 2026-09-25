namespace Usharp.Emit;

/// <summary>失败事件载荷形状（<c>{path,errors}</c> / <c>{file,errors}</c> / <c>{file,flowId,errors}</c>）。</summary>
public enum DeclarationFailedShape
{
    /// <summary><c>{ path, errors }</c>（已解析绝对路径为键）。</summary>
    PathErrors,

    /// <summary><c>{ file, errors }</c>（Basename 为键）。</summary>
    FileErrors,

    /// <summary><c>{ file, flowId, errors }</c>。</summary>
    FileFlowErrors,
}

/// <summary>诊断事件载荷形状（三种既有键形）。</summary>
public enum DeclarationDiagnosticShape
{
    /// <summary><c>{ path, code, message }</c>。</summary>
    PathCodeMessage,

    /// <summary><c>{ file, code, message }</c>。</summary>
    FileCodeMessage,

    /// <summary><c>{ file, flowId, code, message }</c>。</summary>
    FileFlowCodeMessage,
}

/// <summary>异常事件载荷形状（三种既有键形）。</summary>
public enum DeclarationExceptionShape
{
    /// <summary><c>{ file, code = "(exception)", message }</c>。</summary>
    FileTaggedCodeMessage,

    /// <summary><c>{ file, error }</c>。</summary>
    FileError,

    /// <summary><c>{ file, flowId, error }</c>。</summary>
    FileFlowError,
}
