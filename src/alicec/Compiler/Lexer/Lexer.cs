using System;
using System.Collections.Generic;

namespace Alice.Compiler;

internal sealed class Lexer
{
    private readonly string _text;
    private readonly string _path;
    private int _pos;
    private int _line;
    private int _col;

    internal Lexer(string text, string sourcePath)
    {
        _text = text ?? string.Empty;
        _path = string.IsNullOrWhiteSpace(sourcePath) ? "<source>" : sourcePath;
        _pos = 0;
        _line = 1;
        _col = 1;
    }

    internal List<Token> LexAll(out List<Diagnostic> diagnostics)
    {
        diagnostics = new List<Diagnostic>();
        var tokens = new List<Token>();

        while (true)
        {
            var t = NextToken(diagnostics);
            tokens.Add(t);
            if (t.Kind == TokenKind.Eof)
            {
                break;
            }
        }

        return tokens;
    }

    private Token NextToken(List<Diagnostic> diagnostics)
    {
        while (true)
        {
            if (IsAtEnd())
            {
                return MakeToken(TokenKind.Eof, string.Empty, 0);
            }

            var c = Peek();
            if (c == ' ' || c == '\t' || c == '\r')
            {
                Advance();
                continue;
            }

            if (c == '\n')
            {
                var span = CurrentSpan(0);
                AdvanceNewLine();
                return new Token(TokenKind.NewLine, "\n", span);
            }

            if (c == '/' && Peek(1) == '/')
            {
                while (!IsAtEnd() && Peek() != '\n')
                {
                    Advance();
                }
                continue;
            }

            if (c == '/' && Peek(1) == '*')
            {
                var blockStartPos = _pos;
                var blockStartLine = _line;
                var blockStartCol = _col;
                Advance();
                Advance();
                while (!IsAtEnd())
                {
                    if (Peek() == '\n')
                    {
                        AdvanceNewLine();
                        continue;
                    }
                    if (Peek() == '*' && Peek(1) == '/')
                    {
                        Advance();
                        Advance();
                        break;
                    }
                    Advance();
                }

                if (IsAtEnd())
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        MakeSpan(blockStartPos, _pos - blockStartPos, blockStartLine, blockStartCol),
                        "未终止的块注释"));
                }
                continue;
            }

            break;
        }

        var startPos = _pos;
        var startLine = _line;
        var startCol = _col;

        char ch = Peek();

        if (IsIdentStart(ch))
        {
            Advance();
            while (!IsAtEnd() && IsIdentPart(Peek()))
            {
                Advance();
            }

            var text = Slice(startPos, _pos - startPos);
            var kind = KeywordKind(text);
            return new Token(kind, text, MakeSpan(startPos, _pos - startPos, startLine, startCol));
        }

        if (char.IsDigit(ch))
        {
            return LexNumber(startPos, startLine, startCol, diagnostics);
        }

        if (ch == '"')
        {
            return LexString(startPos, startLine, startCol, diagnostics);
        }

        if (ch == '\'')
        {
            return LexChar(startPos, startLine, startCol, diagnostics);
        }

        switch (ch)
        {
            case '@':
                Advance();
                return new Token(TokenKind.At, "@", MakeSpan(startPos, 1, startLine, startCol));
            case ';':
                Advance();
                return new Token(TokenKind.Semicolon, ";", MakeSpan(startPos, 1, startLine, startCol));
            case ',':
                Advance();
                return new Token(TokenKind.Comma, ",", MakeSpan(startPos, 1, startLine, startCol));
            case ':':
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.ColonEquals, ":=", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Colon, ":", MakeSpan(startPos, 1, startLine, startCol));
            case '.':
                Advance();
                return new Token(TokenKind.Dot, ".", MakeSpan(startPos, 1, startLine, startCol));
            case '(':
                Advance();
                return new Token(TokenKind.LParen, "(", MakeSpan(startPos, 1, startLine, startCol));
            case ')':
                Advance();
                return new Token(TokenKind.RParen, ")", MakeSpan(startPos, 1, startLine, startCol));
            case '{':
                Advance();
                return new Token(TokenKind.LBrace, "{", MakeSpan(startPos, 1, startLine, startCol));
            case '}':
                Advance();
                return new Token(TokenKind.RBrace, "}", MakeSpan(startPos, 1, startLine, startCol));
            case '[':
                Advance();
                return new Token(TokenKind.LBracket, "[", MakeSpan(startPos, 1, startLine, startCol));
            case ']':
                Advance();
                return new Token(TokenKind.RBracket, "]", MakeSpan(startPos, 1, startLine, startCol));
            case '+':
                Advance();
                return new Token(TokenKind.Plus, "+", MakeSpan(startPos, 1, startLine, startCol));
            case '-':
                if (Peek(1) == '>')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.Arrow, "->", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Minus, "-", MakeSpan(startPos, 1, startLine, startCol));
            case '*':
                Advance();
                return new Token(TokenKind.Star, "*", MakeSpan(startPos, 1, startLine, startCol));
            case '%':
                Advance();
                return new Token(TokenKind.Percent, "%", MakeSpan(startPos, 1, startLine, startCol));
            case '!':
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.BangEquals, "!=", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Bang, "!", MakeSpan(startPos, 1, startLine, startCol));
            case '=':
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.EqualsEquals, "==", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Equals, "=", MakeSpan(startPos, 1, startLine, startCol));
            case '<':
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.LessEquals, "<=", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Less, "<", MakeSpan(startPos, 1, startLine, startCol));
            case '>':
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.GreaterEquals, ">=", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Greater, ">", MakeSpan(startPos, 1, startLine, startCol));
            case '&':
                if (Peek(1) == '&')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.AmpAmp, "&&", MakeSpan(startPos, 2, startLine, startCol));
                }
                Advance();
                return new Token(TokenKind.Amp, "&", MakeSpan(startPos, 1, startLine, startCol));
            case '|':
                if (Peek(1) == '|')
                {
                    Advance();
                    Advance();
                    return new Token(TokenKind.PipePipe, "||", MakeSpan(startPos, 2, startLine, startCol));
                }
                break;
            case '/':
                Advance();
                return new Token(TokenKind.Slash, "/", MakeSpan(startPos, 1, startLine, startCol));
        }

        diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            MakeSpan(startPos, 1, startLine, startCol),
            $"无法识别的字符: '{ch}'"));
        Advance();
        return new Token(TokenKind.Identifier, string.Empty, MakeSpan(startPos, 1, startLine, startCol));
    }

    private Token LexNumber(int startPos, int startLine, int startCol, List<Diagnostic> diagnostics)
    {
        var hasDot = false;
        var hasExp = false;

        while (!IsAtEnd() && char.IsDigit(Peek()))
        {
            Advance();
        }

        if (!IsAtEnd() && Peek() == '.' && char.IsDigit(Peek(1)))
        {
            hasDot = true;
            Advance();
            while (!IsAtEnd() && char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        if (!IsAtEnd() && (Peek() == 'e' || Peek() == 'E'))
        {
            hasExp = true;
            Advance();
            if (!IsAtEnd() && (Peek() == '+' || Peek() == '-'))
            {
                Advance();
            }
            if (IsAtEnd() || !char.IsDigit(Peek()))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, CurrentSpan(0), "指数部分缺少数字"));
            }
            while (!IsAtEnd() && char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        var numberText = Slice(startPos, _pos - startPos);

        var suffixStart = _pos;
        while (!IsAtEnd() && IsIdentPart(Peek()))
        {
            Advance();
        }

        var suffix = _pos > suffixStart ? Slice(suffixStart, _pos - suffixStart) : null;
        var span = MakeSpan(startPos, _pos - startPos, startLine, startCol);

        if (suffix is not null)
        {
            if (!IsAllowedNumberSuffix(suffix))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, span, $"不支持的数值后缀: {suffix}"));
            }
        }

        var isFloatBySuffix = suffix is "f32" or "f64";
        return new Token((hasDot || hasExp || isFloatBySuffix) ? TokenKind.FloatLiteral : TokenKind.IntLiteral, numberText, span, suffix);
    }

    private Token LexString(int startPos, int startLine, int startCol, List<Diagnostic> diagnostics)
    {
        Advance();
        while (!IsAtEnd() && Peek() != '"')
        {
            if (Peek() == '\n')
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, MakeSpan(startPos, _pos - startPos, startLine, startCol), "字符串字面量不能跨行"));
                break;
            }

            if (Peek() == '\\')
            {
                Advance();
                if (!IsAtEnd())
                {
                    Advance();
                }
                continue;
            }

            Advance();
        }

        if (IsAtEnd())
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, MakeSpan(startPos, _pos - startPos, startLine, startCol), "未终止的字符串字面量"));
        }
        else
        {
            Advance();
        }

        var text = Slice(startPos, _pos - startPos);
        return new Token(TokenKind.StringLiteral, text, MakeSpan(startPos, _pos - startPos, startLine, startCol));
    }

    private Token LexChar(int startPos, int startLine, int startCol, List<Diagnostic> diagnostics)
    {
        Advance();
        if (IsAtEnd() || Peek() == '\n')
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, MakeSpan(startPos, _pos - startPos, startLine, startCol), "未终止的字符字面量"));
        }
        else
        {
            if (Peek() == '\\')
            {
                Advance();
                if (!IsAtEnd())
                {
                    Advance();
                }
            }
            else
            {
                Advance();
            }

            if (IsAtEnd() || Peek() != '\'')
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, MakeSpan(startPos, _pos - startPos, startLine, startCol), "未终止的字符字面量"));
            }
            else
            {
                Advance();
            }
        }

        var text = Slice(startPos, _pos - startPos);
        return new Token(TokenKind.CharLiteral, text, MakeSpan(startPos, _pos - startPos, startLine, startCol));
    }

    private static bool IsAllowedNumberSuffix(string suffix)
    {
        return suffix is "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64" or "f32" or "f64";
    }

    private static TokenKind KeywordKind(string text)
    {
        return text switch
        {
            "fun" => TokenKind.KwFun,
            "namespace" => TokenKind.KwNamespace,
            "import" => TokenKind.KwImport,
            "as" => TokenKind.KwAs,
            "const" => TokenKind.KwConst,
            "var" => TokenKind.KwVar,
            "class" => TokenKind.KwClass,
            "interface" => TokenKind.KwInterface,
            "new" => TokenKind.KwNew,
            "this" => TokenKind.KwThis,
            "public" => TokenKind.KwPublic,
            "protected" => TokenKind.KwProtected,
            "private" => TokenKind.KwPrivate,
            "static" => TokenKind.KwStatic,
            "try" => TokenKind.KwTry,
            "except" => TokenKind.KwExcept,
            "finally" => TokenKind.KwFinally,
            "raise" => TokenKind.KwRaise,
            "defer" => TokenKind.KwDefer,
            "extern" => TokenKind.KwExtern,
            "type" => TokenKind.KwType,
            "enum" => TokenKind.KwEnum,
            "struct" => TokenKind.KwStruct,
            "async" => TokenKind.KwAsync,
            "await" => TokenKind.KwAwait,
            "go" => TokenKind.KwGo,
            "unsafe" => TokenKind.Identifier,
            "if" => TokenKind.KwIf,
            "else" => TokenKind.KwElse,
            "while" => TokenKind.KwWhile,
            "return" => TokenKind.KwReturn,
            "break" => TokenKind.KwBreak,
            "continue" => TokenKind.KwContinue,
            "true" => TokenKind.KwTrue,
            "false" => TokenKind.KwFalse,
            "null" => TokenKind.KwNull,
            _ => TokenKind.Identifier,
        };
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private bool IsAtEnd() => _pos >= _text.Length;
    private char Peek(int offset = 0) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private void Advance()
    {
        _pos++;
        _col++;
    }

    private void AdvanceNewLine()
    {
        _pos++;
        _line++;
        _col = 1;
    }

    private string Slice(int start, int length) => _text.Substring(start, length);

    private SourceSpan MakeSpan(int start, int length, int line, int col) => new(_path, start, length, line, col);
    private SourceSpan CurrentSpan(int length) => new(_path, _pos, length, _line, _col);
    private Token MakeToken(TokenKind kind, string text, int length) => new(kind, text, CurrentSpan(length));
}
