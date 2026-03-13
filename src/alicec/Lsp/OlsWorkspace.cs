using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace alicec.Lsp;

internal sealed record OlsDocumentState(DocumentUri Uri, string Text, int Version);

internal sealed class OlsWorkspace
{
    private readonly ConcurrentDictionary<DocumentUri, OlsDocumentState> _docs = new();

    public OlsDocumentState Upsert(DocumentUri uri, string text, int version)
        => _docs.AddOrUpdate(uri, _ => new OlsDocumentState(uri, text, version), (_, __) => new OlsDocumentState(uri, text, version));

    public bool TryGet(DocumentUri uri, out OlsDocumentState? doc) => _docs.TryGetValue(uri, out doc);

    public void Remove(DocumentUri uri) => _docs.TryRemove(uri, out _);
}
