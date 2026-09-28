using System;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Emit
{
    /// <summary>
    /// 声明面装载**日志画像**（键位 + 载荷形状）——把原本散在 5 个装载器里的
    /// <c>DelegateDeclarationLogSink</c> + switch 样板收敛为一份纯数据；未给键位的事件不落日志（等价旧「未处理」分支）。
    /// <para>已装载事件（<c>Loaded</c>）载荷随声明而异，经 <see cref="LoadedPayload"/> 传入；需要编译快照等富信息的装载器
    /// （如治理规格）不给 <see cref="LoadedKey"/>，改在装载后自行记账。</para>
    /// </summary>
    /// <param name="BaseName">Basename（日志 <c>file</c> 字段语义；<c>NotDeclared</c> 载荷取值）。</param>
    /// <param name="NotDeclaredKey">未声明日志键（null = 不落）。</param>
    /// <param name="CompileFailedKey">编译失败日志键（null = 不落）。</param>
    /// <param name="CompileDiagnosticKey">编译诊断日志键（null = 不落）。</param>
    /// <param name="EmitFailedKey">发射失败日志键（null = 不落）。</param>
    /// <param name="EmitDiagnosticKey">发射诊断日志键（null = 不落）。</param>
    /// <param name="LoadedKey">装载成功日志键（null = 不落，交装载器自行记账）。</param>
    /// <param name="ExceptionKey">异常日志键（null = 不落）。</param>
    /// <param name="LoadedPayload">装载成功载荷（<c>(payload, file, path, flowId)</c> ⇒ 载荷对象）。</param>
    /// <param name="CompileFailedShape">编译失败载荷形状。</param>
    /// <param name="EmitFailedShape">发射失败载荷形状。</param>
    /// <param name="CompileDiagnosticShape">编译诊断载荷形状。</param>
    /// <param name="EmitDiagnosticShape">发射诊断载荷形状。</param>
    /// <param name="ExceptionShape">异常载荷形状。</param>
    public sealed record DeclarationLogProfile(
        string BaseName,
        string? NotDeclaredKey = null,
        string? CompileFailedKey = null,
        string? CompileDiagnosticKey = null,
        string? EmitFailedKey = null,
        string? EmitDiagnosticKey = null,
        string? LoadedKey = null,
        string? ExceptionKey = null,
        Func<object?, string?, string?, string?, object?>? LoadedPayload = null,
        DeclarationFailedShape CompileFailedShape = DeclarationFailedShape.PathErrors,
        DeclarationFailedShape EmitFailedShape = DeclarationFailedShape.PathErrors,
        DeclarationDiagnosticShape CompileDiagnosticShape = DeclarationDiagnosticShape.PathCodeMessage,
        DeclarationDiagnosticShape EmitDiagnosticShape = DeclarationDiagnosticShape.PathCodeMessage,
        DeclarationExceptionShape ExceptionShape = DeclarationExceptionShape.FileTaggedCodeMessage)
    {
        /// <summary>按画像构造告警端口（键名/载荷逐字保持各装载器既有键形，不改 <c>agent_log_messages.json</c>）。</summary>
        public IDeclarationLogSink CreateSink(Action<string, object?> log)
            => new DelegateDeclarationLogSink((@event, file, flowId, path, payload) =>
            {
                switch (@event)
                {
                    case DeclarationLogEvent.NotDeclared when NotDeclaredKey is not null:
                        log(NotDeclaredKey, new { path = BaseName });
                        break;
                    case DeclarationLogEvent.CompileFailed when CompileFailedKey is not null:
                        log(CompileFailedKey, Failed(CompileFailedShape, file, path, flowId, payload));
                        break;
                    case DeclarationLogEvent.CompileDiagnostic when CompileDiagnosticKey is not null:
                        log(CompileDiagnosticKey, Diagnostic(CompileDiagnosticShape, file, path, flowId, (UsharpDiagnostic)payload!));
                        break;
                    case DeclarationLogEvent.EmitFailed when EmitFailedKey is not null:
                        log(EmitFailedKey, Failed(EmitFailedShape, file, path, flowId, payload));
                        break;
                    case DeclarationLogEvent.EmitDiagnostic when EmitDiagnosticKey is not null:
                        log(EmitDiagnosticKey, Diagnostic(EmitDiagnosticShape, file, path, flowId, (UsharpDiagnostic)payload!));
                        break;
                    case DeclarationLogEvent.Loaded when LoadedKey is not null:
                        log(LoadedKey, LoadedPayload?.Invoke(payload, file, path, flowId) ?? new { file, flowId, path });
                        break;
                    case DeclarationLogEvent.Exception when ExceptionKey is not null:
                        log(ExceptionKey, Exception(ExceptionShape, file, flowId, (Exception)payload!));
                        break;
                }
            });

        private static object Failed(DeclarationFailedShape shape, string? file, string? path, string? flowId, object? payload)
        {
            var errors = (int)payload!;
            return shape switch
            {
                DeclarationFailedShape.FileErrors => new { file, errors },
                DeclarationFailedShape.FileFlowErrors => new { file, flowId, errors },
                _ => new { path, errors },
            };
        }

        private static object Diagnostic(DeclarationDiagnosticShape shape, string? file, string? path, string? flowId, UsharpDiagnostic diagnostic)
            => shape switch
            {
                DeclarationDiagnosticShape.FileCodeMessage => new { file, code = diagnostic.Code, message = diagnostic.ToString() },
                DeclarationDiagnosticShape.FileFlowCodeMessage => new { file, flowId, code = diagnostic.Code, message = diagnostic.ToString() },
                _ => new { path, code = diagnostic.Code, message = diagnostic.ToString() },
            };

        private static object Exception(DeclarationExceptionShape shape, string? file, string? flowId, Exception error)
            => shape switch
            {
                DeclarationExceptionShape.FileError => new { file, error = error.Message },
                DeclarationExceptionShape.FileFlowError => new { file, flowId, error = error.Message },
                _ => new { file, code = "(exception)", message = error.Message },
            };
    }
}
