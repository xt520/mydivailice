namespace Alice.Compiler;

internal sealed record Token(TokenKind Kind, string Text, SourceSpan Span, string? Suffix = null);
