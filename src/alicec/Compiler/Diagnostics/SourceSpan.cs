namespace Alice.Compiler;

internal readonly record struct SourceSpan(
    string SourcePath,
    int Start,
    int Length,
    int Line,
    int Column);
