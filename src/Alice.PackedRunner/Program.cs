using System.Reflection;
using System.Runtime.Loader;

namespace Alice.PackedRunner;

internal static class Program
{
    private static readonly byte[] Magic = "ALICEPK1"u8.ToArray();

    public static int Main(string[] args)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Console.Error.WriteLine("错误：无法定位当前可执行文件路径");
                return 1;
            }

            var payload = TryReadPayload(exePath);
            if (payload is null)
            {
                Console.Error.WriteLine("错误：该可执行文件未包含 Alice 打包的程序负载");
                return 1;
            }

            return RunPayload(payload, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static byte[]? TryReadPayload(string exePath)
    {
        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < Magic.Length + sizeof(int))
        {
            return null;
        }

        fs.Seek(-Magic.Length, SeekOrigin.End);
        var magicBuf = new byte[Magic.Length];
        if (fs.Read(magicBuf, 0, magicBuf.Length) != magicBuf.Length)
        {
            return null;
        }

        if (!magicBuf.SequenceEqual(Magic))
        {
            return null;
        }

        fs.Seek(-(Magic.Length + sizeof(int)), SeekOrigin.End);
        Span<byte> lenBuf = stackalloc byte[sizeof(int)];
        if (fs.Read(lenBuf) != lenBuf.Length)
        {
            return null;
        }

        var payloadLen = BitConverter.ToInt32(lenBuf);
        if (payloadLen <= 0)
        {
            return null;
        }

        var payloadStart = fs.Length - Magic.Length - sizeof(int) - payloadLen;
        if (payloadStart < 0)
        {
            return null;
        }

        fs.Seek(payloadStart, SeekOrigin.Begin);
        var payload = new byte[payloadLen];
        var read = 0;
        while (read < payload.Length)
        {
            var n = fs.Read(payload, read, payload.Length - read);
            if (n <= 0)
            {
                return null;
            }
            read += n;
        }

        return payload;
    }

    private static int RunPayload(byte[] assemblyBytes, string[] args)
    {
        using var peStream = new MemoryStream(assemblyBytes);
        var alc = new AssemblyLoadContext("alice-packed", isCollectible: true);
        alc.Resolving += (_, name) => TryResolveStd(name);

        var asm = alc.LoadFromStream(peStream);
        var entry = asm.EntryPoint;
        if (entry is null)
        {
            Console.Error.WriteLine("错误：负载程序集没有入口点");
            return 1;
        }

        var parameters = entry.GetParameters();
        if (parameters.Length == 0)
        {
            entry.Invoke(null, null);
            return 0;
        }

        entry.Invoke(null, new object?[] { args });
        return 0;
    }

    private static Assembly? TryResolveStd(AssemblyName name)
    {
        if (!string.Equals(name.Name, "Alice.Std", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return typeof(global::Alice.Std.io.__AliceModule_io).Assembly;
    }
}
