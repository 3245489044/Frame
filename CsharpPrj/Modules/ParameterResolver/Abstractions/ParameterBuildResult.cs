
namespace ParameterResolver.Abstractions;

public class ParameterBuildResult
{
    public bool IsSuccess { get; }

    public object?[] Args { get; }

    public IReadOnlyList<ParameterMatchInfo> MatchInfos { get; }

    public string? ErrorMessage { get; }

    private ParameterBuildResult(
        bool isSuccess,
        object?[] args,
        IReadOnlyList<ParameterMatchInfo> matchInfos,
        string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        Args = args;
        MatchInfos = matchInfos;
        ErrorMessage = errorMessage;
    }

    public static ParameterBuildResult Create(object?[] args, IReadOnlyList<ParameterMatchInfo> matchInfos)
    {
        return new ParameterBuildResult(isSuccess: true, args: args, matchInfos: matchInfos);
    }

    public static ParameterBuildResult Failure(string error)
        => new ParameterBuildResult(
            isSuccess: false,
            args: Array.Empty<object?>(),
            matchInfos: Array.Empty<ParameterMatchInfo>(),
            errorMessage: error);
}
