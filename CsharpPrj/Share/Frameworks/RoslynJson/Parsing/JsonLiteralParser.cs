using System;
using System.Globalization;
using System.Text;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.RoslynJson.Parsing;

/// <summary>
/// 最小严格 JSON 字面量解析器：把作者书写的 JSON 区块（对象/数组成员）解析为转换内核 IR。
/// <para>
/// 自带而非引包的理由：① 分析器为 netstandard2.0（不引入 System.Text.Json，避免与编译器宿主冲突）；
/// ② JSON 区块要求**逐字保真**（零语义变换），自解析可精确报位置而非重排语义。
/// </para>
/// <para>支持 RFC 8259 子集：object / array / string / number / true / false / null（无注释、无尾逗号）。</para>
/// </summary>
public static class JsonLiteralParser
{
    /// <summary>解析整段 JSON（允许前后空白）；末尾有残余内容即失败。</summary>
    public static ConversionNode Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var cursor = new Cursor(text);
        cursor.SkipWhitespace();
        var node = cursor.ReadValue();
        cursor.SkipWhitespace();

        if (!cursor.AtEnd)
            throw new JsonLiteralParseException("JSON 区块尾有多余内容", cursor.Position);

        return node;
    }

    private sealed class Cursor
    {
        private readonly string _text;

        public Cursor(string text)
        {
            _text = text;
        }

        public int Position { get; private set; }

        public bool AtEnd => Position >= _text.Length;

        private char Current => _text[Position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) Position++;
        }

        public ConversionNode ReadValue()
        {
            if (AtEnd) throw new JsonLiteralParseException("JSON 区块意外结束", Position);

            switch (Current)
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ConversionScalar.Of(ReadString());
                case 't': Expect("true"); return ConversionScalar.Of(true);
                case 'f': Expect("false"); return ConversionScalar.Of(false);
                case 'n': Expect("null"); return ConversionScalar.Null;
                default: return ReadNumber();
            }
        }

        private ConversionObject ReadObject()
        {
            var result = new ConversionObject();
            Position++;              // '{'
            SkipWhitespace();

            if (!AtEnd && Current == '}') { Position++; return result; }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd || Current != '"') throw new JsonLiteralParseException("JSON 对象键须为双引号字符串", Position);

                var key = ReadString();
                SkipWhitespace();
                if (AtEnd || Current != ':') throw new JsonLiteralParseException("JSON 对象键后须为 ':'", Position);

                Position++;          // ':'
                SkipWhitespace();
                result.Add(key, ReadValue());

                SkipWhitespace();
                if (AtEnd) throw new JsonLiteralParseException("JSON 对象未闭合", Position);

                if (Current == ',') { Position++; continue; }
                if (Current == '}') { Position++; return result; }

                throw new JsonLiteralParseException("JSON 对象成员间须为 ',' 或 '}'", Position);
            }
        }

        private ConversionArray ReadArray()
        {
            var result = new ConversionArray();
            Position++;              // '['
            SkipWhitespace();

            if (!AtEnd && Current == ']') { Position++; return result; }

            while (true)
            {
                SkipWhitespace();
                result.Add(ReadValue());
                SkipWhitespace();

                if (AtEnd) throw new JsonLiteralParseException("JSON 数组未闭合", Position);

                if (Current == ',') { Position++; continue; }
                if (Current == ']') { Position++; return result; }

                throw new JsonLiteralParseException("JSON 数组元素间须为 ',' 或 ']'", Position);
            }
        }

        private string ReadString()
        {
            var builder = new StringBuilder();
            Position++;              // 起始引号

            while (true)
            {
                if (AtEnd) throw new JsonLiteralParseException("JSON 字符串未闭合", Position);

                var ch = _text[Position++];

                if (ch == '"') return builder.ToString();

                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (AtEnd) throw new JsonLiteralParseException("JSON 字符串转义不完整", Position);

                var escape = _text[Position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        builder.Append((char)int.Parse(ReadHex4(), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    default:
                        throw new JsonLiteralParseException("JSON 字符串含未知转义 '\\" + escape + "'", Position - 2);
                }
            }
        }

        private string ReadHex4()
        {
            if (Position + 4 > _text.Length)
                throw new JsonLiteralParseException("JSON \\u 转义须为 4 位十六进制", Position);

            var hex = _text.Substring(Position, 4);
            Position += 4;
            return hex;
        }

        private ConversionNode ReadNumber()
        {
            var start = Position;

            while (!AtEnd)
            {
                var ch = Current;
                var isNumberChar = (ch >= '0' && ch <= '9')
                                   || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E';
                if (!isNumberChar) break;
                Position++;
            }

            if (Position == start)
                throw new JsonLiteralParseException("JSON 值无法识别：'" + Current + "'", Position);

            var token = _text.Substring(start, Position - start);
            var isFloating = token.IndexOf('.') >= 0 || token.IndexOf('e') >= 0 || token.IndexOf('E') >= 0;

            if (!isFloating
                && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            {
                return ConversionScalar.Of(integer);
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return ConversionScalar.Of(number);
            }

            throw new JsonLiteralParseException("JSON 数值非法：" + token, start);
        }

        private void Expect(string literal)
        {
            if (Position + literal.Length > _text.Length
                || string.CompareOrdinal(_text, Position, literal, 0, literal.Length) != 0)
            {
                throw new JsonLiteralParseException("JSON 字面量非法（期望 " + literal + "）", Position);
            }

            Position += literal.Length;
        }
    }
}
