using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Alice.Compiler;

internal sealed class ModuleLoader
{
    private readonly List<string> _roots;
    private readonly List<string> _stdRoots;
    private readonly Dictionary<string, CompilationUnit> _loaded;
    private readonly Stack<string> _stack;

    internal ModuleLoader(IEnumerable<string> roots)
    {
        _roots = roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _stdRoots = _roots
            .Where(r => string.Equals(Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "std", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _loaded = new Dictionary<string, CompilationUnit>(StringComparer.Ordinal);
        _stack = new Stack<string>();
    }

    internal IReadOnlyList<CompilationUnit> LoadAll(string entryPath, TextWriter diagnostics)
    {
        entryPath = Path.GetFullPath(entryPath);
        var entryUnit = ParseFile(entryPath, diagnostics);
        if (entryUnit is null)
        {
            return Array.Empty<CompilationUnit>();
        }

        var entryModuleName = entryUnit.ModuleName;
        _loaded[entryModuleName] = entryUnit;

        if (!entryUnit.IsInterfaceFile)
        {
            TryLoadSiblingInterfaceFile(entryModuleName, entryUnit, diagnostics);
        }

        _stack.Push(entryModuleName);
        LoadImportsRecursive(entryUnit, diagnostics);
        _stack.Pop();

        var list = new List<CompilationUnit>();
        list.Add(entryUnit);
        foreach (var kv in _loaded)
        {
            if (!ReferenceEquals(kv.Value, entryUnit))
            {
                list.Add(kv.Value);
            }
        }
        return list;
    }

    private void TryLoadSiblingInterfaceFile(string moduleName, CompilationUnit implUnit, TextWriter diagnostics)
    {
        var implPath = Path.GetFullPath(implUnit.SourcePath);
        var ifacePath = Path.ChangeExtension(implPath, ".alicei");
        if (!File.Exists(ifacePath))
        {
            return;
        }
        var parsed = ParseFile(ifacePath, diagnostics);
        if (parsed is null)
        {
            return;
        }
        var ifaceModule = parsed.ModuleName;
        if (!string.Equals(moduleName, ifaceModule, StringComparison.Ordinal))
        {
            diagnostics.WriteLine($"{parsed.SourcePath}(1,1): 错误: 模块名与文件不一致: sibling={moduleName}, file={ifaceModule}");
            return;
        }
        var key = moduleName + "#iface";
        if (_loaded.ContainsKey(key))
        {
            return;
        }
        _loaded[key] = parsed;
    }

    internal bool TryLoadModuleByName(QualifiedName moduleName, TextWriter diagnostics)
    {
        var name = moduleName.ToString();
        if (_loaded.ContainsKey(name))
        {
            return false;
        }

        if (_stack.Contains(name))
        {
            return false;
        }

        var path = ResolveModulePath(moduleName);
        if (path is null)
        {
            return false;
        }

        var parsed = ParseFile(path, diagnostics);
        if (parsed is null)
        {
            return false;
        }

        if (!ValidateModuleNameMatchesFile(name, parsed, diagnostics))
        {
            return false;
        }

        _loaded[name] = parsed;
        if (!parsed.IsInterfaceFile)
        {
            TryLoadSiblingInterfaceFile(name, parsed, diagnostics);
        }
        _stack.Push(name);
        LoadImportsRecursive(parsed, diagnostics);
        _stack.Pop();
        return true;
    }

    internal IReadOnlyList<CompilationUnit> GetAllLoadedUnits(string entryModuleName)
    {
        var list = new List<CompilationUnit>();
        if (_loaded.TryGetValue(entryModuleName, out var entry))
        {
            list.Add(entry);
        }

        foreach (var kv in _loaded.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kv.Key == entryModuleName) continue;
            list.Add(kv.Value);
        }
        return list;
    }

    private void LoadImportsRecursive(CompilationUnit unit, TextWriter diagnostics)
    {
        foreach (var imp in unit.Imports)
        {
            var moduleName = imp.ModuleName.ToString();
            if (_loaded.ContainsKey(moduleName))
            {
                continue;
            }

            if (_stack.Contains(moduleName))
            {
                diagnostics.WriteLine($"{unit.SourcePath}({imp.Span.Line},{imp.Span.Column}): 错误: 检测到循环 import: {string.Join(" -> ", _stack.Reverse())} -> {moduleName}");
                continue;
            }

            var path = ResolveModulePath(imp.ModuleName);
            if (path is null)
            {
                diagnostics.WriteLine($"{unit.SourcePath}({imp.Span.Line},{imp.Span.Column}): 错误: 找不到模块 {moduleName}");
                continue;
            }

            var parsed = ParseFile(path, diagnostics);
            if (parsed is null)
            {
                continue;
            }

            if (!ValidateModuleNameMatchesFile(moduleName, parsed, diagnostics))
            {
                continue;
            }

            _loaded[moduleName] = parsed;
            if (!parsed.IsInterfaceFile)
            {
                TryLoadSiblingInterfaceFile(moduleName, parsed, diagnostics);
            }
            _stack.Push(moduleName);
            LoadImportsRecursive(parsed, diagnostics);
            _stack.Pop();
        }
    }

    internal static List<string> InferRoots(string entryPath, IReadOnlyList<string> extraModulePaths, TextWriter diagnostics)
    {
        entryPath = Path.GetFullPath(entryPath);
        var roots = new List<string>();

        var entryDir = Path.GetDirectoryName(entryPath) ?? Directory.GetCurrentDirectory();
        roots.Add(entryDir);

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(entryPath);
        }
        catch (Exception ex)
        {
            diagnostics.WriteLine(ex.ToString());
            return roots;
        }

        var lexer = new Lexer(sourceText, entryPath);
        var tokens = lexer.LexAll(out var lexDiags);
        if (lexDiags.Count == 0)
        {
            var parser = new Parser(tokens, entryPath);
            var unit = parser.ParseCompilationUnit(out var parseDiags);
            if (parseDiags.Count == 0 && unit.Namespace is not null)
            {
                var nsLen = unit.Namespace.Name.Segments.Count;
                if (nsLen > 0)
                {
                    var rootCandidate = entryDir;
                    for (var i = 0; i < nsLen; i++)
                    {
                        rootCandidate = Path.GetDirectoryName(rootCandidate) ?? rootCandidate;
                    }
                    var baseName = Path.GetFileNameWithoutExtension(entryPath);
                    var expected = Path.Combine(new[] { rootCandidate }.Concat(unit.Namespace.Name.Segments).Concat(new[] { baseName + ".alice" }).ToArray());
                    if (Path.GetFullPath(expected).Equals(entryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        roots.Insert(0, rootCandidate);
                    }
                }
            }
        }

        foreach (var p in extraModulePaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            roots.Add(Path.GetFullPath(p));
        }

        try
        {
            var envStd = Environment.GetEnvironmentVariable("ALICE_STD");
            if (!string.IsNullOrWhiteSpace(envStd) && Directory.Exists(envStd))
            {
                roots.Add(Path.GetFullPath(envStd));
            }
        }
        catch
        {
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var probe = string.IsNullOrWhiteSpace(baseDir) ? null : Path.GetFullPath(baseDir);
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(probe); i++)
            {
                var candidate = Path.Combine(probe, "std");
                if (Directory.Exists(candidate))
                {
                    roots.Add(Path.GetFullPath(candidate));
                    break;
                }
                probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch
        {
        }

        try
        {
            var cwdStd = Path.Combine(Directory.GetCurrentDirectory(), "std");
            if (Directory.Exists(cwdStd))
            {
                roots.Add(Path.GetFullPath(cwdStd));
            }
        }
        catch
        {
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string? ResolveModulePath(QualifiedName moduleName)
    {
        var segs = moduleName.Segments;
        if (segs.Count == 0)
        {
            return null;
        }

        foreach (var root in _roots)
        {
            var basePath = Path.Combine(new[] { root }.Concat(segs).ToArray());

            var candidateAlice = basePath + ".alice";
            if (File.Exists(candidateAlice))
            {
                return Path.GetFullPath(candidateAlice);
            }

            var candidateAliceI = basePath + ".alicei";
            if (File.Exists(candidateAliceI))
            {
                return Path.GetFullPath(candidateAliceI);
            }

            var indexAlice = Path.Combine(basePath, "index.alice");
            if (File.Exists(indexAlice))
            {
                return Path.GetFullPath(indexAlice);
            }

            var indexAliceI = Path.Combine(basePath, "index.alicei");
            if (File.Exists(indexAliceI))
            {
                return Path.GetFullPath(indexAliceI);
            }
        }

        return null;
    }

    private CompilationUnit? ParseFile(string path, TextWriter diagnostics)
    {
        var text = File.ReadAllText(path);
        var lexer = new Lexer(text, path);
        var tokens = lexer.LexAll(out var lexDiagnostics);
        if (lexDiagnostics.Count > 0)
        {
            foreach (var d in lexDiagnostics)
            {
                diagnostics.WriteLine(d.ToString());
            }
            return null;
        }

        var parser = new Parser(tokens, path);
        var unit = parser.ParseCompilationUnit(out var parseDiagnostics);
        if (parseDiagnostics.Count > 0)
        {
            foreach (var d in parseDiagnostics)
            {
                diagnostics.WriteLine(d.ToString());
            }
            return null;
        }

        var full = Path.GetFullPath(path);
        var isStd = _stdRoots.Any(r => full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, r, StringComparison.OrdinalIgnoreCase));
        return unit with { IsStdLib = isStd };
    }

    private static bool ValidateModuleNameMatchesFile(string moduleName, CompilationUnit unit, TextWriter diagnostics)
    {
        var actual = unit.ModuleName;
        if (!string.Equals(moduleName, actual, StringComparison.Ordinal))
        {
            diagnostics.WriteLine($"{unit.SourcePath}(1,1): 错误: 模块名与文件不一致: import={moduleName}, file={actual}");
            return false;
        }
        return true;
    }
}
