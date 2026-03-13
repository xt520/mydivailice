namespace Alice.Std.thread;

public static class __AliceModule_thread
{
    public sealed class JoinHandle
    {
        private readonly Thread _thread;

        internal JoinHandle(Thread thread)
        {
            _thread = thread;
        }

        public void Join()
        {
            _thread.Join();
        }
    }

    public static JoinHandle spawn(Action f)
    {
        var t = new Thread(() => f())
        {
            IsBackground = true,
        };
        t.Start();
        return new JoinHandle(t);
    }
}
