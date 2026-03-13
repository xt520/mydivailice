using System;
using System.IO;
using System.Threading;

namespace alicec.ConsoleUi;

internal sealed class ProgressSpinner : IDisposable
{
    private readonly TextWriter _to;
    private readonly string _title;
    private readonly CancellationTokenSource _cts;
    private readonly Thread _thread;

    private volatile string? _detail;
    private volatile bool _completed;
    private volatile bool _failed;

    internal static bool IsEnabled(TextWriter to)
    {
        try
        {
            if (!ReferenceEquals(to, Console.Error)) return false;
            if (Console.IsErrorRedirected) return false;
            if (Console.IsOutputRedirected) return false;
            var v = Environment.GetEnvironmentVariable("ALICE_NO_PROGRESS");
            if (string.Equals(v, "1", StringComparison.Ordinal) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal ProgressSpinner(TextWriter to, string title)
    {
        _to = to;
        _title = title;
        _cts = new CancellationTokenSource();
        _thread = new Thread(Loop)
        {
            IsBackground = true
        };
        _thread.Start();
    }

    internal void SetDetail(string? detail)
    {
        _detail = detail;
    }

    internal void Complete()
    {
        _completed = true;
        Stop();
    }

    internal void Fail()
    {
        _failed = true;
        Stop();
    }

    private void Stop()
    {
        try { _cts.Cancel(); } catch { }
        try { _thread.Join(300); } catch { }
    }

    private void Loop()
    {
        var frames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var i = 0;
        while (!_cts.IsCancellationRequested)
        {
            var detail = _detail;
            var line = frames[i % frames.Length] + " " + _title + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " - " + detail);

            try
            {
                _to.Write("\r" + line);
            }
            catch
            {
            }

            i++;
            Thread.Sleep(80);
        }

        try
        {
            var suffix = _failed ? "失败" : "完成";
            var detail = _detail;
            var final = (_failed ? "✗" : "✓") + " " + _title + " " + suffix + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " - " + detail);
            _to.Write("\r" + final + new string(' ', 8) + "\n");
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (!_completed && !_failed)
        {
            Complete();
        }

        try { _cts.Dispose(); } catch { }
    }
}
