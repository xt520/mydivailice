using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Alice.Compiler;

internal static class AliceRunner
{
    internal static int RunInProcess(byte[] assemblyBytes, string[] args)
    {
        var alc = new CollectibleAlc();
        try
        {
            using var peStream = new MemoryStream(assemblyBytes);
            var asm = alc.LoadFromStream(peStream);
            var entry = asm.EntryPoint;
            if (entry is null)
            {
                Console.Error.WriteLine("找不到入口点");
                return 1;
            }

            var parameters = entry.GetParameters();
            object? invokeArg;
            if (parameters.Length == 0)
            {
                invokeArg = Array.Empty<object>();
                entry.Invoke(null, null);
            }
            else
            {
                invokeArg = new object?[] { args };
                entry.Invoke(null, (object?[]?)invokeArg);
            }

            return 0;
        }
        finally
        {
            alc.Unload();
            var alcRef = new WeakReference(alc);
            for (var i = 0; alcRef.IsAlive && i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    private sealed class CollectibleAlc : AssemblyLoadContext
    {
        public CollectibleAlc() : base(isCollectible: true)
        {
            Resolving += (_, name) => TryResolve(name);
        }

        private static Assembly? TryResolve(AssemblyName name)
        {
            if (string.Equals(name.Name, "Alice.Std", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = typeof(global::Alice.Std.io.__AliceModule_io).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                }
            }
            return null;
        }

        protected override Assembly? Load(AssemblyName name)
        {
            return null;
        }
    }
}
