namespace Alice.Std.sync;

public static class __AliceModule_index
{
    public sealed class Chan<T>
    {
        private readonly object _gate = new();
        private readonly int _cap;
        private readonly global::System.Collections.Generic.Queue<T> _buf;
        private bool _closed;

        private int _recvWaiters;
        private bool _hasRendezvous;
        private T _rendezvousValue = default!;

        internal Chan(int cap)
        {
            _cap = cap;
            _buf = new global::System.Collections.Generic.Queue<T>(cap <= 0 ? 1 : cap);
        }

        internal void Send(T v)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    throw new global::System.InvalidOperationException("channel closed");
                }

                if (_cap > 0)
                {
                    while (_buf.Count >= _cap)
                    {
                        global::System.Threading.Monitor.Wait(_gate);
                        if (_closed)
                        {
                            throw new global::System.InvalidOperationException("channel closed");
                        }
                    }

                    _buf.Enqueue(v);
                    global::System.Threading.Monitor.PulseAll(_gate);
                    return;
                }

                while (_hasRendezvous || _recvWaiters == 0)
                {
                    global::System.Threading.Monitor.Wait(_gate);
                    if (_closed)
                    {
                        throw new global::System.InvalidOperationException("channel closed");
                    }
                }

                _hasRendezvous = true;
                _rendezvousValue = v;
                global::System.Threading.Monitor.PulseAll(_gate);
                while (_hasRendezvous)
                {
                    global::System.Threading.Monitor.Wait(_gate);
                    if (_closed)
                    {
                        throw new global::System.InvalidOperationException("channel closed");
                    }
                }
            }
        }

        internal T Recv()
        {
            lock (_gate)
            {
                if (_cap > 0)
                {
                    while (_buf.Count == 0)
                    {
                        if (_closed)
                        {
                            throw new global::System.InvalidOperationException("channel closed");
                        }
                        global::System.Threading.Monitor.Wait(_gate);
                    }

                    var v = _buf.Dequeue();
                    global::System.Threading.Monitor.PulseAll(_gate);
                    return v;
                }

                _recvWaiters++;
                global::System.Threading.Monitor.PulseAll(_gate);
                try
                {
                    while (!_hasRendezvous)
                    {
                        if (_closed)
                        {
                            throw new global::System.InvalidOperationException("channel closed");
                        }
                        global::System.Threading.Monitor.Wait(_gate);
                    }

                    var v = _rendezvousValue;
                    _hasRendezvous = false;
                    global::System.Threading.Monitor.PulseAll(_gate);
                    return v;
                }
                finally
                {
                    _recvWaiters--;
                    global::System.Threading.Monitor.PulseAll(_gate);
                }
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                global::System.Threading.Monitor.PulseAll(_gate);
            }
        }
    }

    public static Chan<T> makeChan<T>(int cap)
    {
        if (cap < 0) throw new global::System.ArgumentOutOfRangeException(nameof(cap));
        return new Chan<T>(cap);
    }

    public static void send<T>(Chan<T> ch, T v)
    {
        ch.Send(v);
    }

    public static T recv<T>(Chan<T> ch)
    {
        return ch.Recv();
    }

    public static void close<T>(Chan<T> ch)
    {
        ch.Close();
    }
}
