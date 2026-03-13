namespace Alice.Std.io;

public static class __AliceModule_io
{
    public const int OPEN_READ = 1;
    public const int OPEN_WRITE = 2;
    public const int OPEN_APPEND = 4;
    public const int OPEN_CREATE = 8;
    public const int OPEN_TRUNC = 16;

    public const int SEEK_SET = 0;
    public const int SEEK_CUR = 1;
    public const int SEEK_END = 2;

    public interface Reader
    {
        int Read(byte[] buf);
    }

    public interface Writer
    {
        int Write(byte[] buf);
        void Flush();
    }

    public interface Closer
    {
        void Close();
    }

    public interface ReadCloser : Reader, Closer
    {
    }

    public interface WriteCloser : Writer, Closer
    {
    }

    public interface ReadWriteCloser : Reader, Writer, Closer
    {
    }

    public sealed class File : ReadWriteCloser
    {
        private readonly FileStream _stream;

        public string path { get; }

        internal File(string path, FileStream stream)
        {
            this.path = path;
            _stream = stream;
        }

        public int Read(byte[] buf) => _stream.Read(buf, 0, buf.Length);

        public int Write(byte[] buf)
        {
            _stream.Write(buf, 0, buf.Length);
            return buf.Length;
        }

        public void Flush() => _stream.Flush();

        public void Close() => _stream.Dispose();

        public long Seek(long offset, int whence)
        {
            var origin = whence switch
            {
                SEEK_SET => SeekOrigin.Begin,
                SEEK_CUR => SeekOrigin.Current,
                SEEK_END => SeekOrigin.End,
                _ => SeekOrigin.Begin,
            };
            return _stream.Seek(offset, origin);
        }

        public long Tell() => _stream.Position;
    }

    private sealed class StreamReaderAdapter : Reader
    {
        private readonly Stream _stream;

        public StreamReaderAdapter(Stream stream)
        {
            _stream = stream;
        }

        public int Read(byte[] buf) => _stream.Read(buf, 0, buf.Length);
    }

    private sealed class StreamWriterAdapter : Writer
    {
        private readonly Stream _stream;

        public StreamWriterAdapter(Stream stream)
        {
            _stream = stream;
        }

        public int Write(byte[] buf)
        {
            _stream.Write(buf, 0, buf.Length);
            return buf.Length;
        }

        public void Flush() => _stream.Flush();
    }

    public static File open(string path, int flags)
    {
        var mode = (flags & OPEN_CREATE) != 0 ? FileMode.OpenOrCreate : FileMode.Open;
        if ((flags & OPEN_TRUNC) != 0) mode = FileMode.Create;
        var access = (flags & OPEN_WRITE) != 0 ? FileAccess.ReadWrite : FileAccess.Read;
        var share = FileShare.ReadWrite;
        var stream = new FileStream(path, mode, access, share);
        if ((flags & OPEN_APPEND) != 0)
        {
            stream.Seek(0, SeekOrigin.End);
        }
        return new File(path, stream);
    }

    public static File create(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
        return new File(path, stream);
    }

    public static bool exists(string path) => System.IO.File.Exists(path) || Directory.Exists(path);

    public static void remove(string path)
    {
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
            return;
        }
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public static void mkdir(string path) => Directory.CreateDirectory(path);

    public static byte[] readAllBytes(string path) => System.IO.File.ReadAllBytes(path);

    public static void writeAllBytes(string path, byte[] data) => System.IO.File.WriteAllBytes(path, data);

    public static string readAllText(string path) => System.IO.File.ReadAllText(path, Encoding.UTF8);

    public static void writeAllText(string path, string text) => System.IO.File.WriteAllText(path, text, Encoding.UTF8);

    public static string? readLine() => Console.ReadLine();

    public static Reader stdin() => new StreamReaderAdapter(Console.OpenStandardInput());

    public static Writer stdout() => new StreamWriterAdapter(Console.OpenStandardOutput());

    public static Writer stderr() => new StreamWriterAdapter(Console.OpenStandardError());

    public static byte[] utf8Encode(string s) => Encoding.UTF8.GetBytes(s);

    public static string utf8Decode(byte[] b) => Encoding.UTF8.GetString(b);

    public static string utf8DecodeN(byte[] b, int n)
    {
        if (n <= 0) return string.Empty;
        if (n >= b.Length) return Encoding.UTF8.GetString(b);
        return Encoding.UTF8.GetString(b, 0, n);
    }

    public static long copy(Writer dst, Reader src)
    {
        var buf = new byte[16 * 1024];
        long total = 0;
        while (true)
        {
            var n = src.Read(buf);
            if (n <= 0) break;
            var chunk = buf;
            if (n != buf.Length)
            {
                chunk = new byte[n];
                Buffer.BlockCopy(buf, 0, chunk, 0, n);
            }
            dst.Write(chunk);
            total += n;
        }
        dst.Flush();
        return total;
    }
}
