namespace Alice.Std.net;

public static class __AliceModule_index
{
    public interface Addr
    {
        string Network();
        string String();
        string Host();
        int Port();
    }

    public sealed class TcpAddr : Addr
    {
        public string host { get; }
        public int port { get; }

        public TcpAddr(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public string Network() => "tcp";
        public string String() => $"{host}:{port}";
        public string Host() => host;
        public int Port() => port;
    }

    public sealed class UdpAddr : Addr
    {
        public string host { get; }
        public int port { get; }

        public UdpAddr(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public string Network() => "udp";
        public string String() => $"{host}:{port}";
        public string Host() => host;
        public int Port() => port;
    }

    public interface Conn : Alice.Std.io.__AliceModule_io.Reader, Alice.Std.io.__AliceModule_io.Writer, Alice.Std.io.__AliceModule_io.Closer
    {
        Addr LocalAddr();
        Addr RemoteAddr();
        void SetReadTimeoutMs(int ms);
        void SetWriteTimeoutMs(int ms);
    }

    public interface Listener : Alice.Std.io.__AliceModule_io.Closer
    {
        Conn Accept();
        Addr Addr();
    }

    public sealed class Packet
    {
        public int n { get; }
        public Addr addr { get; }
        public byte[] data { get; }

        public Packet(int n, Addr addr, byte[] data)
        {
            this.n = n;
            this.addr = addr;
            this.data = data;
        }
    }

    public interface PacketConn : Alice.Std.io.__AliceModule_io.Closer
    {
        Addr LocalAddr();
        Packet ReadFrom(byte[] buf);
        int WriteTo(byte[] buf, Addr addr);
    }

    public static Conn dialTcp(string host, int port)
    {
        var client = new TcpClient();
        client.Connect(host, port);
        return new TcpConn(client);
    }

    public static Listener listenTcp(string host, int port)
    {
        var ip = IPAddress.Parse(host);
        var listener = new TcpListener(ip, port);
        listener.Start();
        return new TcpListenerWrapper(listener);
    }

    public static string[] lookupIp(string host)
    {
        var addrs = Dns.GetHostAddresses(host);
        return addrs.Select(a => a.ToString()).ToArray();
    }

    public static PacketConn listenUdp(string host, int port)
    {
        var ip = IPAddress.Parse(host);
        var udp = new UdpClient(new IPEndPoint(ip, port));
        return new UdpPacketConn(udp, connected: false);
    }

    public static PacketConn dialUdp(string host, int port)
    {
        var udp = new UdpClient();
        udp.Connect(host, port);
        return new UdpPacketConn(udp, connected: true);
    }

    private sealed class TcpConn : Conn
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        internal TcpConn(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public int Read(byte[] buf) => _stream.Read(buf, 0, buf.Length);

        public int Write(byte[] buf)
        {
            _stream.Write(buf, 0, buf.Length);
            return buf.Length;
        }

        public void Flush() => _stream.Flush();

        public void Close()
        {
            _stream.Dispose();
            _client.Dispose();
        }

        public Addr LocalAddr()
        {
            if (_client.Client.LocalEndPoint is IPEndPoint ep)
            {
                return new TcpAddr(ep.Address.ToString(), ep.Port);
            }
            return new TcpAddr("0.0.0.0", 0);
        }

        public Addr RemoteAddr()
        {
            if (_client.Client.RemoteEndPoint is IPEndPoint ep)
            {
                return new TcpAddr(ep.Address.ToString(), ep.Port);
            }
            return new TcpAddr("0.0.0.0", 0);
        }

        public void SetReadTimeoutMs(int ms)
        {
            _client.ReceiveTimeout = ms;
            _stream.ReadTimeout = ms;
        }

        public void SetWriteTimeoutMs(int ms)
        {
            _client.SendTimeout = ms;
            _stream.WriteTimeout = ms;
        }
    }

    private sealed class TcpListenerWrapper : Listener
    {
        private readonly TcpListener _listener;

        internal TcpListenerWrapper(TcpListener listener)
        {
            _listener = listener;
        }

        public Conn Accept()
        {
            var c = _listener.AcceptTcpClient();
            return new TcpConn(c);
        }

        public Addr Addr()
        {
            if (_listener.LocalEndpoint is IPEndPoint ep)
            {
                return new TcpAddr(ep.Address.ToString(), ep.Port);
            }
            return new TcpAddr("0.0.0.0", 0);
        }

        public void Close()
        {
            _listener.Stop();
        }
    }

    private sealed class UdpPacketConn : PacketConn
    {
        private readonly UdpClient _udp;
        private readonly bool _connected;

        internal UdpPacketConn(UdpClient udp, bool connected)
        {
            _udp = udp;
            _connected = connected;
        }

        public Addr LocalAddr()
        {
            if (_udp.Client.LocalEndPoint is IPEndPoint ep)
            {
                return new UdpAddr(ep.Address.ToString(), ep.Port);
            }
            return new UdpAddr("0.0.0.0", 0);
        }

        public Packet ReadFrom(byte[] buf)
        {
            IPEndPoint? remote = null;
            var bytes = _udp.Receive(ref remote);
            var n = Math.Min(buf.Length, bytes.Length);
            Buffer.BlockCopy(bytes, 0, buf, 0, n);
            var addr = remote is null ? (Addr)new UdpAddr("0.0.0.0", 0) : new UdpAddr(remote.Address.ToString(), remote.Port);
            var dataCopy = new byte[n];
            Buffer.BlockCopy(buf, 0, dataCopy, 0, n);
            return new Packet(n, addr, dataCopy);
        }

        public int WriteTo(byte[] buf, Addr addr)
        {
            if (_connected)
            {
                return _udp.Send(buf, buf.Length);
            }
            var ip = IPAddress.Parse(addr.Host());
            return _udp.Send(buf, buf.Length, new IPEndPoint(ip, addr.Port()));
        }

        public void Close()
        {
            _udp.Dispose();
        }
    }
}
