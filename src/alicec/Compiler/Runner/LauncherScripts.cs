namespace Alice.Compiler;

internal static class LauncherScripts
{
    internal static string CreateDotnetCmd(string exeFileName)
    {
        return $"""
@echo off
setlocal

dotnet "%~dp0{exeFileName}" %*

endlocal
""";
    }
}
