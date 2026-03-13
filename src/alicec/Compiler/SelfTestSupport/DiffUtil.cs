using System;
using System.Linq;

namespace Alice.Compiler;

internal static class DiffUtil
{
    internal static string FormatFirstDifference(string expected, string actual)
    {
        var e = expected.Replace("\r\n", "\n").Replace("\r", "\n");
        var a = actual.Replace("\r\n", "\n").Replace("\r", "\n");

        var el = e.Split('\n');
        var al = a.Split('\n');
        var n = Math.Min(el.Length, al.Length);
        for (var i = 0; i < n; i++)
        {
            if (!string.Equals(el[i], al[i], StringComparison.Ordinal))
            {
                return $"第 {i + 1} 行不一致: expected='{el[i]}' actual='{al[i]}'";
            }
        }

        if (el.Length != al.Length)
        {
            return $"行数不一致: expected={el.Length} actual={al.Length}";
        }

        return "一致";
    }
}
