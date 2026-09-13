using System.Text;
using Modules.StateMachine;
using Share.LogicComposition.Conversion.Lexing;

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

    public List<string> Tokenize() => new(TokenizeWithDiagnostics().Tokens);

    /// <summary>
    /// 切词并**显式回报**未闭合引号（不做静默修补/静默丢弃）：未闭合时
    /// <see cref="CommandTokenizeResult.UnterminatedQuoteIndex"/> 为引号起始偏移（0 基），否则 -1。
    /// 是否把该事实升级为失败由调用方裁决（对齐 <c>ErrorCode.UnclosedQuote</c>）。
    /// </summary>
    public CommandTokenizeResult TokenizeWithDiagnostics()
    {
        var tokens = new List<string>();
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
            // 空白须交给状态机（NORMAL 态的空白转移负责刷写当前 token）：旁路会导致 token 静默丢失
            sm.Feed(_input[_position]);
            _position++;
        }

        // 未闭合引号的判定与偏移取值统一由共享内核原语给出（引号态 + 末尾转义态），不再自持状态跟踪
        var unterminatedQuoteIndex = TextScan.FindUnterminatedQuote(_input, '"', '\'');
        var unterminated = unterminatedQuoteIndex != TextScan.Unterminated;

        // EOF 仍在普通态：刷写最后一个 token（旧实现漏刷导致末 token 静默丢失）
        if (!unterminated && sm.Context.Length > 0) tokens.Add(sm.Context.ToString());

        return new CommandTokenizeResult(tokens, unterminatedQuoteIndex);
    }
}
