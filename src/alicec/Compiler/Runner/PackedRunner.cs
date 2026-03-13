using System;
using System.IO;
using System.Threading.Tasks;

namespace Alice.Compiler;

internal static class PackedRunner
{
    internal static string GetMagic() => "ALICEPK1";

    internal static string GetBundledRunnersDir()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "runners"));

    internal static string GetBundledRunnerExeName(string rid, bool selfContained)
        => selfContained
            ? $"alice-runner-{rid}-selfcontained.exe"
            : $"alice-runner-{rid}.exe";

    internal static string? TryGetBundledRunnerPath(string rid, bool selfContained)
    {
        try
        {
            var dir = GetBundledRunnersDir();
            var p = Path.Combine(dir, GetBundledRunnerExeName(rid, selfContained));
            if (File.Exists(p)) return p;
        }
        catch
        {
        }
        return null;
    }

    internal static string? TryFindRunnerProjectPath()
    {
        try
        {
            var probe = string.IsNullOrWhiteSpace(AppContext.BaseDirectory) ? null : Path.GetFullPath(AppContext.BaseDirectory);
            for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(probe); i++)
            {
                var candidate1 = Path.Combine(probe, "src", "Alice.PackedRunner", "Alice.PackedRunner.csproj");
                if (File.Exists(candidate1)) return candidate1;

                var candidate2 = Path.Combine(probe, "Alice.PackedRunner", "Alice.PackedRunner.csproj");
                if (File.Exists(candidate2)) return candidate2;

                probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch
        {
        }

        return null;
    }

    internal static async Task<string?> PublishSingleFileRunnerAsync(
        string outputDir,
        string rid,
        bool selfContained,
        TextWriter diagnostics)
    {
        Directory.CreateDirectory(outputDir);

        var bundled = TryGetBundledRunnerPath(rid, selfContained);
        if (!string.IsNullOrWhiteSpace(bundled))
        {
            return bundled;
        }

        var proj = TryFindRunnerProjectPath();
        if (string.IsNullOrWhiteSpace(proj) || !File.Exists(proj))
        {
            await diagnostics.WriteLineAsync("错误：找不到 Alice.PackedRunner 项目").ConfigureAwait(false);
            await diagnostics.WriteLineAsync($"路径：{proj ?? "<null>"}").ConfigureAwait(false);
            return null;
        }

        var selfContainedArg = selfContained ? "true" : "false";
        var args = $"publish \"{proj}\" -c Release -r {rid} --self-contained {selfContainedArg} /p:PublishSingleFile=true /p:UseAppHost=true -o \"{outputDir}\"";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null)
        {
            await diagnostics.WriteLineAsync("错误：无法启动 dotnet 进程").ConfigureAwait(false);
            return null;
        }

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync().ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout)) await diagnostics.WriteLineAsync(stdout).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stderr)) await diagnostics.WriteLineAsync(stderr).ConfigureAwait(false);

        if (p.ExitCode != 0)
        {
            await diagnostics.WriteLineAsync($"错误：dotnet publish 失败，退出码 {p.ExitCode}").ConfigureAwait(false);
            return null;
        }

        var exe = FindFirstExecutable(outputDir);
        if (exe is null)
        {
            await diagnostics.WriteLineAsync("错误：未找到 runner 可执行文件").ConfigureAwait(false);
            return null;
        }

        return exe;
    }

    internal static void AppendPayload(string runnerExePath, byte[] payloadAssemblyBytes)
    {
        var magic = System.Text.Encoding.ASCII.GetBytes(GetMagic());
        var len = BitConverter.GetBytes(payloadAssemblyBytes.Length);

        using var fs = new FileStream(runnerExePath, FileMode.Append, FileAccess.Write, FileShare.None);
        fs.Write(payloadAssemblyBytes, 0, payloadAssemblyBytes.Length);
        fs.Write(len, 0, len.Length);
        fs.Write(magic, 0, magic.Length);
    }

    private static string? FindFirstExecutable(string outputDir)
    {
        foreach (var f in Directory.EnumerateFiles(outputDir))
        {
            var ext = Path.GetExtension(f);
            if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }

        foreach (var f in Directory.EnumerateFiles(outputDir))
        {
            var name = Path.GetFileName(f);
            if (!name.Contains('.', StringComparison.Ordinal) && new FileInfo(f).Length > 0)
            {
                return f;
            }
        }

        return null;
    }
}
