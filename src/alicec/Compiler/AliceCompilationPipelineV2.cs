using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Alice.Compiler;

internal static class AliceCompilationPipelineV2
{
    internal sealed record ModuleOptions(IReadOnlyList<string> ModulePaths);

    internal static string? CompileToCSharpFromEntry(string entryPath, ModuleOptions options, TextWriter diagnostics, bool allowUnsafe)
    {
        var roots = ModuleLoader.InferRoots(entryPath, options.ModulePaths, diagnostics);
        var loader = new ModuleLoader(roots);
        var units0 = loader.LoadAll(entryPath, diagnostics);
        if (units0.Count == 0)
        {
            return null;
        }

        var entryModuleName = units0[0].ModuleName;
        var units = units0;

        for (var iter = 0; iter < 4; iter++)
        {
            var binder0 = new Binder(units, diagnostics, strictUnknownTypes: false);
            var binding0 = binder0.Bind();
            var loadedAny = false;
            foreach (var qn in binding0.MissingModules)
            {
                loadedAny |= loader.TryLoadModuleByName(qn, diagnostics);
            }
            if (!loadedAny)
            {
                units = loader.GetAllLoadedUnits(entryModuleName);
                break;
            }
            units = loader.GetAllLoadedUnits(entryModuleName);
        }

        if (!allowUnsafe)
        {
            foreach (var u in units)
            {
                foreach (var f in u.Functions)
                {
                    if (f.Attributes.Contains("unsafe", StringComparer.Ordinal))
                    {
                        diagnostics.WriteLine($"{u.SourcePath}({f.Span.Line},{f.Span.Column}): 错误: 使用 @unsafe 需要 --allow-unsafe");
                        return null;
                    }
                }
            }
        }

        var binder = new Binder(units, diagnostics, strictUnknownTypes: true);
        var binding = binder.Bind();
        if (binding.MissingModules.Count > 0)
        {
            return null;
        }
        if (binding.HasErrors)
        {
            return null;
        }

        var emitter = new CSharpEmitter(entryPath);
        return emitter.EmitProgram(binding);
    }
}
