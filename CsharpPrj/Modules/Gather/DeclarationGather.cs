#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Modules.Gather;

/// <summary>声明装载失败姿态（W-4 单点）：无降级 / 可见化后回退。</summary>
public enum DeclarationLoadPolicy
{
    /// <summary>缺失或不合格 ⇒ 抛（禁零行为）。</summary>
    FailClosed = 0,

    /// <summary>缺失或不合格 ⇒ 记可见化事件并返回 <c>UsedFallback</c>（调用方须显式回退）。</summary>
    VisibleFallback = 1,
}

/// <summary>声明来源：相对配置根的路径 + 事件前缀 + 可选的诊断覆盖环境变量。</summary>
/// <param name="RelativePath">声明面相对路径（日志/异常文案的稳定标识）。</param>
/// <param name="EventPrefix">可见化事件前缀（如 <c>ConversationGather</c> ⇒ <c>ConversationGather_decl_loaded</c>）。</param>
/// <param name="EnvOverride">路径覆盖环境变量（诊断/测试用；空 ⇒ 声明面单源）。</param>
public sealed record DeclarationSource(string RelativePath, string EventPrefix, string? EnvOverride = null);

/// <summary>声明装载的结构化事件（调用方只做日志映射，不重复判定）。</summary>
/// <param name="Event">事件名（<c>{Prefix}_decl_loaded|missing|invalid</c>）。</param>
/// <param name="SourcePath">实际解析路径（可空）。</param>
/// <param name="Fingerprint">内容指纹（SHA256 十六进制小写；未读到内容 ⇒ null）。</param>
/// <param name="UsedFallback">是否走了回退（<see cref="DeclarationLoadPolicy.VisibleFallback"/> 且失败）。</param>
/// <param name="Diagnostic">诊断文本（成功 ⇒ null）。</param>
public sealed record DeclarationLoadEvent(
    string Event,
    string? SourcePath,
    string? Fingerprint,
    bool UsedFallback,
    string? Diagnostic);

/// <summary>装载结果：值 + 来源指纹 + 回退事实 + 诊断（三态显式，禁静默 null）。</summary>
public sealed class DeclarationLoadResult<T>
    where T : class
{
    internal DeclarationLoadResult(T? value, string? sourcePath, string? fingerprint, bool usedFallback, string? diagnostic)
    {
        Value = value;
        SourcePath = sourcePath;
        Fingerprint = fingerprint;
        UsedFallback = usedFallback;
        Diagnostic = diagnostic;
    }

    /// <summary>装载值（回退 ⇒ null，调用方须显式处理）。</summary>
    public T? Value { get; }

    /// <summary>实际解析路径（可空）。</summary>
    public string? SourcePath { get; }

    /// <summary>声明内容指纹（SHA256 小写 hex；未读到内容 ⇒ null）。</summary>
    public string? Fingerprint { get; }

    /// <summary>是否回退（true ⇒ <see cref="Value"/> 为 null 且 <see cref="Diagnostic"/> 非空）。</summary>
    public bool UsedFallback { get; }

    /// <summary>诊断（成功 ⇒ null）。</summary>
    public string? Diagnostic { get; }

    /// <summary>取声明；不可用 ⇒ 抛（禁静默零行为）。</summary>
    public T Require()
    {
        if (Value is not null)
        {
            return Value;
        }

        throw new InvalidOperationException(Diagnostic ?? "声明不可用（无诊断）");
    }
}

