using System;
using System.Collections.Generic;
using System.IO;

namespace Alice.Compiler;

internal static class AliceCompilationPipeline
{
    internal static string? CompileToCSharp(string sourceText, string sourcePath, TextWriter diagnostics)
    {
        var lexer = new Lexer(sourceText, sourcePath);
        var tokens = lexer.LexAll(out var lexDiagnostics);
        if (lexDiagnostics.Count > 0)
        {
            foreach (var d in lexDiagnostics)
            {
                diagnostics.WriteLine(d.ToString());
            }

            return null;
        }

        var parser = new Parser(tokens, sourcePath);
        var unit = parser.ParseCompilationUnit(out var parseDiagnostics);
        if (parseDiagnostics.Count > 0)
        {
            foreach (var d in parseDiagnostics)
            {
                diagnostics.WriteLine(d.ToString());
            }

            return null;
        }

        var emitter = new CSharpEmitter(sourcePath);
        return emitter.EmitProgram(new ProgramNode(new[] { unit }));
    }
}
