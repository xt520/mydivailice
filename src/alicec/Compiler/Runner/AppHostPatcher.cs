using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Alice.Compiler;

internal static class AppHostPatcher
{
    internal static void CreatePatchedAppHost(string templateExePath, string destExePath, string entryDllFileName)
    {
        if (string.IsNullOrWhiteSpace(templateExePath)) throw new ArgumentException(nameof(templateExePath));
        if (string.IsNullOrWhiteSpace(destExePath)) throw new ArgumentException(nameof(destExePath));
        if (string.IsNullOrWhiteSpace(entryDllFileName)) throw new ArgumentException(nameof(entryDllFileName));
        if (!entryDllFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("entryDllFileName 必须以 .dll 结尾");

        var bytes = File.ReadAllBytes(templateExePath);
        var oldName = "alicec.dll";
        var oldBytes = Encoding.UTF8.GetBytes(oldName);
        var newBytes = Encoding.UTF8.GetBytes(entryDllFileName);
        if (newBytes.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(entryDllFileName), "dll 文件名过长");
        }

        var patched = false;

        for (var i = 0; i + oldBytes.Length + 32 < bytes.Length; i++)
        {
            if (!Matches(bytes, i, oldBytes)) continue;

            var nullPos = i + oldBytes.Length;
            if (bytes[nullPos] != 0) continue;

            var zerosAfter = bytes.Skip(nullPos).Take(32).All(b => b == 0);
            if (!zerosAfter) continue;

            Array.Clear(bytes, i, 1024);
            Array.Copy(newBytes, 0, bytes, i, newBytes.Length);
            bytes[i + newBytes.Length] = 0;
            patched = true;
            break;
        }

        if (!patched)
        {
            throw new InvalidOperationException("未能在 apphost 模板中定位入口 dll 名称占位区（alicec.dll）");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destExePath))!);
        File.WriteAllBytes(destExePath, bytes);
    }

    private static bool Matches(byte[] haystack, int offset, byte[] needle)
    {
        for (var i = 0; i < needle.Length; i++)
        {
            if (haystack[offset + i] != needle[i]) return false;
        }
        return true;
    }
}
