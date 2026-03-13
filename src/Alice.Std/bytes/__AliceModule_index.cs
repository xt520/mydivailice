namespace Alice.Std.bytes;

public static class __AliceModule_index
{
    public static ushort readU16BE(byte[] b, int off)
    {
        return (ushort)((b[off] << 8) | b[off + 1]);
    }

    public static uint readU32BE(byte[] b, int off)
    {
        return ((uint)b[off] << 24) | ((uint)b[off + 1] << 16) | ((uint)b[off + 2] << 8) | b[off + 3];
    }

    public static void writeU32BE(byte[] b, int off, uint v)
    {
        b[off] = (byte)((v >> 24) & 0xFF);
        b[off + 1] = (byte)((v >> 16) & 0xFF);
        b[off + 2] = (byte)((v >> 8) & 0xFF);
        b[off + 3] = (byte)(v & 0xFF);
    }

    public static byte[] concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        global::System.Buffer.BlockCopy(a, 0, r, 0, a.Length);
        global::System.Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }
}
