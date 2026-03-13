namespace Alice.Std.time;

public static class __AliceModule_time
{
    public static long nowUnixMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static long nowUnixNs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
    }

    public static void sleepMs(int ms)
    {
        if (ms <= 0) return;
        Thread.Sleep(ms);
    }

    public sealed class Stopwatch
    {
        private readonly global::System.Diagnostics.Stopwatch _sw;

        public Stopwatch()
        {
            _sw = new global::System.Diagnostics.Stopwatch();
        }

        public void start()
        {
            _sw.Start();
        }

        public void stop()
        {
            _sw.Stop();
        }

        public void reset()
        {
            _sw.Reset();
        }

        public long elapsedMs()
        {
            return _sw.ElapsedMilliseconds;
        }

        public long elapsedNs()
        {
            return (long)(_sw.Elapsed.TotalMilliseconds * 1_000_000.0);
        }
    }
}
