namespace Alice.Std.slice;

public static class __AliceModule_index
{
    public sealed class Slice<T>
    {
        public T[] arr { get; }
        public int off { get; }
        public int len { get; }
        public int cap { get; }

        internal Slice(T[] arr, int off, int len, int cap)
        {
            this.arr = arr;
            this.off = off;
            this.len = len;
            this.cap = cap;
        }

        public T Get(int i) => arr[off + i];
        public void Set(int i, T v) => arr[off + i] = v;
        public int Len() => len;
        public int Cap() => cap;
        public T[] ToArray()
        {
            var r = new T[len];
            global::System.Array.Copy(arr, off, r, 0, len);
            return r;
        }
    }

    public static Slice<T> FromArray<T>(T[] a)
    {
        return new Slice<T>(a, 0, a.Length, a.Length);
    }

    public static Slice<T> SliceArray<T>(T[] a, int lo, int hi)
    {
        var len = hi - lo;
        var cap = a.Length - lo;
        return new Slice<T>(a, lo, len, cap);
    }

    public static Slice<T> SliceSlice<T>(Slice<T> s, int lo, int hi)
    {
        var len = hi - lo;
        var cap = s.cap - lo;
        return new Slice<T>(s.arr, s.off + lo, len, cap);
    }

    public static Slice<T> Append<T>(Slice<T> s, T v)
    {
        if (s.len < s.cap)
        {
            s.arr[s.off + s.len] = v;
            return new Slice<T>(s.arr, s.off, s.len + 1, s.cap);
        }

        var newCap = s.cap == 0 ? 4 : s.cap * 2;
        var a2 = new T[newCap];
        global::System.Array.Copy(s.arr, s.off, a2, 0, s.len);
        a2[s.len] = v;
        return new Slice<T>(a2, 0, s.len + 1, newCap);
    }
}
