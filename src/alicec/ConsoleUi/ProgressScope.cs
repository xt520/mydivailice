using System;
using System.IO;

namespace alicec.ConsoleUi;

internal static class ProgressScope
{
    [ThreadStatic]
    private static ProgressSpinner? _spinner;

    internal static IDisposable? Begin(TextWriter to, string title, string? detail = null)
    {
        if (!ProgressSpinner.IsEnabled(to))
        {
            return null;
        }

        var prev = _spinner;
        var next = new ProgressSpinner(to, title);
        if (!string.IsNullOrWhiteSpace(detail)) next.SetDetail(detail);
        _spinner = next;
        return new Scope(prev, next);
    }

    internal static void Update(string? detail)
    {
        _spinner?.SetDetail(detail);
    }

    internal static void Complete()
    {
        _spinner?.Complete();
    }

    internal static void Fail()
    {
        _spinner?.Fail();
    }

    private sealed class Scope : IDisposable
    {
        private readonly ProgressSpinner? _prev;
        private readonly ProgressSpinner _current;
        private bool _done;

        public Scope(ProgressSpinner? prev, ProgressSpinner current)
        {
            _prev = prev;
            _current = current;
        }

        public void Dispose()
        {
            if (_done) return;
            _done = true;

            try { _current.Complete(); } catch { }
            _spinner = _prev;
        }
    }
}
