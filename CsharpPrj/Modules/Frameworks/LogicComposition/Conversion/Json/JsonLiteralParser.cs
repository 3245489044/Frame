using System;
using System.Globalization;
using System.Text;
using Share.LogicComposition.Conversion.Lexing;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Json;

/// <summary>
/// 最小严格 JSON 字面量解析器：把作者书写的 JSON 区块（对象/数组成员）解析为转换内核 IR。
/// <para>
/// 自带而非引包的理由：① 本内核为 netstandard2.0（分析器侧加载；不引入 System.Text.Json，避免与编译器宿主冲突）；
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
            var start = Position;                 // 起始引号
            var end = TextScan.FindStringEnd(_text, start);
            if (end == TextScan.Unterminated)
                throw new JsonLiteralParseException("JSON 字符串未闭合", _text.Length);

            Position = end;                       // 停在闭合引号之后
            return DecodeString(start + 1, end - 1);
        }

        /// <summary>解码字符串内容（不含首尾引号）；转义序列按 RFC 8259 表逐项还原。</summary>
        private string DecodeString(int contentStart, int contentEnd)
        {
            var builder = new StringBuilder(contentEnd - contentStart);
            var i = contentStart;

            while (i < contentEnd)
            {
                var ch = _text[i++];
                if (ch != '\\') { builder.Append(ch); continue; }

                if (i >= contentEnd) throw new JsonLiteralParseException("JSON 字符串转义不完整", i);

                var escape = _text[i++];
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
                        if (i + 4 > contentEnd)
                            throw new JsonLiteralParseException("JSON \\u 转义须为 4 位十六进制", i);

                        var hex = _text.Substring(i, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw new JsonLiteralParseException("JSON \\u 转义须为 4 位十六进制", i);

                        builder.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new JsonLiteralParseException("JSON 字符串含未知转义 '\\" + escape + "'", i - 2);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 读取数值（RFC 8259 文法：<c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>）。
        /// 前导 <c>+</c>、前导零、<c>.5</c>、<c>1.</c> 等非法规格均拒绝并给出精确位置。
        /// </summary>
        private ConversionNode ReadNumber()
        {
            var start = Position;
            var index = Position;

            if (index < _text.Length && _text[index] == '-') index++;

            if (index >= _text.Length || !IsDigit(_text[index]))
                throw new JsonLiteralParseException(
                    "JSON 值无法识别：'" + (index < _text.Length ? _text[index] : '?') + "'", index);

            if (_text[index] == '0') index++;
            else while (index < _text.Length && IsDigit(_text[index])) index++;

            var isFloating = ReadFraction(ref index);
            if (ReadExponent(ref index)) isFloating = true;

            var token = _text.Substring(start, index - start);
            Position = index;

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

        private bool ReadFraction(ref int index)
        {
            if (index >= _text.Length || _text[index] != '.') return false;

            index++;
            if (index >= _text.Length || !IsDigit(_text[index]))
                throw new JsonLiteralParseException("JSON 数值小数部分须为数字", index);

            while (index < _text.Length && IsDigit(_text[index])) index++;
            return true;
        }

        private bool ReadExponent(ref int index)
        {
            if (index >= _text.Length || (_text[index] != 'e' && _text[index] != 'E')) return false;

            index++;
            if (index < _text.Length && (_text[index] == '+' || _text[index] == '-')) index++;

            if (index >= _text.Length || !IsDigit(_text[index]))
                throw new JsonLiteralParseException("JSON 数值指数部分须为数字", index);

            while (index < _text.Length && IsDigit(_text[index])) index++;
            return true;
        }

        private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

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
