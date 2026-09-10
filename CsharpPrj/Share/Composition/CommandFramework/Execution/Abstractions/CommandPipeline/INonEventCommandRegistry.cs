
namespace CommandFramework.Abstractions;

public interface INonEventCommandRegistry : Container.Abstractions.INonEventCommandLookup

{
    void Register(string commandName, Action<TConfig.IExecutionContext> handler,
        CommandCriticality criticality = CommandCriticality.Important,
        string? description = null);

    void RegisterRange(params (string Name, Action<TConfig.IExecutionContext> Handler,
        CommandCriticality Criticality, string? Description)[] commands);

    void Declare(string commandName, CommandCriticality criticality = CommandCriticality.Important);

    bool IsRegistered(string commandName);

    NonEventCommandValidationResult ValidateAll();

    string[] GetUnhandledCommands();

    int Count { get; }

    void Clear();
}

public class NonEventCommandValidationResult
{
    public int TotalCommands { get; set; }

    public int RegisteredCount { get; set; }

    public string[] MissingHandlers { get; set; } = Array.Empty<string>();

    public string[] CriticalMissing { get; set; } = Array.Empty<string>();

    public bool IsValid { get; set; }

    public DateTime ValidationTime { get; set; }

    public string GetSummary()
    {
        if (IsValid)
            return $"所有 {TotalCommands} 个非事件命令均已注册处理函数。";

        return $"发现 {MissingHandlers.Length} 个非事件命令缺少处理函数：" +
               $"{string.Join(", ", MissingHandlers)}" +
               (CriticalMissing.Length > 0
                   ? $"（其中 {CriticalMissing.Length} 个为关键命令：{string.Join(", ", CriticalMissing)}）"
                   : "");
    }
}
