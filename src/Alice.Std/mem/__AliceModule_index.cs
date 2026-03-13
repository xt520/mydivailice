namespace Alice.Std.mem;

public static class __AliceModule_index
{
    public sealed class NativeBuffer
    {
        public nint ptr { get; }
        public int len { get; }

        internal NativeBuffer(nint ptr, int len)
        {
            this.ptr = ptr;
            this.len = len;
        }
    }

    public static NativeBuffer alloc(int n)
    {
        var p = global::System.Runtime.InteropServices.Marshal.AllocHGlobal(n);
        return new NativeBuffer(p, n);
    }

    public static void free(NativeBuffer b)
    {
        global::System.Runtime.InteropServices.Marshal.FreeHGlobal(b.ptr);
    }

    public static void memcpy(NativeBuffer dst, int dstOff, byte[] src, int srcOff, int n)
    {
        if (n < 0) throw new global::System.ArgumentOutOfRangeException(nameof(n));
        if (dstOff < 0 || dstOff + n > dst.len) throw new global::System.ArgumentOutOfRangeException(nameof(dstOff));
        if (srcOff < 0 || srcOff + n > src.Length) throw new global::System.ArgumentOutOfRangeException(nameof(srcOff));

        global::System.Runtime.InteropServices.Marshal.Copy(src, srcOff, dst.ptr + dstOff, n);
    }

    public static void memcpyToArray(NativeBuffer src, int srcOff, byte[] dst, int dstOff, int n)
    {
        if (n < 0) throw new global::System.ArgumentOutOfRangeException(nameof(n));
        if (srcOff < 0 || srcOff + n > src.len) throw new global::System.ArgumentOutOfRangeException(nameof(srcOff));
        if (dstOff < 0 || dstOff + n > dst.Length) throw new global::System.ArgumentOutOfRangeException(nameof(dstOff));

        global::System.Runtime.InteropServices.Marshal.Copy(src.ptr + srcOff, dst, dstOff, n);
    }

    public static unsafe byte* ptrU8(NativeBuffer b, int off)
    {
        if (off < 0 || off > b.len) throw new global::System.ArgumentOutOfRangeException(nameof(off));
        return (byte*)b.ptr + off;
    }

    public static unsafe T* ptr<T>(NativeBuffer b, int off) where T : unmanaged
    {
        if (off < 0 || off > b.len) throw new global::System.ArgumentOutOfRangeException(nameof(off));
        return (T*)((byte*)b.ptr + off);
    }

    public static unsafe void memcpyPtr(byte* dst, byte* src, int n)
    {
        if (n < 0) throw new global::System.ArgumentOutOfRangeException(nameof(n));
        global::System.Buffer.MemoryCopy(src, dst, n, n);
    }
}
