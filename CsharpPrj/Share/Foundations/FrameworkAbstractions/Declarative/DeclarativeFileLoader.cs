using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FrameworkAbstractions.Declarative;

/// <summary>声明源来源事实（路径 + 内容指纹 + 新鲜度 + 诊断；单源，禁调用点各算一套）。</summary>
public sealed record SourceProvenance(
    string Path,
    string ContentHash,
    string Fingerprint,
    DateTimeOffset LoadedAt,
    bool IsStale,
    string? Diagnostic)
{
    /// <summary>降级/陈旧可见化文案。</summary>
    public string Describe()
        => Diagnostic is null ? $"{Path} ({ContentHash[..Math.Min(12, ContentHash.Length)]})" : $"{Path}: {Diagnostic}";
}

/// <summary>声明装载结果（值 + 来源事实）。</summary>
public sealed record DeclarationLoad<T>(T Value, SourceProvenance Provenance) where T : class;

/// <summary>声明装载诊断码（受控词；异常消息前缀即码）。</summary>
public static class DeclarationCodes
{
    /// <summary>文件缺失/路径未解析。</summary>
    public const string FileMissing = "decl.file-missing";

    /// <summary>反序列化失败。</summary>
    public const string ParseFailed = "decl.parse-failed";

    /// <summary>结构校验失败。</summary>
    public const string Invalid = "decl.invalid";

    /// <summary>来源陈旧（内容指纹与期望不一致）。</summary>
    public const string Stale = "decl.stale";
}

/// <summary>
/// 声明文件装载模板（通用件，单路语义）：读取 → 反序列化 → 校验 → 来源事实 → 陈旧断言。
/// <para>**无内置兜底**：缺失/解析失败/校验失败/陈旧一律抛；回退必须由调用方显式声明并可见化。</para>
/// </summary>
public static class DeclarativeFileLoader
{
    /// <summary>装载必需声明（失败即抛；成功返回值 + 来源事实）。</summary>
    /// <param name="path">绝对路径（调用方负责解析，不在模板内做候选链）。</param>
    /// <param name="deserialize">文本 → 强类型（失败返回 null）。</param>
    /// <param name="validate">附加结构校验（缺省仅判非 null）。</param>
    /// <param name="expectedFingerprint">期望内容指纹（非空且不一致 ⇒ <see cref="DeclarationCodes.Stale"/>）。</param>
    /// <param name="fingerprint">指纹算法（缺省 = SHA256 全量内容）。</param>
    public static DeclarationLoad<T> LoadRequired<T>(
        string path,
        Func<string, T?> deserialize,
        Func<T, bool>? validate = null,
        string? expectedFingerprint = null,
        Func<string, string>? fingerprint = null)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{DeclarationCodes.FileMissing}: 声明路径为空。");
        ArgumentNullException.ThrowIfNull(deserialize);

        if (!File.Exists(path))
            throw new InvalidOperationException($"{DeclarationCodes.FileMissing}: 声明文件不存在 {path}");

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{DeclarationCodes.ParseFailed}: 读取失败 {path}（{ex.Message}）", ex);
        }

        T? parsed;
        try
        {
            parsed = deserialize(text);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{DeclarationCodes.ParseFailed}: 解析失败 {path}（{ex.Message}）", ex);
        }

        if (parsed is null || (validate is not null && !validate(parsed)))
            throw new InvalidOperationException($"{DeclarationCodes.Invalid}: 结构无效 {path}");

        var hash = Sha256(text);
        var fp = fingerprint?.Invoke(text) ?? hash;
        var stale = !string.IsNullOrWhiteSpace(expectedFingerprint)
                    && !string.Equals(expectedFingerprint!.Trim(), fp, StringComparison.OrdinalIgnoreCase);
        if (stale)
            throw new InvalidOperationException(
                $"{DeclarationCodes.Stale}: {path} 内容指纹 {fp[..Math.Min(12, fp.Length)]} ≠ 期望 {expectedFingerprint}");

        var provenance = new SourceProvenance(path, hash, fp, DateTimeOffset.UtcNow, IsStale: false, Diagnostic: null);
        return new DeclarationLoad<T>(parsed, provenance);
    }

    /// <summary>标记陈旧（长期未刷新场景：显式 stale + 诊断，禁静默当作新鲜）。</summary>
    public static SourceProvenance MarkStale(SourceProvenance provenance, string reason)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return provenance with
        {
            IsStale = true,
            Diagnostic = string.IsNullOrWhiteSpace(reason) ? DeclarationCodes.Stale : reason.Trim(),
        };
    }

    /// <summary>内容 SHA256（十六进制小写）。</summary>
    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();
}
