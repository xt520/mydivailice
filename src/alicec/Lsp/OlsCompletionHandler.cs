using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alice.Compiler;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace alicec.Lsp;

internal sealed class OlsCompletionHandler : ICompletionHandler
{
    private static readonly string[] Keywords =
    {
        "namespace", "import", "as", "fun", "class", "struct", "enum", "interface",
        "public", "protected", "private", "static", "const", "extern", "type",
        "try", "except", "finally", "raise", "defer",
        "if", "else", "while", "return", "break", "continue",
        "async", "await", "go",
    };

    private readonly OlsWorkspace _workspace;

    public OlsCompletionHandler(OlsWorkspace workspace)
    {
        _workspace = workspace;
    }

    public Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var items = new List<CompletionItem>();
        items.AddRange(Keywords.Select(k => new CompletionItem { Label = k, Kind = CompletionItemKind.Keyword }));

        if (_workspace.TryGet(request.TextDocument.Uri, out var doc) && doc is not null)
        {
            var path = request.TextDocument.Uri.GetFileSystemPath();
            var lexer = new Lexer(doc.Text, path);
            var tokens = lexer.LexAll(out _);
            var parser = new Parser(tokens, path);
            var unit = parser.ParseCompilationUnit(out var diags);
            if (diags.Count == 0)
            {
                foreach (var f in unit.Functions) items.Add(new CompletionItem { Label = f.Name, Kind = CompletionItemKind.Function });
                foreach (var c in unit.Classes) items.Add(new CompletionItem { Label = c.Name, Kind = CompletionItemKind.Class });
                foreach (var s in unit.Structs) items.Add(new CompletionItem { Label = s.Name, Kind = CompletionItemKind.Struct });
                foreach (var e in unit.Enums) items.Add(new CompletionItem { Label = e.Name, Kind = CompletionItemKind.Enum });
                foreach (var i in unit.Interfaces) items.Add(new CompletionItem { Label = i.Name, Kind = CompletionItemKind.Interface });
                foreach (var t in unit.TypeAliases) items.Add(new CompletionItem { Label = t.Name, Kind = CompletionItemKind.TypeParameter });
            }
        }

        return Task.FromResult(new CompletionList(items, isIncomplete: false));
    }

    public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("alice"),
            TriggerCharacters = new Container<string>(".")
        };
}
