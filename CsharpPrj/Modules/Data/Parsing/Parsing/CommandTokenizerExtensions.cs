namespace Modules.Parsing.Parsing;

public static class CommandTokenizerExtensions
{
    public static (string commandName, string[] args) Parse(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
            return (string.Empty, Array.Empty<string>());
        return (tokens[0], tokens.Skip(1).ToArray());
    }

    public static string GetFirstWord(string command)
    {
        var tokens = Tokenize(command);
        return tokens.Count > 0 ? tokens[0] : string.Empty;
    }

    public static string GetRemaining(string command)
    {
        var (_, args) = Parse(command);
        return args.Length > 0 ? string.Join(" ", args) : string.Empty;
    }

    private static List<string> Tokenize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new List<string>();
        return new CommandTokenizer(input).Tokenize();
    }
}
