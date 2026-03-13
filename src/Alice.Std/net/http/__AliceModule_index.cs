namespace Alice.Std.net.http;

public static class __AliceModule_index
{
    public sealed class Request
    {
        public string method { get; set; } = "GET";
        public string url { get; set; } = "";
        public string[] headers { get; set; } = Array.Empty<string>();
        public string body { get; set; } = "";
    }

    public sealed class Response
    {
        public int status { get; set; }
        public string statusText { get; set; } = "";
        public string[] headers { get; set; } = Array.Empty<string>();
        public string body { get; set; } = "";
    }

    public static Response get(string url)
    {
        var req = new Request { method = "GET", url = url };
        return request(req);
    }

    public static string getText(string url)
    {
        return get(url).body;
    }

    public static Response postText(string url, string body)
    {
        var req = new Request { method = "POST", url = url, body = body };
        req.headers = new[] { "Content-Type: text/plain; charset=utf-8" };
        return request(req);
    }

    public static Response request(Request req)
    {
        using var client = new HttpClient();
        using var msg = new HttpRequestMessage(new HttpMethod(req.method), req.url);
        foreach (var h in req.headers ?? Array.Empty<string>())
        {
            var idx = h.IndexOf(':');
            if (idx <= 0) continue;
            var name = h.Substring(0, idx).Trim();
            var value = h.Substring(idx + 1).Trim();
            if (!msg.Headers.TryAddWithoutValidation(name, value))
            {
                msg.Content ??= new StringContent("", Encoding.UTF8);
                msg.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (!string.IsNullOrEmpty(req.body) && req.method != "GET" && req.method != "HEAD")
        {
            msg.Content = new StringContent(req.body, Encoding.UTF8);
        }

        var resp = client.Send(msg);
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var headers = resp.Headers.Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}")
            .Concat(resp.Content.Headers.Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}"))
            .ToArray();
        return new Response
        {
            status = (int)resp.StatusCode,
            statusText = resp.ReasonPhrase ?? "",
            headers = headers,
            body = body,
        };
    }
}
