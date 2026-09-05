using System.Text;
using Modules.StateMachine;

namespace Modules.Parsing.Parsing;

public class CommandTokenizer
{
    private const int NORMAL = 0;
    private const int IN_DOUBLE_QUOTE = 1;
    private const int IN_SINGLE_QUOTE = 2;
    private const int ESCAPED_DQ = 3;
    private const int ESCAPED_SQ = 4;

    private readonly string _input;
    private int _position;

    public CommandTokenizer(string input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _position = 0;
    }

    public List<string> Tokenize()
    {
        var tokens = new List<string>();
        var currentToken = new StringBuilder();

        var sm = new SequentialStateMachine<char, StringBuilder, List<string>>(NORMAL);


        sm.When(NORMAL, c => c == '"', IN_DOUBLE_QUOTE);

        sm.When(NORMAL, c => c == '\'', IN_SINGLE_QUOTE);

        sm.When(NORMAL, c => char.IsWhiteSpace(c), NORMAL, (c, ctx) =>
        {
            if (ctx.Length > 0)
            {
                tokens.Add(ctx.ToString());
                ctx.Clear();
            }
        });

        sm.When(NORMAL, c => true, NORMAL, (c, ctx) =>
        {
            ctx.Append(c);
        });


        sm.When(IN_DOUBLE_QUOTE, c => c == '"', NORMAL, (c, ctx) =>
        {
            tokens.Add(ctx.ToString());
            ctx.Clear();
        });

        sm.When(IN_DOUBLE_QUOTE, c => c == '\\', ESCAPED_DQ);

        sm.When(IN_DOUBLE_QUOTE, c => true, IN_DOUBLE_QUOTE, (c, ctx) =>
        {
            ctx.Append(c);
        });


        sm.When(IN_SINGLE_QUOTE, c => c == '\'', NORMAL, (c, ctx) =>
        {
            tokens.Add(ctx.ToString());
            ctx.Clear();
        });

        sm.When(IN_SINGLE_QUOTE, c => c == '\\', ESCAPED_SQ);

        sm.When(IN_SINGLE_QUOTE, c => true, IN_SINGLE_QUOTE, (c, ctx) =>
        {
            ctx.Append(c);
        });


        sm.When(ESCAPED_DQ, c => true, IN_DOUBLE_QUOTE, (c, ctx) =>
        {
            ctx.Append(c);
        });


        sm.When(ESCAPED_SQ, c => true, IN_SINGLE_QUOTE, (c, ctx) =>
        {
            ctx.Append(c);
        });


        while (_position < _input.Length)
        {
            var c = _input[_position];

            if (sm.CurrentState == NORMAL && char.IsWhiteSpace(c))
            {
                if (currentToken.Length > 0)
                {
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                }
                while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
                    _position++;
                continue;
            }

            sm.Feed(c);
            _position++;
        }

        if (currentToken.Length > 0)
        {
            if (sm.CurrentState != NORMAL)
            {
                currentToken.Insert(0, '"');
            }

            tokens.Add(currentToken.ToString());
        }

        return tokens;
    }
}
