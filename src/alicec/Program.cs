using System;
using System.IO;
using System.Linq;
using Alice.Compiler;

namespace alicec;

internal static class Program
{
    private static int PrintUsage(TextWriter to)
    {
        to.WriteLine("用法:");
        to.WriteLine("  alicec <file.alice> [-- <args...>]    (傻瓜式直跑，等价于 run + 自动 module-path 推断)");
        to.WriteLine("  alicec run <file.alice> [--emit-cs <path>] [--module-path <dir> ...] [-- <args...>]");
        to.WriteLine("      额外选项: --allow-unsafe  (允许生成/编译 unsafe C#)");
        to.WriteLine("  alicec r <file.alice> [-- <args...>]  (run 别名)");
        to.WriteLine("  alicec build <file.alice> [-o <out.dll>] [--emit-cs <path>] [--module-path <dir> ...]");
        to.WriteLine("      额外选项: --allow-unsafe  (允许生成/编译 unsafe C#)");
        to.WriteLine("  alicec b <file.alice> [-o <out.dll>]  (build 别名)");
        to.WriteLine("  alicec check <file.alice> [--emit-cs <path>] [--module-path <dir> ...]");
        to.WriteLine("      额外选项: --allow-unsafe  (允许生成/编译 unsafe C#)");
        to.WriteLine("  alicec compile <file.alice> ...       (check 别名)");
        to.WriteLine("  alicec bind --ref <path.dll> -o <outDir>");
        to.WriteLine("  alicec package <file.alice> [-o <outDir>] [--mode minimal|exe|single|both] [--rid <rid>] [--self-contained] [--module-path <dir> ...]");
        to.WriteLine("      额外选项: --allow-unsafe  (允许生成/编译 unsafe C#)");
        to.WriteLine("  alicec -tui");
        to.WriteLine("  alicec selftest");
        return 2;
    }

