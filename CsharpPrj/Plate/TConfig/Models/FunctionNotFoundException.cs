
namespace TConfig;

public class FunctionNotFoundException : Exception
{
    public string FunctionName { get; }

    public FunctionNotFoundException(string functionName)
        : base($"函数未找到: {functionName}")
    {
        FunctionName = functionName;
    }

    public FunctionNotFoundException(string functionName, string message)
        : base(message)
    {
        FunctionName = functionName;
    }

    public FunctionNotFoundException(string functionName, Exception innerException)
        : base($"函数未找到: {functionName}", innerException)
    {
        FunctionName = functionName;
    }
}
