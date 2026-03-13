using System;
using System.Linq;

namespace Alice.Compiler;

internal static class Cli
{
    internal sealed record RunOptions(string FilePath, string? EmitCSharpPath, string[] ProgramArgs);
    internal sealed record BuildOptions(string FilePath, string? OutputPath, string? EmitCSharpPath);

    internal sealed record ParsedRun(string FilePath, string? EmitCSharpPath, List<string> ModulePaths, string[] ProgramArgs, bool AllowUnsafe);
    internal sealed record ParsedBuild(string FilePath, string? OutputPath, string? EmitCSharpPath, List<string> ModulePaths, bool AllowUnsafe);
    internal sealed record ParsedCheck(string FilePath, string? EmitCSharpPath, List<string> ModulePaths, bool AllowUnsafe);
    internal sealed record ParsedBind(string RefPath, string OutputDir);

    internal enum PackMode
    {
        Minimal,
        Exe,
        Single,
        Both
    }

    internal sealed record ParsedPackage(string FilePath, string? OutputDir, PackMode Mode, bool SelfContained, string Rid, List<string> ModulePaths, bool AllowUnsafe);

    internal sealed record ParsedEasy(string FilePath, string[] ProgramArgs);

    internal sealed record ParsedTui;

    internal static ParsedRun? ParseRun(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0].StartsWith('-'))
        {
            return null;
        }

        var filePath = args[0];
        string? emitCs = null;
        var modulePaths = new List<string>();
        var programArgs = Array.Empty<string>();
        var allowUnsafe = false;

        var i = 1;
        while (i < args.Length)
        {
            var a = args[i];
            if (a == "--")
            {
                programArgs = args.Skip(i + 1).ToArray();
                break;
            }

            if (a == "--module-path")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                modulePaths.Add(args[i + 1]);
                i += 2;
                continue;
            }

            if (a == "--emit-cs")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                emitCs = args[i + 1];
                i += 2;
                continue;
            }

            if (a == "--allow-unsafe")
            {
                allowUnsafe = true;
                i += 1;
                continue;
            }

            return null;
        }

        return new ParsedRun(filePath, emitCs, modulePaths, programArgs, allowUnsafe);
    }

    internal static ParsedBuild? ParseBuild(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0].StartsWith('-'))
        {
            return null;
        }

        var filePath = args[0];
        string? output = null;
        string? emitCs = null;
        var modulePaths = new List<string>();
        var allowUnsafe = false;

        var i = 1;
        while (i < args.Length)
        {
            var a = args[i];
            if (a == "-o")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                output = args[i + 1];
                i += 2;
                continue;
            }

            if (a == "--module-path")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                modulePaths.Add(args[i + 1]);
                i += 2;
                continue;
            }

            if (a == "--emit-cs")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                emitCs = args[i + 1];
                i += 2;
                continue;
            }

            if (a == "--allow-unsafe")
            {
                allowUnsafe = true;
                i += 1;
                continue;
            }

            return null;
        }

        return new ParsedBuild(filePath, string.IsNullOrWhiteSpace(output) ? null : output, emitCs, modulePaths, allowUnsafe);
    }

    internal static ParsedCheck? ParseCheck(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0].StartsWith('-'))
        {
            return null;
        }

        var filePath = args[0];
        string? emitCs = null;
        var modulePaths = new List<string>();
        var allowUnsafe = false;

        var i = 1;
        while (i < args.Length)
        {
            var a = args[i];
            if (a == "--module-path")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                modulePaths.Add(args[i + 1]);
                i += 2;
                continue;
            }

            if (a == "--emit-cs")
            {
                if (i + 1 >= args.Length)
                {
                    return null;
                }

                emitCs = args[i + 1];
                i += 2;
                continue;
            }

            if (a == "--allow-unsafe")
            {
                allowUnsafe = true;
                i += 1;
                continue;
            }

            return null;
        }

        return new ParsedCheck(filePath, emitCs, modulePaths, allowUnsafe);
    }

    internal static ParsedEasy? ParseEasy(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        if (args[0].StartsWith('-'))
        {
            return null;
        }

        var filePath = args[0];
        var progArgs = Array.Empty<string>();
        var dd = Array.IndexOf(args, "--");
        if (dd >= 0)
        {
            progArgs = args.Skip(dd + 1).ToArray();
        }
        return new ParsedEasy(filePath, progArgs);
    }

    internal static ParsedTui? ParseTui(string[] args)
    {
        if (args.Length == 0) return new ParsedTui();
        return null;
    }

    internal static ParsedBind? ParseBind(string[] args)
    {
        string? refPath = null;
        string? outDir = null;

        var i = 0;
        while (i < args.Length)
        {
            var a = args[i];
            if (a == "--ref")
            {
                if (i + 1 >= args.Length) return null;
                refPath = args[i + 1];
                i += 2;
                continue;
            }
            if (a == "-o")
            {
                if (i + 1 >= args.Length) return null;
                outDir = args[i + 1];
                i += 2;
                continue;
            }
            return null;
        }

        if (string.IsNullOrWhiteSpace(refPath) || string.IsNullOrWhiteSpace(outDir)) return null;
        return new ParsedBind(refPath!, outDir!);
    }

    internal static ParsedPackage? ParsePackage(string[] args)
    {
        if (args.Length == 0) return null;
        if (args[0].StartsWith('-')) return null;

        var filePath = args[0];
        string? outDir = null;
        var mode = PackMode.Minimal;
        var selfContained = false;
        var rid = "win-x64";
        var modulePaths = new List<string>();
        var allowUnsafe = false;

        var i = 1;
        while (i < args.Length)
        {
            var a = args[i];
            if (a == "-o")
            {
                if (i + 1 >= args.Length) return null;
                outDir = args[i + 1];
                i += 2;
                continue;
            }
            if (a == "--mode")
            {
                if (i + 1 >= args.Length) return null;
                var m = args[i + 1];
                mode = m switch
                {
                    "minimal" => PackMode.Minimal,
                    "exe" => PackMode.Exe,
                    "single" => PackMode.Single,
                    "both" => PackMode.Both,
                    _ => mode
                };
                if (m != "minimal" && m != "exe" && m != "single" && m != "both") return null;
                i += 2;
                continue;
            }
            if (a == "--self-contained")
            {
                selfContained = true;
                i += 1;
                continue;
            }
            if (a == "--rid")
            {
                if (i + 1 >= args.Length) return null;
                rid = args[i + 1];
                i += 2;
                continue;
            }
            if (a == "--module-path")
            {
                if (i + 1 >= args.Length) return null;
                modulePaths.Add(args[i + 1]);
                i += 2;
                continue;
            }
            if (a == "--allow-unsafe")
            {
                allowUnsafe = true;
                i += 1;
                continue;
            }
            return null;
        }

        return new ParsedPackage(filePath, string.IsNullOrWhiteSpace(outDir) ? null : outDir, mode, selfContained, rid, modulePaths, allowUnsafe);
    }
}
