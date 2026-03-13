using System;

namespace Alice.Compiler;

internal enum DiagnosticSeverity
{
    Error,
    Warning,
}

internal sealed record Diagnostic(DiagnosticSeverity Severity, SourceSpan Span, string Message)
{
    public override string ToString()
    {
        var sev = Severity == DiagnosticSeverity.Error ? "错误" : "警告";
        return $"{Span.SourcePath}({Span.Line},{Span.Column}): {sev}: {Message}";
    }
}
