using System.Text.Json;
using System.Text.Json.Serialization;
using CallCenter.LlmGate.Rules;

namespace CallCenter.LlmGate.Hosting;

/// <summary>gate.json 声明（阈值单源；UI 只切 mode，其余改动落本文件）。</summary>
public sealed class GateConfigFile
{
    public string Mode { get; set; } = "enforce";
    public string Listen { get; set; } = "http://127.0.0.1:8790";
    public string Upstream { get; set; } = "https://api.deepseek.com";

    public int MaxHop { get; set; } = 8;
    public int LoopRepeat { get; set; } = 6;
    public int LoopWindowSec { get; set; } = 60;
    public int CallsPerMinute { get; set; } = 60;
    public decimal SessionCostUsd { get; set; } = 1.00m;
    public decimal TaskCostUsd { get; set; } = 0.50m;
    public decimal DailyCostUsd { get; set; } = 10.00m;
    public long DailyTokens { get; set; } = 5_000_000;
    public decimal HoldCostUsd { get; set; } = 0.02m;
    public bool HoldUnknownModel { get; set; } = true;

    public int HoldTimeoutMs { get; set; } = 300_000;
    public int MaxPending { get; set; } = 32;
    public int RingSize { get; set; } = 2000;
    public int UpstreamTimeoutSec { get; set; } = 120;
    public bool StoreBody { get; set; }
    public int PreviewChars { get; set; } = 200;

    public string? PricingPath { get; set; }
    public string? DataRoot { get; set; }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>读取配置；缺件返回默认值（不静默失败：调用方应把路径打到状态栏）。坏件抛异常。</summary>
    public static GateConfigFile Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GateConfigFile();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GateConfigFile>(json, ReadOptions) ?? new GateConfigFile();
    }

    public GateOptions ToOptions(string configPath, string? modeOverride = null, string? listenOverride = null,
        string? upstreamOverride = null, string? dataRootOverride = null)
        => new()
        {
            Mode = modeOverride ?? Mode,
            Listen = NormalizeListen(listenOverride ?? Listen),
            Upstream = upstreamOverride ?? Upstream,
            MaxHop = MaxHop,
            LoopRepeat = LoopRepeat,
            LoopWindowSec = LoopWindowSec,
            CallsPerMinute = CallsPerMinute,
            SessionCostUsd = SessionCostUsd,
            TaskCostUsd = TaskCostUsd,
            DailyCostUsd = DailyCostUsd,
            DailyTokens = DailyTokens,
            HoldCostUsd = HoldCostUsd,
            HoldUnknownModel = HoldUnknownModel,
            HoldTimeoutMs = HoldTimeoutMs,
            MaxPending = MaxPending,
            RingSize = RingSize,
            UpstreamTimeoutSec = UpstreamTimeoutSec,
            StoreBody = StoreBody,
            PreviewChars = PreviewChars,
            DataRoot = dataRootOverride ?? DataRoot,
            ConfigPath = configPath,
        };

    private static string NormalizeListen(string listen)
        => listen.EndsWith('/') ? listen.TrimEnd('/') : listen;
}
