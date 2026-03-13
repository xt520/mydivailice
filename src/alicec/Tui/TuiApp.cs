using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alice.Compiler;

namespace alicec.Tui;

internal static class TuiApp
{
    internal static async Task<int> RunAsync(TextWriter output)
    {
        var workDir = Directory.GetCurrentDirectory();
        while (true)
        {
            await output.WriteLineAsync($"Alice TUI  (工作目录: {workDir})").ConfigureAwait(false);
            await output.WriteLineAsync("1) 运行 run").ConfigureAwait(false);
            await output.WriteLineAsync("2) 构建 build").ConfigureAwait(false);
            await output.WriteLineAsync("3) 打包 package (minimal)").ConfigureAwait(false);
            await output.WriteLineAsync("4) 打包 package (exe)").ConfigureAwait(false);
            await output.WriteLineAsync("5) 打包 package (single)").ConfigureAwait(false);
            await output.WriteLineAsync("6) 打包 package (both)").ConfigureAwait(false);
            await output.WriteLineAsync("7) selftest").ConfigureAwait(false);
            await output.WriteLineAsync("8) 切换工作目录 (cd)").ConfigureAwait(false);
            await output.WriteLineAsync("0) 退出").ConfigureAwait(false);
            await output.WriteAsync("> ").ConfigureAwait(false);

            var choice = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(choice)) continue;

            switch (choice.Trim())
            {
                case "0":
                    return 0;
                case "7":
                    return Program.SelfTestFromTui();
                case "8":
                    await output.WriteLineAsync("输入要切换到的目录（留空取消）：").ConfigureAwait(false);
                    await output.WriteAsync("> ").ConfigureAwait(false);
                    var newDir = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(newDir)) break;
                    newDir = newDir.Trim();
                    if (!Directory.Exists(newDir))
                    {
                        await output.WriteLineAsync("错误：目录不存在").ConfigureAwait(false);
                        break;
                    }
                    workDir = Path.GetFullPath(newDir);
                    break;
                case "1":
                    await output.WriteLineAsync("输入要运行的 .alice/.ailice 路径（留空自动查找当前目录入口）：").ConfigureAwait(false);
                    await output.WriteAsync("> ").ConfigureAwait(false);
                    var runPath = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(runPath))
                    {
                        var detected = DetectSingleEntryInDirectory(output, workDir);
                        if (detected is null) break;
                        workDir = Path.GetDirectoryName(Path.GetFullPath(detected)) ?? workDir;
                        return await Program.RunCommandFromTuiAsync(new[] { "run", detected }).ConfigureAwait(false);
                    }
                    {
                        var p = ResolvePath(workDir, runPath.Trim());
                        if (p is not null) workDir = Path.GetDirectoryName(p) ?? workDir;
                        return await Program.RunCommandFromTuiAsync(new[] { "run", p ?? runPath.Trim() }).ConfigureAwait(false);
                    }
                case "2":
                    await output.WriteLineAsync("输入要构建的 .alice/.ailice 路径（留空自动查找当前目录入口）：").ConfigureAwait(false);
                    await output.WriteAsync("> ").ConfigureAwait(false);
                    var buildPath = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(buildPath))
                    {
                        var detected = DetectSingleEntryInDirectory(output, workDir);
                        if (detected is null) break;
                        workDir = Path.GetDirectoryName(Path.GetFullPath(detected)) ?? workDir;
                        return await Program.RunCommandFromTuiAsync(new[] { "build", detected }).ConfigureAwait(false);
                    }
                    {
                        var p = ResolvePath(workDir, buildPath.Trim());
                        if (p is not null) workDir = Path.GetDirectoryName(p) ?? workDir;
                        return await Program.RunCommandFromTuiAsync(new[] { "build", p ?? buildPath.Trim() }).ConfigureAwait(false);
                    }
                case "4":
                case "5":
                case "6":
                    await output.WriteLineAsync("输入要打包的 .alice/.ailice 路径（留空自动查找当前目录入口）：").ConfigureAwait(false);
                    await output.WriteAsync("> ").ConfigureAwait(false);
                    var pkgPath = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(pkgPath))
                    {
                        var detected = DetectSingleEntryInDirectory(output, workDir);
                        if (detected is null) break;
                        workDir = Path.GetDirectoryName(Path.GetFullPath(detected)) ?? workDir;
                        var mode = choice.Trim() switch
                        {
                            "3" => "minimal",
                            "4" => "exe",
                            "5" => "single",
                            _ => "both"
                        };
                        return await Program.RunCommandFromTuiAsync(new[] { "package", detected, "--mode", mode }).ConfigureAwait(false);
                    }
                    var mode2 = choice.Trim() switch
                    {
                        "3" => "minimal",
                        "4" => "exe",
                        "5" => "single",
                        _ => "both"
                    };
                    {
                        var p = ResolvePath(workDir, pkgPath.Trim());
                        if (p is not null) workDir = Path.GetDirectoryName(p) ?? workDir;
                        return await Program.RunCommandFromTuiAsync(new[] { "package", p ?? pkgPath.Trim(), "--mode", mode2 }).ConfigureAwait(false);
                    }
            }

            await output.WriteLineAsync().ConfigureAwait(false);
        }
    }

    private static string? ResolvePath(string workDir, string input)
    {
        try
        {
            var p = input;
            if (!Path.IsPathRooted(p))
            {
                p = Path.Combine(workDir, p);
            }
            p = Path.GetFullPath(p);
            return p;
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectSingleEntryInDirectory(TextWriter output, string dir)
    {
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.EnumerateFiles(dir, "*.alice", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.EnumerateFiles(dir, "*.ailice", SearchOption.TopDirectoryOnly));
        }
        catch (Exception ex)
        {
            output.WriteLine($"错误：无法扫描目录：{ex.Message}");
            return null;
        }

        if (files.Count == 0)
        {
            output.WriteLine("错误：工作目录没有 .alice/.ailice 文件，请输入路径或切换目录");
            return null;
        }

        var candidates = files
            .Where(f => HasTopLevelMain(f))
            .ToList();

        if (candidates.Count == 1)
        {
            output.WriteLine($"自动选择入口：{candidates[0]}");
            return candidates[0];
        }

        if (candidates.Count == 0)
        {
            output.WriteLine("错误：未在当前目录找到包含 fun main 的 .alice/.ailice 文件");
            output.WriteLine("已扫描：");
            foreach (var f in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                output.WriteLine("  " + Path.GetFileName(f));
            }
            return null;
        }

        output.WriteLine("错误：当前目录存在多个包含 fun main 的文件，请显式输入路径：");
        foreach (var f in candidates.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine("  " + Path.GetFileName(f));
        }
        return null;
    }

    private static bool HasTopLevelMain(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var lexer = new Lexer(text, filePath);
            var tokens = lexer.LexAll(out var lexDiags);
            if (lexDiags.Count > 0)
            {
                return false;
            }

            var parser = new Parser(tokens, filePath);
            var unit = parser.ParseCompilationUnit(out var parseDiags);
            if (parseDiags.Count == 0)
            {
                return unit.Functions.Any(f => string.Equals(f.Name, "main", StringComparison.Ordinal));
            }

            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.KwFun && tokens[i + 1].Kind == TokenKind.Identifier && tokens[i + 1].Text == "main")
                {
                    return true;
                }
                if (i + 2 < tokens.Count && tokens[i].Kind == TokenKind.KwAsync && tokens[i + 1].Kind == TokenKind.KwFun && tokens[i + 2].Kind == TokenKind.Identifier && tokens[i + 2].Text == "main")
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
