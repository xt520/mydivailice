namespace Alice.Std.net.tls;

public static class __AliceModule_index
{
    private sealed class TlsConn : global::Alice.Std.net.__AliceModule_index.Conn
    {
        private readonly global::Alice.Std.net.__AliceModule_index.Conn _inner;
        private readonly global::System.Net.Security.SslStream _ssl;

        internal TlsConn(global::Alice.Std.net.__AliceModule_index.Conn inner, global::System.Net.Security.SslStream ssl)
        {
            _inner = inner;
            _ssl = ssl;
        }

        public int Read(byte[] buf) => _ssl.Read(buf, 0, buf.Length);

        public int Write(byte[] buf)
        {
            _ssl.Write(buf, 0, buf.Length);
            return buf.Length;
        }

        public void Flush() => _ssl.Flush();

        public void Close()
        {
            _ssl.Dispose();
            _inner.Close();
        }

        public global::Alice.Std.net.__AliceModule_index.Addr LocalAddr() => _inner.LocalAddr();
        public global::Alice.Std.net.__AliceModule_index.Addr RemoteAddr() => _inner.RemoteAddr();
        public void SetReadTimeoutMs(int ms) => _inner.SetReadTimeoutMs(ms);
        public void SetWriteTimeoutMs(int ms) => _inner.SetWriteTimeoutMs(ms);
    }

    public static global::Alice.Std.net.__AliceModule_index.Conn wrap(global::Alice.Std.net.__AliceModule_index.Conn conn, string serverName)
    {
        var stream = new ConnStream(conn);
        var ssl = new global::System.Net.Security.SslStream(stream, leaveInnerStreamOpen: false, userCertificateValidationCallback: (_, _, _, _) => true);
        ssl.AuthenticateAsClient(serverName);
        return new TlsConn(conn, ssl);
    }

    public static global::Alice.Std.net.__AliceModule_index.Conn dial(string host, int port, string serverName)
    {
        var c = global::Alice.Std.net.__AliceModule_index.dialTcp(host, port);
        return wrap(c, serverName);
    }

    public static int Smoke()
    {
        return 1;
    }

    private sealed class ConnStream : global::System.IO.Stream
    {
        private readonly global::Alice.Std.net.__AliceModule_index.Conn _conn;

        internal ConnStream(global::Alice.Std.net.__AliceModule_index.Conn conn)
        {
            _conn = conn;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new global::System.NotSupportedException();
        public override long Position { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset == 0 && count == buffer.Length)
            {
                return _conn.Read(buffer);
            }
            var tmp = new byte[count];
            var n = _conn.Read(tmp);
            if (n > 0)
            {
                global::System.Buffer.BlockCopy(tmp, 0, buffer, offset, n);
            }
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (offset == 0 && count == buffer.Length)
            {
                _conn.Write(buffer);
                return;
            }
            var tmp = new byte[count];
            global::System.Buffer.BlockCopy(buffer, offset, tmp, 0, count);
            _conn.Write(tmp);
        }

        public override void Flush() => _conn.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _conn.Close();
            }
            base.Dispose(disposing);
        }

        public override long Seek(long offset, global::System.IO.SeekOrigin origin) => throw new global::System.NotSupportedException();
        public override void SetLength(long value) => throw new global::System.NotSupportedException();
    }
}
