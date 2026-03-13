namespace Alice.Std.net.http;

public static class __AliceModule_server
{
    public sealed class HttpContext
    {
        public __AliceModule_index.Request req { get; }

        public HttpContext(__AliceModule_index.Request req)
        {
            this.req = req;
        }
    }

    public sealed class HttpResponse
    {
        public int status { get; set; } = 200;
        public string[] headers { get; set; } = Array.Empty<string>();
        public string body { get; set; } = "";
    }

    public static int serveOnce(string host, int port, Func<__AliceModule_index.Request, HttpResponse> handler)
    {
        var ip = IPAddress.Parse(host);
        var listener = new TcpListener(ip, port);
        listener.Start();
        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(() =>
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                var req = ReadRequest(stream, host);
                var resp = handler(req);
                WriteResponse(stream, resp);
            }
            finally
            {
                listener.Stop();
            }
        });

        return actualPort;
    }

    private static __AliceModule_index.Request ReadRequest(NetworkStream stream, string host)
    {
        var headerBytes = new List<byte>(4096);
        var buf = new byte[1];
        while (true)
        {
            var n = stream.Read(buf, 0, 1);
            if (n <= 0) break;
            headerBytes.Add(buf[0]);
            var c = headerBytes.Count;
            if (c >= 4 && headerBytes[c - 4] == (byte)'\r' && headerBytes[c - 3] == (byte)'\n' && headerBytes[c - 2] == (byte)'\r' && headerBytes[c - 1] == (byte)'\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var first = lines.Length > 0 ? lines[0] : "";
        var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = parts.Length > 0 ? parts[0] : "GET";
        var path = parts.Length > 1 ? parts[1] : "/";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerLines = new List<string>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var name = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            headers[name] = value;
            headerLines.Add($"{name}: {value}");
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var lenText))
        {
            _ = int.TryParse(lenText, out contentLength);
        }

        var bodyBytes = Array.Empty<byte>();
        if (contentLength > 0)
        {
            bodyBytes = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var rn = stream.Read(bodyBytes, read, contentLength - read);
                if (rn <= 0) break;
                read += rn;
            }
            if (read != contentLength)
            {
                var shrink = new byte[read];
                Buffer.BlockCopy(bodyBytes, 0, shrink, 0, read);
                bodyBytes = shrink;
            }
        }

        var url = "http://" + host + path;
        return new __AliceModule_index.Request
        {
            method = method,
            url = url,
            headers = headerLines.ToArray(),
            body = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes),
        };
    }

    private static void WriteResponse(NetworkStream stream, HttpResponse resp)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(resp.body ?? "");
        var headers = new List<string>();
        if (resp.headers is not null)
        {
            headers.AddRange(resp.headers);
        }
        if (!headers.Any(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add($"Content-Length: {bodyBytes.Length}");
        }
        if (!headers.Any(h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add("Connection: close");
        }

        var statusText = resp.status switch
        {
            200 => "OK",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "OK",
        };

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {resp.status} {statusText}\r\n");
        foreach (var h in headers)
        {
            sb.Append(h);
            sb.Append("\r\n");
        }
        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }
}
