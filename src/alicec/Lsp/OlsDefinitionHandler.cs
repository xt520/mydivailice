using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alice.Compiler;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace alicec.Lsp;

internal sealed class OlsDefinitionHandler : IDefinitionHandler
{
    private readonly OlsWorkspace _workspace;

    public OlsDefinitionHandler(OlsWorkspace workspace)
    {
        _workspace = workspace;
    }

    public Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (!_workspace.TryGet(request.TextDocument.Uri, out var doc) || doc is null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        var uri = request.TextDocument.Uri;
        var path = uri.GetFileSystemPath();
        var lexer = new Lexer(doc.Text, path);
        var tokens = lexer.LexAll(out _);
        var offset = OffsetFromPosition(doc.Text, request.Position.Line, request.Position.Character);
        var hit = tokens.FirstOrDefault(t => offset >= t.Span.Start && offset < t.Span.Start + t.Span.Length);
        if (hit is null || hit.Kind != TokenKind.Identifier) return Task.FromResult<LocationOrLocationLinks?>(null);

        var parser = new Parser(tokens, path);
        var unit = parser.ParseCompilationUnit(out var diags);
        if (diags.Count > 0) return Task.FromResult<LocationOrLocationLinks?>(null);

        var name = hit.Text;
        var defs = new List<(string Name, SourceSpan Span)>();
        defs.AddRange(unit.Functions.Select(f => (f.Name, f.Span)));
        defs.AddRange(unit.Classes.Select(c => (c.Name, c.Span)));
        defs.AddRange(unit.Structs.Select(s => (s.Name, s.Span)));
        defs.AddRange(unit.Enums.Select(e => (e.Name, e.Span)));
        defs.AddRange(unit.Interfaces.Select(i => (i.Name, i.Span)));
        defs.AddRange(unit.TypeAliases.Select(t => (t.Name, t.Span)));

        var def = defs.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(def.Name)) return Task.FromResult<LocationOrLocationLinks?>(null);

        var span = def.Span;
        var loc = new Location
        {
            Uri = uri,
            Range = new Range(
                new Position(Math.Max(0, span.Line - 1), Math.Max(0, span.Column - 1)),
                new Position(Math.Max(0, span.Line - 1), Math.Max(0, span.Column - 1) + 1))
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(loc));
    }

    public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = TextDocumentSelector.ForLanguage("alice") };

    private static int OffsetFromPosition(string text, int line0, int col0)
    {
        var line = 0;
        var col = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (line == line0 && col == col0) return i;
            if (text[i] == '\n')
            {
                line++;
                col = 0;
                continue;
            }
            col++;
        }
        return text.Length;
    }
}
