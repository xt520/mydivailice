using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Alice.Compiler;

internal static class RoslynCompiler
{
    internal static byte[]? CompileToAssemblyBytes(
        string csharpSource,
        string assemblyName,
        OutputKind outputKind,
        TextWriter diagnostics,
        bool allowUnsafe = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        var references = GetTrustedPlatformReferences();

        try
        {
            var stdAsm = typeof(global::Alice.Std.io.__AliceModule_io).Assembly;
            if (!string.IsNullOrWhiteSpace(stdAsm.Location))
            {
                references.Add(MetadataReference.CreateFromFile(stdAsm.Location));
            }
        }
        catch (Exception ex)
        {
            diagnostics.WriteLine(ex.ToString());
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(outputKind)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(allowUnsafe));

        using var peStream = new MemoryStream();
        var result = compilation.Emit(peStream);
        if (!result.Success)
        {
            foreach (var d in result.Diagnostics)
            {
                diagnostics.WriteLine(d.ToString());
            }

            return null;
        }

        return peStream.ToArray();
    }

    internal static byte[]? CompileToAssemblyBytes(
        string csharpSource,
        string assemblyName,
        TextWriter diagnostics)
    {
        return CompileToAssemblyBytes(csharpSource, assemblyName, OutputKind.ConsoleApplication, diagnostics, allowUnsafe: false);
    }

    private static List<MetadataReference> GetTrustedPlatformReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
            };
        }

        return tpa
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