    public static async System.Threading.Tasks.Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return PrintUsage(Console.Error);
            }

            if (!args[0].StartsWith('-') && args[0].EndsWith(".alice", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = Cli.ParseEasy(args);
                if (parsed is null)
                {
                    return PrintUsage(Console.Error);
                }

                var compiled = AliceCompiler.CompileEntryToAssembly(
                    parsed.FilePath,
                    modulePaths: Array.Empty<string>(),
                    emitGeneratedCSharpPath: null,
                    diagnostics: Console.Error);

                if (compiled is null)
                {
                    return 1;
                }

                return AliceRunner.RunInProcess(compiled.AssemblyBytes, parsed.ProgramArgs);
            }

            var command = args[0];
            var rest = args.Skip(1).ToArray();

            switch (command)
            {
                case "-tui":
                {
                    return await alicec.Tui.TuiApp.RunAsync(Console.Error).ConfigureAwait(false);
                }
                case "run":
                case "r":
                {
                    var parsed = Cli.ParseRun(rest);
                    if (parsed is null)
                    {
                        return PrintUsage(Console.Error);
                    }

                        var compiled = AliceCompiler.CompileEntryToAssembly(
                            parsed.FilePath,
                            parsed.ModulePaths,
                            emitGeneratedCSharpPath: parsed.EmitCSharpPath,
                            outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                            diagnostics: Console.Error,
                            allowUnsafe: parsed.AllowUnsafe);

                    if (compiled is null)
                    {
                        return 1;
                    }

                    return AliceRunner.RunInProcess(compiled.AssemblyBytes, parsed.ProgramArgs);
                }
                case "build":
                case "b":
                {
                    var parsed = Cli.ParseBuild(rest);
                    if (parsed is null)
                    {
                        return PrintUsage(Console.Error);
                    }

                    var outputPath = parsed.OutputPath;
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        var srcFull0 = Path.GetFullPath(parsed.FilePath);
                        var srcDir0 = Path.GetDirectoryName(srcFull0) ?? Directory.GetCurrentDirectory();
                        var baseName0 = Path.GetFileNameWithoutExtension(srcFull0);
                        var outDir0 = Path.Combine(srcDir0, "artifacts");
                        Directory.CreateDirectory(outDir0);
                        outputPath = Path.Combine(outDir0, baseName0 + ".dll");
                    }

                    var outputKind = global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;

                    var compiled = AliceCompiler.CompileEntryToAssembly(
                        parsed.FilePath,
                        parsed.ModulePaths,
                        emitGeneratedCSharpPath: parsed.EmitCSharpPath,
                        outputKind,
                        diagnostics: Console.Error,
                        allowUnsafe: parsed.AllowUnsafe);

                    if (compiled is null)
                    {
                        return 1;
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.OutputPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                    }

                    File.WriteAllBytes(outputPath, compiled.AssemblyBytes);

                    var runtimeConfigPath = Path.ChangeExtension(outputPath, ".runtimeconfig.json");
                    File.WriteAllText(runtimeConfigPath, RuntimeConfig.CreateNet8FrameworkDependent());

                    var launcherPath = Path.ChangeExtension(outputPath, ".cmd");
                    File.WriteAllText(launcherPath, LauncherScripts.CreateDotnetCmd(Path.GetFileName(outputPath)!));
                    return 0;
                }
                case "check":
                case "compile":
                {
                    var parsed = Cli.ParseCheck(rest);
                    if (parsed is null)
                    {
                        return PrintUsage(Console.Error);
                    }

                    var compiled = AliceCompiler.CompileEntryToAssembly(
                        parsed.FilePath,
                        parsed.ModulePaths,
                        emitGeneratedCSharpPath: parsed.EmitCSharpPath,
                        outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                        diagnostics: Console.Error,
                        allowUnsafe: parsed.AllowUnsafe);

                    return compiled is null ? 1 : 0;
                }
                case "selftest":
                {
                    return SelfTest.RunAll(Console.Out, Console.Error);
                }
                case "bind":
                {
                    var parsed = Alice.Compiler.Cli.ParseBind(rest);
                    if (parsed is null)
                    {
                        return PrintUsage(Console.Error);
                    }

                    return BindTool.Run(new BindTool.BindOptions(parsed.RefPath, parsed.OutputDir), Console.Error);
                }
                case "package":
                {
                    return await RunPackageAsync(rest).ConfigureAwait(false);
                }
                case "lsp":
                {
                    return await alicec.Lsp.OlsServer.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error).ConfigureAwait(false);
                }
                default:
                    return PrintUsage(Console.Error);
            }
        }
        catch (System.IO.FileNotFoundException fnf)
        {
            Console.Error.WriteLine("错误：入口文件不存在");
            Console.Error.WriteLine($"路径：{fnf.FileName}");
            return 1;
        }
        catch (System.IO.DirectoryNotFoundException dnf)
        {
            Console.Error.WriteLine("错误：入口路径不存在");
            Console.Error.WriteLine(dnf.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    internal static int SelfTestFromTui() => SelfTest.RunAll(Console.Out, Console.Error);

    internal static async System.Threading.Tasks.Task<int> RunCommandFromTuiAsync(string[] args)
    {
        if (args.Length == 0) return 2;
        var cmd = args[0];
        var rest = args.Skip(1).ToArray();
        return cmd switch
        {
            "run" => RunFromTuiRun(rest),
            "build" => RunFromTuiBuild(rest),
            "package" => await RunPackageAsync(rest).ConfigureAwait(false),
            "selftest" => SelfTestFromTui(),
            _ => 2
        };
    }

    private static int RunFromTuiRun(string[] rest)
    {
        var parsed = Cli.ParseRun(rest);
        if (parsed is null) return 2;

        var compiled = AliceCompiler.CompileEntryToAssembly(
            parsed.FilePath,
            parsed.ModulePaths,
            emitGeneratedCSharpPath: parsed.EmitCSharpPath,
            outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            diagnostics: Console.Error,
            allowUnsafe: parsed.AllowUnsafe);

        if (compiled is null) return 1;
        return AliceRunner.RunInProcess(compiled.AssemblyBytes, parsed.ProgramArgs);
    }

    private static int RunFromTuiBuild(string[] rest)
    {
        var parsed = Cli.ParseBuild(rest);
        if (parsed is null) return 2;

        var outputPath = parsed.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var srcFull0 = Path.GetFullPath(parsed.FilePath);
            var srcDir0 = Path.GetDirectoryName(srcFull0) ?? Directory.GetCurrentDirectory();
            var baseName0 = Path.GetFileNameWithoutExtension(srcFull0);
            var outDir0 = Path.Combine(srcDir0, "artifacts");
            Directory.CreateDirectory(outDir0);
            outputPath = Path.Combine(outDir0, baseName0 + ".dll");
        }

        var compiled = AliceCompiler.CompileEntryToAssembly(
            parsed.FilePath,
            parsed.ModulePaths,
            emitGeneratedCSharpPath: parsed.EmitCSharpPath,
            outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            diagnostics: Console.Error,
            allowUnsafe: parsed.AllowUnsafe);

        if (compiled is null) return 1;
        File.WriteAllBytes(outputPath, compiled.AssemblyBytes);
        File.WriteAllText(Path.ChangeExtension(outputPath, ".runtimeconfig.json"), RuntimeConfig.CreateNet8FrameworkDependent());
        File.WriteAllText(Path.ChangeExtension(outputPath, ".cmd"), LauncherScripts.CreateDotnetCmd(Path.GetFileName(outputPath)!));
        return 0;
    }

    private static async System.Threading.Tasks.Task<int> RunPackageAsync(string[] args)
    {
        var parsed = Alice.Compiler.Cli.ParsePackage(args);
        if (parsed is null)
        {
            return PrintUsage(Console.Error);
        }

        var outDir = parsed.OutputDir;
        if (string.IsNullOrWhiteSpace(outDir))
        {
            var srcFull0 = Path.GetFullPath(parsed.FilePath);
            var srcDir0 = Path.GetDirectoryName(srcFull0) ?? Directory.GetCurrentDirectory();
            var baseName0 = Path.GetFileNameWithoutExtension(srcFull0);
            outDir = Path.Combine(srcDir0, "artifacts", baseName0 + ".pkg");
        }

        Directory.CreateDirectory(outDir);

        if (parsed.Mode is Alice.Compiler.Cli.PackMode.Minimal or Alice.Compiler.Cli.PackMode.Both)
        {
            var minimalOut = Path.Combine(outDir, "minimal");
            Directory.CreateDirectory(minimalOut);
            var dllPath = Path.Combine(minimalOut, Path.GetFileNameWithoutExtension(parsed.FilePath) + ".dll");

            var compiled = AliceCompiler.CompileEntryToAssembly(
                parsed.FilePath,
                parsed.ModulePaths,
                emitGeneratedCSharpPath: null,
                outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                diagnostics: Console.Error,
                allowUnsafe: parsed.AllowUnsafe);

            if (compiled is null) return 1;

            File.WriteAllBytes(dllPath, compiled.AssemblyBytes);
            File.WriteAllText(Path.ChangeExtension(dllPath, ".runtimeconfig.json"), RuntimeConfig.CreateNet8FrameworkDependent());

            var stdDll = Path.Combine(AppContext.BaseDirectory, "Alice.Std.dll");
            if (!File.Exists(stdDll))
            {
                try
                {
                    var loc = typeof(global::Alice.Std.io.__AliceModule_io).Assembly.Location;
                    if (!string.IsNullOrWhiteSpace(loc) && File.Exists(loc)) stdDll = loc;
                }
                catch
                {
                }
            }

            if (File.Exists(stdDll))
            {
                File.Copy(stdDll, Path.Combine(minimalOut, "Alice.Std.dll"), overwrite: true);
            }
            else
            {
                Console.Error.WriteLine("警告：未找到 Alice.Std.dll，minimal 产物运行时可能报缺少程序集");
            }

            var baseName = Path.GetFileNameWithoutExtension(dllPath);
            var ps1 = string.Join("\n", new[]
            {
                "#!/usr/bin/env pwsh",
                "$ErrorActionPreference = 'Stop'",
                "$dll = Join-Path $PSScriptRoot '" + baseName + ".dll'",
                "& dotnet $dll @Args",
                "exit $LASTEXITCODE",
                ""
            });
            File.WriteAllText(Path.Combine(minimalOut, baseName + ".ps1"), ps1);

            var sh = string.Join("\n", new[]
            {
                "#!/usr/bin/env sh",
                "set -e",
                "here=\"$(cd \"$(dirname \"$0\")\" && pwd)\"",
                "exec dotnet \"$here/" + baseName + ".dll\" \"$@\"",
                ""
            });
            File.WriteAllText(Path.Combine(minimalOut, baseName + ".sh"), sh);
        }

        if (parsed.Mode is Alice.Compiler.Cli.PackMode.Exe or Alice.Compiler.Cli.PackMode.Both)
        {
            var exeOut = Path.Combine(outDir, "exe");
            Directory.CreateDirectory(exeOut);
            var baseName = Path.GetFileNameWithoutExtension(parsed.FilePath);

            var compiled = AliceCompiler.CompileEntryToAssembly(
                parsed.FilePath,
                parsed.ModulePaths,
                emitGeneratedCSharpPath: null,
                outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                diagnostics: Console.Error,
                allowUnsafe: parsed.AllowUnsafe);

            if (compiled is null) return 1;

            var dllPath = Path.Combine(exeOut, baseName + ".dll");
            File.WriteAllBytes(dllPath, compiled.AssemblyBytes);
            File.WriteAllText(Path.ChangeExtension(dllPath, ".runtimeconfig.json"), RuntimeConfig.CreateNet8FrameworkDependent());

            var stdDll = Path.Combine(AppContext.BaseDirectory, "Alice.Std.dll");
            if (!File.Exists(stdDll))
            {
                try
                {
                    var loc = typeof(global::Alice.Std.io.__AliceModule_io).Assembly.Location;
                    if (!string.IsNullOrWhiteSpace(loc) && File.Exists(loc)) stdDll = loc;
                }
                catch
                {
                }
            }
            if (File.Exists(stdDll))
            {
                File.Copy(stdDll, Path.Combine(exeOut, "Alice.Std.dll"), overwrite: true);
            }

            var finalExeName = baseName + (global::System.OperatingSystem.IsWindows() ? ".exe" : string.Empty);
            var finalExePath = Path.Combine(exeOut, finalExeName);

            if (global::System.OperatingSystem.IsWindows())
            {
                var template = Path.Combine(AppContext.BaseDirectory, "runners", "alicec.exe");
                if (!File.Exists(template))
                {
                    template = Path.Combine(AppContext.BaseDirectory, "alicec.exe");
                }
                if (!File.Exists(template))
                {
                    Console.Error.WriteLine("错误：找不到 apphost 模板（alicec.exe）。");
                    return 1;
                }

                AppHostPatcher.CreatePatchedAppHost(template, finalExePath, baseName + ".dll");
            }
            else
            {
                Console.Error.WriteLine("错误：exe 模式目前仅支持 Windows；其他平台请使用 minimal 或 single");
                return 1;
            }
        }

                    if (parsed.Mode is Alice.Compiler.Cli.PackMode.Single or Alice.Compiler.Cli.PackMode.Both)
                    {
                        var singleOut = Path.Combine(outDir, parsed.SelfContained ? "single-selfcontained" : "single");
                        Directory.CreateDirectory(singleOut);

                        var runnerWorkDir = Path.Combine(singleOut, "_runner");
                        Directory.CreateDirectory(runnerWorkDir);

            var compiled = AliceCompiler.CompileEntryToAssembly(
                parsed.FilePath,
                parsed.ModulePaths,
                emitGeneratedCSharpPath: null,
                outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                diagnostics: Console.Error,
                allowUnsafe: parsed.AllowUnsafe);

            if (compiled is null) return 1;

                        var runnerExe = await Alice.Compiler.PackedRunner.PublishSingleFileRunnerAsync(runnerWorkDir, parsed.Rid, parsed.SelfContained, Console.Error)
                            .ConfigureAwait(false);

            if (runnerExe is null) return 1;

                        var finalName = Path.GetFileNameWithoutExtension(parsed.FilePath) + (global::System.OperatingSystem.IsWindows() ? ".exe" : string.Empty);
                        var finalPath = Path.Combine(singleOut, finalName);
                        File.Copy(runnerExe, finalPath, overwrite: true);
                        Alice.Compiler.PackedRunner.AppendPayload(finalPath, compiled.AssemblyBytes);

                        try { Directory.Delete(runnerWorkDir, recursive: true); } catch { }
                    }

        return 0;
    }
}
