using System;
using System.IO;
using Microsoft.CodeAnalysis;

namespace Alice.Compiler;

internal sealed record CompiledAssembly(byte[] AssemblyBytes, string GeneratedCSharp);

internal static class AliceCompiler
{
    internal static CompiledAssembly? CompileToAssembly(
        string sourceText,
        string sourcePath,
        string? emitGeneratedCSharpPath,
        TextWriter diagnostics)
    {
        var cs = AliceCompilationPipeline.CompileToCSharp(sourceText, sourcePath, diagnostics);
        if (cs is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(emitGeneratedCSharpPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitGeneratedCSharpPath))!);
            File.WriteAllText(emitGeneratedCSharpPath, cs);
        }

        var bytes = RoslynCompiler.CompileToAssemblyBytes(
            cs,
            assemblyName: Path.GetFileNameWithoutExtension(sourcePath),
            diagnostics);

        if (bytes is null)
        {
            return null;
        }

        return new CompiledAssembly(bytes, cs);
    }

    internal static CompiledAssembly? CompileEntryToAssembly(
        string entryPath,
        IReadOnlyList<string> modulePaths,
        string? emitGeneratedCSharpPath,
        TextWriter diagnostics)
    {
        return CompileEntryToAssembly(entryPath, modulePaths, emitGeneratedCSharpPath, OutputKind.ConsoleApplication, diagnostics, allowUnsafe: false);
    }

    internal static CompiledAssembly? CompileEntryToAssembly(
        string entryPath,
        IReadOnlyList<string> modulePaths,
        string? emitGeneratedCSharpPath,
        OutputKind outputKind,
        TextWriter diagnostics,
        bool allowUnsafe = false)
    {
        var cs = AliceCompilationPipelineV2.CompileToCSharpFromEntry(
            entryPath,
            new AliceCompilationPipelineV2.ModuleOptions(modulePaths),
            diagnostics,
            allowUnsafe);
        if (cs is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(emitGeneratedCSharpPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitGeneratedCSharpPath))!);
            File.WriteAllText(emitGeneratedCSharpPath, cs);
        }

        var bytes = RoslynCompiler.CompileToAssemblyBytes(
            cs,
            assemblyName: Path.GetFileNameWithoutExtension(entryPath),
            outputKind,
            diagnostics,
            allowUnsafe);

        if (bytes is null)
        {
            return null;
        }

        return new CompiledAssembly(bytes, cs);
    }
}
