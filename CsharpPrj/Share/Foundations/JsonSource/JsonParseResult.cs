#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Share.JsonSource;

/// <summary>一次解析的结局：成功值或错误列表（失败不抛，交由上层决定如何上报）。</summary>
public readonly record struct JsonParseResult<T>
{
    private JsonParseResult(bool success, T? value, IReadOnlyList<string> errors)
    {
        Success = success;
        Value = value;
        Errors = errors;
    }

    /// <summary>是否解析成功。</summary>
    public bool Success { get; }

    /// <summary>解析结果（失败时为 default）。</summary>
    public T? Value { get; }

    /// <summary>失败原因（成功时为空数组）。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>构造成功结果。</summary>
    public static JsonParseResult<T> Ok(T value) => new(true, value, Array.Empty<string>());

    /// <summary>构造失败结果。</summary>
    public static JsonParseResult<T> Fail(params string[] errors)
        => new(false, default, errors.Length == 0 ? new[] { "解析失败。" } : errors);

    /// <summary>构造失败结果。</summary>
    public static JsonParseResult<T> Fail(IReadOnlyList<string> errors)
        => new(false, default, errors.Count == 0 ? new[] { "解析失败。" } : errors);
}

/// <summary>热重载失败事件（读取失败或解析失败）；框架从不抛出，交给回调上报。</summary>
public sealed record JsonSourceFailure(string Path, string? Key, IReadOnlyList<string> Errors);

/// <summary>BCL 解析器工厂：把 <see cref="JsonSerializer"/> 适配成解析委托。</summary>
public static class JsonSourceParsers
{
    /// <summary>反序列化为 <typeparamref name="T"/>；错误以文本列表返回，不抛。</summary>
    public static Func<string, JsonParseResult<T>> Deserialize<T>(JsonSerializerOptions? options = null)
    {
        return text =>
        {
            try
            {
                var value = JsonSerializer.Deserialize<T>(text, options);
                return value is null
                    ? JsonParseResult<T>.Fail("JSON 内容为 null。")
                    : JsonParseResult<T>.Ok(value);
            }
            catch (JsonException ex)
            {
                return JsonParseResult<T>.Fail(ex.Message);
            }
            catch (NotSupportedException ex)
            {
                return JsonParseResult<T>.Fail(ex.Message);
            }
        };
    }
}
