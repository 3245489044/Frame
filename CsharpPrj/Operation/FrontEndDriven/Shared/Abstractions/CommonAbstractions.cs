namespace FrontEndDriven.Abstractions;

#region === 路径解析 ===

public interface IPathResolver
{
    string? ResolveConfigPath(string configPath);

    string ResolveConfigPathUnsafe(string configPath);
}

#endregion
