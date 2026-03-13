using System;
using System.Threading;

namespace alicec.ConsoleUi;

internal static class AstFx
{
    private static readonly string[] Frames = new[]
    {
        "▁","▂","▃","▄","▅","▆","▇","█","▇","▆","▅","▄","▃","▂"
    };

    internal static void Tick(string label, long value)
    {
        var frame = Frames[(int)(value % Frames.Length)];
        ProgressScope.Update($"{label} {frame} {value}");
    }

    internal static void SleepTiny()
    {
        Thread.Sleep(10);
    }
}
