using System.IO;

namespace FrontEndDriven.ProductionConsole;

/// <summary>产物类型词表（结果区按类型选择渲染方式；插件不写渲染代码）。</summary>
public static class ArtifactKind
{
    public const string Image = "image";
    public const string Audio = "audio";
    public const string Text = "text";
    public const string Json = "json";
    public const string File = "file";
}

/// <summary>
/// 单个产物描述：绝对路径 + 类型 + 可选元数据。控制台据 Bytes/宿主能力决定内联还是 URL 引用。
/// </summary>
public sealed class ArtifactSpec
{
    public string Kind { get; set; } = ArtifactKind.File;

    /// <summary>绝对路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>MIME（缺省按扩展名推导）。</summary>
    public string? Mime { get; set; }

    public long Bytes { get; set; }

    public string? Sha256 { get; set; }

    /// <summary>可比较的小字段（duration/bpm/seed/model/format/attempts 等）。</summary>
    public Dictionary<string, string>? Meta { get; set; }
}

/// <summary>产物物化结果：Url 可直接进 HTML（data: 或 https://host/…）；Error 非空表示不可展示。</summary>
public sealed class ArtifactUrl
{
    public string? Url { get; set; }

    /// <summary>data / host / none。</summary>
    public string Mode { get; set; } = ArtifactUrlModes.None;

    public long Bytes { get; set; }

    public string? Error { get; set; }

    public bool IsUsable => !string.IsNullOrWhiteSpace(Url) && Error is null;

    public static ArtifactUrl Data(string url, long bytes) =>
        new() { Url = url, Mode = ArtifactUrlModes.Data, Bytes = bytes };

    public static ArtifactUrl Host(string url, long bytes) =>
        new() { Url = url, Mode = ArtifactUrlModes.Host, Bytes = bytes };

    public static ArtifactUrl Failed(string error, long bytes = 0) =>
        new() { Url = null, Mode = ArtifactUrlModes.None, Bytes = bytes, Error = error };
}

public static class ArtifactUrlModes
{
    public const string Data = "data";
    public const string Host = "host";
    public const string None = "none";
}

/// <summary>扩展名 → MIME（未知回退 application/octet-stream）。</summary>
public static class ArtifactMime
{
    public static string For(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".txt" => "text/plain; charset=utf-8",
            ".md" => "text/markdown; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
}
