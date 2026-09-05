namespace ExecutionFramework.Reference;

public static class ReferencePathParser
{
    public const string EnvPrefix = "$env:";

    public const string ConstPrefix = "$const:";

    public const string ParentPrefix = "../";

    public const string CurrentPrefix = "./";

    public readonly record struct ParsedPath(
        ReferencePathKind Kind,
        string[] Segments,
        string Original);

    public enum ReferencePathKind
    {
        RootInput,

        EntityOutput,

        Parent,

        Environment,

        Constant,

        Invalid
    }

    public static ParsedPath Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ParsedPath(ReferencePathKind.Invalid, Array.Empty<string>(), path);

        if (path.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
            return new ParsedPath(ReferencePathKind.Environment, new[] { path[EnvPrefix.Length..] }, path);
        if (path.StartsWith(ConstPrefix, StringComparison.OrdinalIgnoreCase))
            return new ParsedPath(ReferencePathKind.Constant, new[] { path[ConstPrefix.Length..] }, path);

        if (path.StartsWith(ParentPrefix, StringComparison.OrdinalIgnoreCase))
            return new ParsedPath(ReferencePathKind.Parent, SplitSegments(path[ParentPrefix.Length..]), path);

        var scopePath = path.StartsWith(CurrentPrefix, StringComparison.OrdinalIgnoreCase) ? path[2..] : path;

        if (scopePath.StartsWith("input", StringComparison.OrdinalIgnoreCase))
        {
            var afterInput = scopePath["input".Length..];
            if (afterInput.Length == 0)
                return new ParsedPath(ReferencePathKind.RootInput, Array.Empty<string>(), path);
            if (afterInput[0] == '.' || afterInput[0] == '/')
                return new ParsedPath(ReferencePathKind.RootInput, SplitSegments(afterInput[1..]), path);
        }

        var segments = SplitSegments(scopePath);
        return segments.Length > 0
            ? new ParsedPath(ReferencePathKind.EntityOutput, segments, path)
            : new ParsedPath(ReferencePathKind.Invalid, Array.Empty<string>(), path);
    }

    private static string[] SplitSegments(string path)
    {
        return path.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
    }
}