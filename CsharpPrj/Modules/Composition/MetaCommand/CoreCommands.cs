
using System.Collections.Generic;

namespace MetaCommand;

public static class CoreCommands
{

    public const string Echo = "echo";
    public const string Print = "print";


    public const string Sleep = "sleep";
    public const string Wait = "wait";


    public const string Close = "close";
    public const string Exit = "exit";
    public const string Quit = "quit";


    public const string Exec = "exec";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Echo, Print,
        Sleep, Wait,
        Close,
        Exit, Quit,
        Exec,
    };
}