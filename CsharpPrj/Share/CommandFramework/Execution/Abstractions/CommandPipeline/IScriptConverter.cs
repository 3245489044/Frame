
namespace CommandFramework.Abstractions;

public interface IScriptConverter
{
    string? ConvertJsonScriptToCommandFile(string scriptPath);
}
