namespace Alice.Std.async;

public static class __AliceModule_index
{
    public static global::System.Threading.Tasks.Task DelayMs(int ms)
    {
        return global::System.Threading.Tasks.Task.Delay(ms);
    }

    public static global::System.Threading.Tasks.Task<TOut> Then<TIn, TOut>(global::System.Threading.Tasks.Task<TIn> p, global::System.Func<TIn, TOut> f)
    {
        return p.ContinueWith(t => f(t.GetAwaiter().GetResult()));
    }

    public static global::System.Threading.Tasks.Task<TOut> Then<TIn, TOut>(global::System.Threading.Tasks.Task<TIn> p, global::System.Func<TIn, global::System.Threading.Tasks.Task<TOut>> f)
    {
        return p.ContinueWith(t => f(t.GetAwaiter().GetResult())).Unwrap();
    }

    public static global::System.Threading.Tasks.Task Catch(global::System.Threading.Tasks.Task p, global::System.Func<global::System.Exception, global::System.Threading.Tasks.Task> f)
    {
        return p.ContinueWith(t => t.IsFaulted ? f(t.Exception!.InnerException ?? t.Exception) : global::System.Threading.Tasks.Task.CompletedTask).Unwrap();
    }

    public static global::System.Threading.Tasks.Task<T> Catch<T>(global::System.Threading.Tasks.Task<T> p, global::System.Func<global::System.Exception, T> f)
    {
        return p.ContinueWith(t => t.IsFaulted ? f(t.Exception!.InnerException ?? t.Exception) : t.GetAwaiter().GetResult());
    }

    public static global::System.Threading.Tasks.Task Finally(global::System.Threading.Tasks.Task p, global::System.Action f)
    {
        return p.ContinueWith(t =>
        {
            f();
            if (t.IsFaulted)
            {
                return global::System.Threading.Tasks.Task.FromException(t.Exception!.InnerException ?? t.Exception);
            }
            if (t.IsCanceled)
            {
                return global::System.Threading.Tasks.Task.FromCanceled(new global::System.Threading.CancellationToken(canceled: true));
            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }).Unwrap();
    }

    public sealed class CancelSource
    {
        internal readonly global::System.Threading.CancellationTokenSource Cts;

        internal CancelSource(global::System.Threading.CancellationTokenSource cts)
        {
            Cts = cts;
        }
    }

    public sealed class CancelToken
    {
        internal readonly global::System.Threading.CancellationToken Token;

        internal CancelToken(global::System.Threading.CancellationToken token)
        {
            Token = token;
        }
    }

    public static CancelSource newCancelSource()
    {
        return new CancelSource(new global::System.Threading.CancellationTokenSource());
    }

    public static void cancel(CancelSource src)
    {
        src.Cts.Cancel();
    }

    public static CancelToken token(CancelSource src)
    {
        return new CancelToken(src.Cts.Token);
    }

    public static global::System.Threading.Tasks.Task<T> withTimeout<T>(global::System.Threading.Tasks.Task<T> p, int ms)
    {
        return WithTimeoutImpl(p, ms);
    }

    private static async global::System.Threading.Tasks.Task<T> WithTimeoutImpl<T>(global::System.Threading.Tasks.Task<T> p, int ms)
    {
        if (ms < 0) throw new global::System.ArgumentOutOfRangeException(nameof(ms));
        var delay = global::System.Threading.Tasks.Task.Delay(ms);
        var completed = await global::System.Threading.Tasks.Task.WhenAny(p, delay).ConfigureAwait(false);
        if (completed == delay)
        {
            throw new global::System.TimeoutException();
        }
        return await p.ConfigureAwait(false);
    }
}
