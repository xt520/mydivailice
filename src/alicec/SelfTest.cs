using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alice.Compiler;

namespace alicec;

internal static class SelfTest
{
    internal static int RunAll(TextWriter stdout, TextWriter stderr)
    {
        var tests = new List<TestCase>
        {
            new("__bindtest__", Array.Empty<string>(), "ok"),
            new("examples/bubble_sort.alice", Array.Empty<string>(), "[1, 2, 4, 5, 8]"),
            new("examples/mod/Main.alice", Array.Empty<string>(), "[1, 2, 4, 5, 8]"),

            new("examples/oop/Counter.alice", Array.Empty<string>(), "2"),
            new("examples/oop/Main.alice", Array.Empty<string>(), "3"),
            new("examples/oop/InterfaceMain.alice", Array.Empty<string>(), "30"),
            new("examples/oop/InterfaceEmbed.alice", Array.Empty<string>(), "3"),
            new("examples/oop/Inherit.alice", Array.Empty<string>(), "1\n2"),

            new("examples/fn/FuncRef.alice", Array.Empty<string>(), "30"),
            new("examples/fn/Closure.alice", Array.Empty<string>(), "8"),

            new("examples/ex/TryExcept.alice", Array.Empty<string>(), "caught\nfinally"),
            new("examples/gen/GenericClassSmoke.alice", Array.Empty<string>(), "3"),
            new("examples/gen/TypeAliasAndGenericFun.alice", Array.Empty<string>(), "30"),
            new("examples/gen/AssociatedTypeSmoke.alice", Array.Empty<string>(), "3"),
            new("examples/gen/GenericClassDefault.alice", Array.Empty<string>(), "3"),
            new("examples/gen/Constraints.alice", Array.Empty<string>(), "AB"),
            new("examples/gen/AssociatedTypesSequence.alice", Array.Empty<string>(), "1\n6"),
            new("examples/gen/GenericTypeAlias.alice", Array.Empty<string>(), "[1, 2, 3]"),

            new("examples/iface/InterfaceFileFunSig.alice", Array.Empty<string>(), "30"),

            new("examples/io/FileRoundtrip.alice", Array.Empty<string>(), "hello"),
            new("examples/io/DeferClose.alice", Array.Empty<string>(), "ok"),
            new("examples/net/TcpEcho.alice", Array.Empty<string>(), "pong"),
            new("examples/net/UdpEcho.alice", Array.Empty<string>(), "pong"),
            new("examples/http/HttpServerClient.alice", Array.Empty<string>(), "ok"),

            new("examples/collections/ListMap.alice", Array.Empty<string>(), "3\n30"),

            new("examples/enum/Basic.alice", Array.Empty<string>(), "1\n2\n10"),
            new("examples/struct/Point.alice", Array.Empty<string>(), "(1,2)\n3"),

            new("examples/sync/ChanPingPong.alice", Array.Empty<string>(), "ping"),

            new("examples/bytes/Basic.alice", Array.Empty<string>(), "16909060\nab"),

            new("examples/crypto/Sha256Len.alice", Array.Empty<string>(), "32"),
            new("examples/crypto/RandomInt.alice", Array.Empty<string>(), "True\n98\n90"),

            new("examples/tls/Smoke.alice", Array.Empty<string>(), "1"),
            new("examples/time/StopwatchBasic.alice", Array.Empty<string>(), "True\nTrue"),

            new("examples/async/DelaySmoke.alice", Array.Empty<string>(), "ok"),
            new("examples/async/AsyncFun.alice", Array.Empty<string>(), "30"),
            new("examples/async/GoThen.alice", Array.Empty<string>(), "3"),
            new("examples/async/Timeout.alice", Array.Empty<string>(), "timeout"),

            new("examples/unsafe/MemSmoke.alice", Array.Empty<string>(), "ok") { CompilerArgs = new[] { "--allow-unsafe" } },
            new("examples/unsafe/NativeBufferCopy.alice", Array.Empty<string>(), "abc") { CompilerArgs = new[] { "--allow-unsafe" } },
            new("examples/unsafe/PtrU8Write.alice", Array.Empty<string>(), "abc") { CompilerArgs = new[] { "--allow-unsafe" } },
            new("examples/unsafe/PtrGenericSmoke.alice", Array.Empty<string>(), "xyz{") { CompilerArgs = new[] { "--allow-unsafe" } },
            new("examples/unsafe/MemcpyPtrSmoke.alice", Array.Empty<string>(), "abc") { CompilerArgs = new[] { "--allow-unsafe" } },

            new("examples/slice/Basic.alice", Array.Empty<string>(), "[2, 3, 4]\n[2, 3, 4]\n[1, 2, 99, 4, 5]"),

            new("examples/cast/Basic.alice", Array.Empty<string>(), "A\n65"),

            new("test/main4.ailice", Array.Empty<string>(), "A") { CompilerArgs = new[] { "--allow-unsafe" } },

        };

        var ok = 0;
        foreach (var t in tests)
        {
            if (t.FilePath == "__bindtest__")
            {
                if (!RunBindSelfTest(stderr))
                {
                    stderr.WriteLine("[FAIL] __bindtest__");
                    continue;
                }
                stdout.WriteLine("[OK] __bindtest__");
                ok++;
                continue;
            }

            var actual = RunAndCapture(t.FilePath, modulePaths: t.ModulePaths, compilerArgs: t.CompilerArgs, programArgs: t.ProgramArgs, stderr);
            if (actual is null)
            {
                stderr.WriteLine($"[FAIL] {t.FilePath}: 编译或运行失败");
                continue;
            }

            var normalized = NormalizeNewlines(actual);
            if (normalized == t.ExpectedOutput)
            {
                stdout.WriteLine($"[OK] {t.FilePath}");
                ok++;
                continue;
            }

            stderr.WriteLine($"[FAIL] {t.FilePath}");
            stderr.WriteLine(DiffUtil.FormatFirstDifference(t.ExpectedOutput, normalized));
            stderr.WriteLine("--- expected ---");
            stderr.WriteLine(t.ExpectedOutput);
            stderr.WriteLine("--- actual ---");
            stderr.WriteLine(normalized);
        }

        if (ok == tests.Count)
        {
            stdout.WriteLine($"selftest: {ok}/{tests.Count} 通过");
            return 0;
        }

        stderr.WriteLine($"selftest: {ok}/{tests.Count} 通过");
        return 1;
    }

