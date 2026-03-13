namespace Alice.Std.crypto;

public static class __AliceModule_index
{
    public static byte[] sha256(byte[] data)
    {
        return global::System.Security.Cryptography.SHA256.HashData(data);
    }

    public static byte[] hmacSha256(byte[] key, byte[] data)
    {
        using var h = new global::System.Security.Cryptography.HMACSHA256(key);
        return h.ComputeHash(data);
    }

    public static byte[] randomBytes(int n)
    {
        var b = new byte[n];
        global::System.Security.Cryptography.RandomNumberGenerator.Fill(b);
        return b;
    }

    public static int randomInt(int min, int max)
    {
        return global::System.Security.Cryptography.RandomNumberGenerator.GetInt32(min, max);
    }

    public sealed class Random
    {
        private readonly global::System.Random _r;

        public Random(long seed)
        {
            _r = new global::System.Random(unchecked((int)seed));
        }

        public int nextInt(int min, int max)
        {
            return _r.Next(min, max);
        }

        public byte[] nextBytes(int n)
        {
            var b = new byte[n];
            _r.NextBytes(b);
            return b;
        }
    }
}
