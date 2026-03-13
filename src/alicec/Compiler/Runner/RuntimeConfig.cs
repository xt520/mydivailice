namespace Alice.Compiler;

internal static class RuntimeConfig
{
    internal static string CreateNet8FrameworkDependent()
    {
        return """
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "8.0.0"
    }
  }
}
""";
    }
}