    private static bool RunBindSelfTest(TextWriter stderr)
    {
        try
        {
            var refPath = typeof(global::Alice.Std.io.__AliceModule_io).Assembly.Location;
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "selftest-bind");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
            Directory.CreateDirectory(outDir);

            var exit = BindTool.Run(new BindTool.BindOptions(refPath, outDir), stderr);
            if (exit != 0) return false;

            var probe = Path.Combine(outDir, "Alice", "Std", "io", "index.alicei");
            if (!File.Exists(probe)) return false;
            var text = File.ReadAllText(probe);
            return text.Contains("fun open", StringComparison.Ordinal) && text.Contains("class File", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.ToString());
            return false;
        }
    }

    private sealed record TestCase(string FilePath, string[] ModulePaths, string ExpectedOutput)
    {
        public string[] ProgramArgs { get; init; } = Array.Empty<string>();
        public string[] CompilerArgs { get; init; } = Array.Empty<string>();
    }

    private static string? RunAndCapture(string filePath, string[] modulePaths, string[] compilerArgs, string[] programArgs, TextWriter stderr)
    {
        var sw = new StringWriter();
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        try
        {
            Console.SetOut(sw);
            Console.SetError(stderr);

            var allowUnsafe = compilerArgs.Contains("--allow-unsafe", StringComparer.Ordinal);
            var compiled = AliceCompiler.CompileEntryToAssembly(
                filePath,
                modulePaths,
                emitGeneratedCSharpPath: null,
                outputKind: global::Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
                diagnostics: stderr,
                allowUnsafe: allowUnsafe);

            if (compiled is null)
            {
                return null;
            }

            var exit = AliceRunner.RunInProcess(compiled.AssemblyBytes, programArgs);
            if (exit != 0)
            {
                return null;
            }

            return sw.ToString().TrimEnd();
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");
}