/// <summary>
/// 通用声明装载框架（唯一实现）：路径解析 → 读取 → 内容指纹 → 解析 → 校验 → 失败姿态 → 可见化。
/// <para>纪律：本类型只做机械动作，不承载任何领域语义；事件由调用方按前缀映射为日志。</para>
/// <para>纪律：指纹取**内容** SHA256（非 mtime / 非执行者叙述），供漂移与新鲜度对账。</para>
/// </summary>
public static class DeclarationGather<T>
    where T : class
{
    /// <summary>装载一份声明。</summary>
    /// <param name="source">声明来源。</param>
    /// <param name="resolveDefaultPath">默认路径解析（宿主注入；空 ⇒ 视为缺失）。</param>
    /// <param name="loader">文本 → 强类型声明（禁止在 loader 内吞异常）。</param>
    /// <param name="validate">结构校验（非空 ⇒ 违规即不合格；任何姿态都抛）。</param>
    /// <param name="policy">失败姿态。</param>
    /// <param name="onEvent">可见化回调（可空）。</param>
    public static DeclarationLoadResult<T> Load(
        DeclarationSource source,
        Func<string?> resolveDefaultPath,
        Func<string, T> loader,
        Func<T, System.Collections.Generic.IReadOnlyList<string>>? validate,
        DeclarationLoadPolicy policy,
        Action<DeclarationLoadEvent>? onEvent = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolveDefaultPath);
        ArgumentNullException.ThrowIfNull(loader);

        var prefix = source.EventPrefix;
        string? path;
        try
        {
            path = ResolvePath(source, resolveDefaultPath);
        }
        catch (Exception ex)
        {
            return Fail($"{prefix}_decl_invalid", null, null, ex.Message, ex, policy, onEvent);
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var diagnostic = $"声明缺失: {source.RelativePath}（解析路径: {path ?? "(未解析)"}）";
            return Fail($"{prefix}_decl_missing", path, null, diagnostic, null, policy, onEvent);
        }

        string text;
        try
        {
            text = File.ReadAllText(path!);
        }
        catch (Exception ex)
        {
            return Fail($"{prefix}_decl_invalid", path, null, $"声明读取失败: {path}（{ex.Message}）", ex, policy, onEvent);
        }

        var fingerprint = Fingerprint(text);

        T value;
        try
        {
            value = loader(text);
        }
        catch (Exception ex)
        {
            return Fail($"{prefix}_decl_invalid", path, fingerprint, $"声明解析失败: {path}（{ex.Message}）", ex, policy, onEvent);
        }

        if (validate is not null)
        {
            var issues = validate(value);
            if (issues is { Count: > 0 })
            {
                var diagnostic = $"声明不合格（{path}）：{string.Join("；", issues)}";
                onEvent?.Invoke(new DeclarationLoadEvent($"{prefix}_decl_invalid", path, fingerprint, false, diagnostic));
                throw new InvalidOperationException(diagnostic);
            }
        }

        onEvent?.Invoke(new DeclarationLoadEvent($"{prefix}_decl_loaded", path, fingerprint, false, null));
        return new DeclarationLoadResult<T>(value, path, fingerprint, usedFallback: false, diagnostic: null);
    }

    /// <summary>内容指纹（SHA256 十六进制小写；单一实现，调用方禁自算）。</summary>
    public static string Fingerprint(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ResolvePath(DeclarationSource source, Func<string?> resolveDefaultPath)
    {
        if (!string.IsNullOrWhiteSpace(source.EnvOverride))
        {
            var overridePath = Environment.GetEnvironmentVariable(source.EnvOverride!);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }
        }

        return resolveDefaultPath();
    }

    private static DeclarationLoadResult<T> Fail(
        string eventName,
        string? path,
        string? fingerprint,
        string diagnostic,
        Exception? error,
        DeclarationLoadPolicy policy,
        Action<DeclarationLoadEvent>? onEvent)
    {
        onEvent?.Invoke(new DeclarationLoadEvent(
            eventName, path, fingerprint, policy == DeclarationLoadPolicy.VisibleFallback, diagnostic));
        if (policy == DeclarationLoadPolicy.FailClosed)
        {
            throw error is null ? new InvalidOperationException(diagnostic) : new InvalidOperationException(diagnostic, error);
        }

        return new DeclarationLoadResult<T>(null, path, fingerprint, usedFallback: true, diagnostic);
    }
}
